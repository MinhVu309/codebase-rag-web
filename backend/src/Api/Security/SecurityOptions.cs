using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;

namespace Api.Security;

/// <summary>
/// Security settings read from environment variables. Every numeric limit accepts 0 to switch that limit off.
/// Bad values are collected in <see cref="Errors"/> and stop the API at startup (see <see cref="SecurityOptionsValidator"/>).
/// </summary>
public class SecurityOptions
{
    // Defaults: see docs/reports/security-review-2026-11.md for the reasoning.
    public int AuthPerMinute { get; set; } = 10;
    public int AskPerMinute { get; set; } = 10;
    public int ImportsPerWindow { get; set; } = 5;
    public int ImportWindowMinutes { get; set; } = 10;
    public int MaxConcurrentImports { get; set; } = 1;
    public int MaxRequestBodyBytes { get; set; } = 64 * 1024;

    /// <summary>Allowed browser origins (scheme://host[:port]). Empty = CORS is off.</summary>
    public List<string> CorsOrigins { get; set; } = [];

    /// <summary>Reverse proxies (IP or CIDR) whose X-Forwarded-For/Proto are trusted. Empty = ignore those headers.</summary>
    public List<IPNetwork> TrustedProxies { get; set; } = [];

    public List<string> Errors { get; } = [];

    public static void Bind(SecurityOptions o, IConfiguration c)
    {
        o.AuthPerMinute = Int(o, c, "RATE_LIMIT_AUTH_PER_MINUTE", o.AuthPerMinute);
        o.AskPerMinute = Int(o, c, "RATE_LIMIT_ASK_PER_MINUTE", o.AskPerMinute);
        o.ImportsPerWindow = Int(o, c, "RATE_LIMIT_IMPORT_PER_WINDOW", o.ImportsPerWindow);
        o.ImportWindowMinutes = Int(o, c, "RATE_LIMIT_IMPORT_WINDOW_MINUTES", o.ImportWindowMinutes);
        o.MaxConcurrentImports = Int(o, c, "MAX_CONCURRENT_IMPORTS_PER_USER", o.MaxConcurrentImports);
        o.MaxRequestBodyBytes = Int(o, c, "MAX_REQUEST_BODY_BYTES", o.MaxRequestBodyBytes);
        if (o.ImportsPerWindow > 0 && o.ImportWindowMinutes < 1)
            o.Errors.Add("RATE_LIMIT_IMPORT_WINDOW_MINUTES must be at least 1.");

        foreach (var raw in Split(c["CORS_ORIGINS"]))
        {
            if (TryNormalizeOrigin(raw, out var origin)) o.CorsOrigins.Add(origin);
            else o.Errors.Add("CORS_ORIGINS must be a comma-separated list of origins like http://localhost:5173 (no '*', no path).");
        }

        foreach (var raw in Split(c["TRUSTED_PROXIES"]))
        {
            if (TryParseNetwork(raw, out var network)) o.TrustedProxies.Add(network);
            else o.Errors.Add("TRUSTED_PROXIES must be a comma-separated list of IP addresses or CIDR ranges.");
        }
    }

    private static int Int(SecurityOptions o, IConfiguration c, string key, int fallback)
    {
        var raw = c[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return value;
        o.Errors.Add($"{key} must be a non-negative integer (0 turns the limit off).");
        return fallback;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Origins are compared byte for byte by browsers, so keep them in canonical form and refuse wildcards.</summary>
    public static bool TryNormalizeOrigin(string raw, out string origin)
    {
        origin = "";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool TryParseNetwork(string raw, out IPNetwork network)
    {
        if (IPNetwork.TryParse(raw, out network)) return true;
        if (IPAddress.TryParse(raw, out var ip))
        {
            network = new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }
        return false;
    }
}

public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityOptions options) =>
        options.Errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(options.Errors);
}
