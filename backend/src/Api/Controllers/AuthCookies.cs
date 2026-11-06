using Application.Auth;
using Infrastructure;

namespace Api.Controllers;

/// <summary>Sets/clears the HttpOnly, SameSite=Strict auth cookies (D31).</summary>
public class AuthCookies(IConfiguration configuration)
{
    public const string RefreshTokenCookie = "refresh_token";
    // Browsers only send the refresh cookie to the auth endpoints.
    private const string RefreshPath = "/api/auth";

    // Secure by default; set AUTH_COOKIE_SECURE=false only for plain-http local development.
    private bool Secure => !string.Equals(configuration["AUTH_COOKIE_SECURE"], "false", StringComparison.OrdinalIgnoreCase);

    public void Append(HttpResponse response, AuthResult result)
    {
        response.Cookies.Append(DependencyInjection.AccessTokenCookie, result.AccessToken,
            Options("/", result.AccessTokenExpiresAt));
        response.Cookies.Append(RefreshTokenCookie, result.RefreshToken,
            Options(RefreshPath, result.RefreshTokenExpiresAt));
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(DependencyInjection.AccessTokenCookie, Options("/", null));
        response.Cookies.Delete(RefreshTokenCookie, Options(RefreshPath, null));
    }

    private CookieOptions Options(string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Secure,
        SameSite = SameSiteMode.Strict,
        Path = path,
        Expires = expires,
    };
}
