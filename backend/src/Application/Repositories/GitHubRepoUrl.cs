using System.Text.RegularExpressions;

namespace Application.Repositories;

/// <summary>
/// D21: only public GitHub repositories of the exact form <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c>.
/// These values are later handed to a service that runs <c>git clone</c>, so the check is an allowlist,
/// not a blocklist: no other host, scheme, credentials, port, query, fragment or sub-path gets through.
/// </summary>
public static partial class GitHubRepoUrl
{
    // Owner: 1-39 chars, alphanumeric or hyphen, no leading/trailing hyphen. Repo: 1-100 of [A-Za-z0-9._-].
    [GeneratedRegex(@"^https://github\.com/(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)/(?<repo>[A-Za-z0-9._-]{1,100})$")]
    private static partial Regex UrlPattern();

    // Git ref (branch, tag or SHA). Must start alphanumeric so it can never be read as a git option ("-...").
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,254}$")]
    private static partial Regex RefPattern();

    public const string DefaultRef = "main";

    /// <summary>Accepts one trailing slash and a trailing ".git" and returns the canonical form.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        if (s.EndsWith('/')) s = s[..^1];
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];

        var m = UrlPattern().Match(s);
        if (!m.Success) return false;

        var repo = m.Groups["repo"].Value;
        if (repo is "." or "..") return false;

        normalized = $"https://github.com/{m.Groups["owner"].Value}/{repo}";
        return true;
    }

    public static bool IsValidRef(string? gitRef) =>
        gitRef is not null
        && RefPattern().IsMatch(gitRef)
        && !gitRef.Contains("..")
        && !gitRef.Contains("//")
        && !gitRef.EndsWith('/')
        && !gitRef.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
}
