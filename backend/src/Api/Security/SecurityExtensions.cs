using Application.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Api.Security;

public static class SecurityExtensions
{
    /// <summary>Settings, rate limiting, CORS and forwarded-header options. See README, section "Bảo mật".</summary>
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services.AddOptions<SecurityOptions>()
            .Configure<IConfiguration>(SecurityOptions.Bind)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

        services.AddApiRateLimiting();

        // Application only needs the one number.
        services.AddSingleton(sp => new ImportLimits(sp.GetRequiredService<IOptions<SecurityOptions>>().Value.MaxConcurrentImports));

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<SecurityOptions>>((cors, security) =>
        {
            var origins = security.Value.CorsOrigins;
            if (origins.Count == 0) return;
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins([.. origins])                       // never "*": credentials are on
                .AllowCredentials()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Content-Type", "X-Request-ID")
                .WithExposedHeaders("Retry-After", "X-Request-ID")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<SecurityOptions>>((forwarded, security) =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Default trust is loopback only; replace it with exactly what the operator listed.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var network in security.Value.TrustedProxies) forwarded.KnownIPNetworks.Add(network);
        });

        return services;
    }

    /// <summary>X-Forwarded-* are honoured only from TRUSTED_PROXIES; anyone else could fake their IP past the rate limiter.</summary>
    public static WebApplication UseApiForwardedHeaders(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.TrustedProxies.Count > 0)
            app.UseForwardedHeaders();
        return app;
    }

    public static WebApplication UseApiCors(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.CorsOrigins.Count > 0)
            app.UseCors();
        return app;
    }
}
