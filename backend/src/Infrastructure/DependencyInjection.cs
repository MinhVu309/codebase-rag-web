using Application.Abstractions;
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
}
