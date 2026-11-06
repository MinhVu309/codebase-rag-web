using Application.Abstractions;
using Application.Rag;
using Infrastructure.Rag;
using Infrastructure.Auth;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure;

public static class DependencyInjection
{
    public const string AccessTokenCookie = "access_token";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // Settings are read lazily from IConfiguration (env vars: DATABASE_URL, JWT_*).
        services.AddDbContext<AppDbContext>((sp, o) =>
            o.UseNpgsql(ConnectionStringResolver.Resolve(sp.GetRequiredService<IConfiguration>()["DATABASE_URL"] ?? "")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>()
            .Configure<IConfiguration>((o, c) =>
            {
                o.Secret = c["JWT_SECRET"] ?? "";
                o.Issuer = c["JWT_ISSUER"] ?? "";
                o.Audience = c["JWT_AUDIENCE"] ?? "";
                if (int.TryParse(c["JWT_ACCESS_TOKEN_MINUTES"], out var minutes)) o.AccessTokenMinutes = minutes;
                if (int.TryParse(c["JWT_REFRESH_TOKEN_DAYS"], out var days)) o.RefreshTokenDays = days;
            })
            .Validate(o => o.Secret.Length >= JwtOptions.MinSecretLength,
                $"JWT_SECRET must be at least {JwtOptions.MinSecretLength} characters.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "JWT_ISSUER and JWT_AUDIENCE are required.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0,
                "JWT_ACCESS_TOKEN_MINUTES and JWT_REFRESH_TOKEN_DAYS must be positive.")
            .ValidateOnStart();

        AddRag(services);

        services.AddSingleton<IPasswordService, IdentityPasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var o = jwt.Value;
                bearer.MapInboundClaims = false; // keep "sub" as-is
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateKey(o.Secret),
                    ValidateLifetime = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.Zero,
                };
                // Access token travels in an HttpOnly cookie, not an Authorization header.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        ctx.Token = ctx.Request.Cookies[AccessTokenCookie];
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    private static void AddRag(IServiceCollection services)
    {
        services.AddOptions<RagOptions>()
            .Configure<IConfiguration>((o, c) =>
            {
                if (Enum.TryParse<RagMode>(c["RAG_MODE"], ignoreCase: true, out var mode)) o.Mode = mode;
                o.BaseUrl = c["RAG_BASE_URL"] ?? "";
                o.ApiKey = c["RAG_API_KEY"] ?? "";
                o.QueryTimeout = Seconds(c["RAG_QUERY_TIMEOUT_SECONDS"], o.QueryTimeout);
                o.StatusTimeout = Seconds(c["RAG_STATUS_TIMEOUT_SECONDS"], o.StatusTimeout);
                o.RequestTimeout = Seconds(c["RAG_REQUEST_TIMEOUT_SECONDS"], o.RequestTimeout);
                if (int.TryParse(c["RAG_STATUS_RETRIES"], out var retries) && retries >= 0) o.StatusRetries = retries;
            })
            .Validate(o => o.Mode == RagMode.Fake
                           || (Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"),
                "RAG_BASE_URL must be an absolute http(s) URL (or set RAG_MODE=fake).")
            .Validate(o => o.Mode == RagMode.Fake || !string.IsNullOrWhiteSpace(o.ApiKey),
                "RAG_API_KEY is required (or set RAG_MODE=fake).")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddHttpClient<HttpRagClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<RagOptions>>().Value;
            if (o.Mode == RagMode.Http)
                client.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            // Per-call timeouts are enforced in HttpRagClient (they differ per endpoint).
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<FakeRagClient>();
        services.AddScoped<IRagClient>(sp =>
            sp.GetRequiredService<IOptions<RagOptions>>().Value.Mode == RagMode.Fake
                ? sp.GetRequiredService<FakeRagClient>()
                : sp.GetRequiredService<HttpRagClient>());
    }

    private static TimeSpan Seconds(string? value, TimeSpan fallback) =>
        double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : fallback;
}
