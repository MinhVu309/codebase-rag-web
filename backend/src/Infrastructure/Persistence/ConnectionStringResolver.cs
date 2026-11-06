using Npgsql;

namespace Infrastructure.Persistence;

public static class ConnectionStringResolver
{
    /// <summary>
    /// Accepts either an Npgsql key/value connection string or a <c>postgres://user:pass@host:port/db</c> URL
    /// (the format Supabase and most hosts hand out) and returns an Npgsql connection string.
    /// </summary>
    public static string Resolve(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is not configured.");

        var isUrl = databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                    || databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUrl) return databaseUrl;

        var uri = new Uri(databaseUrl);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) builder.Password = Uri.UnescapeDataString(parts[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2) builder[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
        }

        return builder.ConnectionString;
    }
}
