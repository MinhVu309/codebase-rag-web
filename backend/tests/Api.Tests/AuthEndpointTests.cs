using System.Net;
using System.Net.Http.Json;
using Domain.Entities;
using Infrastructure.Auth;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthEndpointTests(ApiFactory factory)
{
    private const string Password = "S3cret-Passw0rd!";

    private record UserBody(Guid Id, string Email, DateTimeOffset CreatedAt);

    private async Task<(AuthTestClient Client, string Email)> RegisterAsync(string? email = null)
    {
        var client = factory.CreateAuthClient();
        email ??= AuthTestClient.NewEmail();
        var response = await client.PostAsync("/api/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (client, email);
    }

    // ---------- register ----------

    [Fact]
    public async Task Register_Creates_User_And_Sets_HttpOnly_SameSiteStrict_Cookies()
    {
        var client = factory.CreateAuthClient();
        var email = AuthTestClient.NewEmail();

        var response = await client.PostAsync("/api/auth/register", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserBody>();
        Assert.Equal(email, user!.Email);

        var access = Assert.Single(client.LastSetCookies, c => c.Name == "access_token");
        var refresh = Assert.Single(client.LastSetCookies, c => c.Name == "refresh_token");
        foreach (var cookie in new[] { access, refresh })
        {
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        }
        Assert.Equal("/", access.Path.Value);
        Assert.Equal("/api/auth", refresh.Path.Value);
    }

    [Fact]
    public async Task Register_Stores_Hashed_Password_And_Hashed_RefreshToken()
    {
        var (client, email) = await RegisterAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(u => u.RefreshTokens).SingleAsync(u => u.Email == email);

        Assert.NotEqual(Password, user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash);
        var stored = Assert.Single(user.RefreshTokens);
        Assert.NotEqual(client.Cookies["refresh_token"], stored.TokenHash);
    }

    [Fact]
    public async Task Register_Normalizes_Email_And_Rejects_Duplicates_Case_Insensitively()
    {
        var email = AuthTestClient.NewEmail();
        await RegisterAsync(email);

        var second = factory.CreateAuthClient();
        var response = await second.PostAsync("/api/auth/register",
            new { email = "  " + email.ToUpperInvariant() + " ", password = Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem!.Status);
    }

    [Theory]
    [InlineData("not-an-email", "S3cret-Passw0rd!")]
    [InlineData("a@example.com", "short")]
    [InlineData("", "S3cret-Passw0rd!")]
    public async Task Register_Rejects_Invalid_Input_With_400_ProblemDetails(string email, string password)
    {
        var client = factory.CreateAuthClient();

        var response = await client.PostAsync("/api/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotEmpty(problem!.Errors);
        Assert.Empty(client.Cookies);
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_With_Valid_Credentials_Sets_Cookies()
    {
        var (_, email) = await RegisterAsync();
        var client = factory.CreateAuthClient();

        var response = await client.PostAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("access_token", client.Cookies.Keys);
        Assert.Contains("refresh_token", client.Cookies.Keys);
    }

    [Fact]
    public async Task Login_Wrong_Password_And_Unknown_Email_Look_Identical()
    {
        var (_, email) = await RegisterAsync();
        var client = factory.CreateAuthClient();

        var wrongPassword = await client.PostAsync("/api/auth/login", new { email, password = "Wrong-Passw0rd!" });
        var unknownEmail = await client.PostAsync("/api/auth/login",
            new { email = AuthTestClient.NewEmail(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var a = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>();
        var b = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(a!.Detail, b!.Detail);
        Assert.Empty(client.Cookies);
    }

    // ---------- me / access token ----------

    [Fact]
    public async Task Me_With_Cookie_Returns_Current_User()
    {
        var (client, email) = await RegisterAsync();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, (await response.Content.ReadFromJsonAsync<UserBody>())!.Email);
    }

    [Fact]
    public async Task Me_Without_Cookie_Returns_401()
    {
        var response = await factory.CreateAuthClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_With_Expired_Access_Token_Returns_401()
    {
        var (client, email) = await RegisterAsync();
        client.Cookies["access_token"] = await MintAccessTokenAsync(email, DateTimeOffset.UtcNow.AddHours(-1));

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_With_Token_Signed_By_Another_Key_Returns_401()
    {
        var (client, email) = await RegisterAsync();
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Issuer = ApiFactory.JwtIssuer,
            Audience = ApiFactory.JwtAudience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                JwtTokenService.CreateKey("another-secret-another-secret-another-secret"), SecurityAlgorithms.HmacSha256),
        });
        client.Cookies["access_token"] = forged;

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _ = email;
    }

    // ---------- refresh ----------

    [Fact]
    public async Task Refresh_Rotates_Token_And_Old_Token_Is_Rejected()
    {
        var (client, _) = await RegisterAsync();
        var oldRefresh = client.Cookies["refresh_token"];

        var response = await client.PostAsync("/api/auth/refresh");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(oldRefresh, client.Cookies["refresh_token"]);

        var replay = factory.CreateAuthClient();
        replay.Cookies["refresh_token"] = oldRefresh;
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsync("/api/auth/refresh")).StatusCode);
    }

    [Fact]
    public async Task Reusing_A_Rotated_Refresh_Token_Revokes_The_Whole_Session()
    {
        var (client, _) = await RegisterAsync();
        var stolen = client.Cookies["refresh_token"];
        await client.PostAsync("/api/auth/refresh"); // legitimate rotation
        var legitimateNew = client.Cookies["refresh_token"];

        var attacker = factory.CreateAuthClient();
        attacker.Cookies["refresh_token"] = stolen;
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.PostAsync("/api/auth/refresh")).StatusCode);

        var victim = factory.CreateAuthClient();
        victim.Cookies["refresh_token"] = legitimateNew;
        Assert.Equal(HttpStatusCode.Unauthorized, (await victim.PostAsync("/api/auth/refresh")).StatusCode);
    }

    [Fact]
    public async Task Refresh_With_Expired_Token_Returns_401()
    {
        var (client, email) = await RegisterAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RefreshTokens.Where(t => t.User.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var response = await client.PostAsync("/api/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Without_Cookie_Returns_401()
    {
        var response = await factory.CreateAuthClient().PostAsync("/api/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_Refresh_With_Same_Token_Succeeds_At_Most_Once()
    {
        var (client, _) = await RegisterAsync();
        var token = client.Cookies["refresh_token"];

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            var c = factory.CreateAuthClient();
            c.Cookies["refresh_token"] = token;
            return (await c.PostAsync("/api/auth/refresh")).StatusCode;
        }));

        Assert.True(attempts.Count(s => s == HttpStatusCode.OK) <= 1);
    }

    // ---------- logout ----------

    [Fact]
    public async Task Logout_Revokes_Refresh_Token_And_Clears_Cookies()
    {
        var (client, _) = await RegisterAsync();
        var refresh = client.Cookies["refresh_token"];

        var response = await client.PostAsync("/api/auth/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(client.Cookies);

        var after = factory.CreateAuthClient();
        after.Cookies["refresh_token"] = refresh;
        Assert.Equal(HttpStatusCode.Unauthorized, (await after.PostAsync("/api/auth/refresh")).StatusCode);
    }

    [Fact]
    public async Task Logout_Without_Session_Is_Idempotent()
    {
        var response = await factory.CreateAuthClient().PostAsync("/api/auth/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---------- secrets never leak ----------

    [Fact]
    public async Task Password_And_Hashes_Never_Appear_In_Responses_Or_Logs()
    {
        var (client, email) = await RegisterAsync();
        var bodies = new List<string>();
        bodies.Add(await (await client.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        bodies.Add(await (await client.PostAsync("/api/auth/login", new { email, password = Password })).Content.ReadAsStringAsync());
        bodies.Add(await (await factory.CreateAuthClient().PostAsync("/api/auth/login", new { email, password = "Wrong-Passw0rd!" })).Content.ReadAsStringAsync());
        bodies.Add(await (await factory.CreateAuthClient().PostAsync("/api/auth/register", new { email, password = Password })).Content.ReadAsStringAsync());

        string passwordHash;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            passwordHash = (await db.Users.SingleAsync(u => u.Email == email)).PasswordHash;
        }

        foreach (var body in bodies)
        {
            Assert.DoesNotContain(Password, body);
            Assert.DoesNotContain("Wrong-Passw0rd!", body);
            Assert.DoesNotContain(passwordHash, body);
            Assert.DoesNotContain("password_hash", body, StringComparison.OrdinalIgnoreCase);
        }

        var logs = string.Join("\n", factory.Logs.Entries);
        Assert.DoesNotContain(Password, logs);
        Assert.DoesNotContain("Wrong-Passw0rd!", logs);
        Assert.DoesNotContain(passwordHash, logs);
        Assert.DoesNotContain(client.Cookies["refresh_token"], logs);
    }

    private Task<string> MintAccessTokenAsync(string email, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == email);
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>();
        var token = new JwtTokenService(options, new FakeTimeProvider(at)).CreateAccessToken(user);
        return Task.FromResult(token.Value);
    }
}
