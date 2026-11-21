using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

/// <summary>
/// Rejects bad RAG_QUERY_MODE / RAG_QUERY_RERANK at startup. It reads the raw settings, because by the time
/// <see cref="RagOptions"/> is bound an unparsable value has already fallen back to the default and a typo
/// such as "hybird" would silently change retrieval behaviour.
/// </summary>
public sealed class RagQueryOptionsValidator(IConfiguration configuration) : IValidateOptions<RagOptions>
{
    public static readonly string[] Modes = ["dense", "hybrid"];

    public ValidateOptionsResult Validate(string? name, RagOptions options)
    {
        var errors = new List<string>();

        var mode = configuration["RAG_QUERY_MODE"];
        if (!string.IsNullOrWhiteSpace(mode) && !Modes.Contains(mode.Trim(), StringComparer.OrdinalIgnoreCase))
            errors.Add("RAG_QUERY_MODE must be 'dense' or 'hybrid'.");

        var rerank = configuration["RAG_QUERY_RERANK"];
        if (!string.IsNullOrWhiteSpace(rerank) && !bool.TryParse(rerank, out _))
            errors.Add("RAG_QUERY_RERANK must be 'true' or 'false'.");

        if (errors.Count == 0 && options.QueryRerank && options.QueryMode != "hybrid")
            errors.Add("RAG_QUERY_RERANK=true requires RAG_QUERY_MODE=hybrid.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
