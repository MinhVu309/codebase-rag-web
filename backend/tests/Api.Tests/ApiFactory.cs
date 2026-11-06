using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Api.Tests;

/// <summary>Boots the real API against a throwaway Postgres container (real migrations are applied on startup).</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecret = "test-secret-test-secret-test-secret-123456";
    public const string JwtIssuer = "test-issuer";
    public const string JwtAudience = "test-audience";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public CapturingLoggerProvider Logs { get; } = new();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = _postgres.GetConnectionString(),
            ["JWT_SECRET"] = JwtSecret,
            ["JWT_ISSUER"] = JwtIssuer,
            ["JWT_AUDIENCE"] = JwtAudience,
            ["JWT_ACCESS_TOKEN_MINUTES"] = "15",
            ["JWT_REFRESH_TOKEN_DAYS"] = "7",
        }));

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });
    }

    public AuthTestClient CreateAuthClient() => new(this);
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
