using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

/// <summary>
/// Rejects bad RAG_QUERY_MODE / RAG_QUERY_RERANK / RAG_QUERY_API at startup. It reads the raw settings, because by the time
/// <see cref="RagOptions"/> is bound an unparsable value has already fallen back to the default and a typo
/// such as "hybird" would silently change retrieval behaviour.
/// </summary>
public sealed class RagQueryOptionsValidator(IConfiguration configuration) : IValidateOptions<RagOptions>
{
    public static readonly string[] Modes = ["dense", "hybrid"];
    public static readonly string[] Apis = ["single", "multi"];

    public ValidateOptionsResult Validate(string? name, RagOptions options)
    {
        var errors = new List<string>();

        var mode = configuration["RAG_QUERY_MODE"];
        if (!string.IsNullOrWhiteSpace(mode) && !Modes.Contains(mode.Trim(), StringComparer.OrdinalIgnoreCase))
            errors.Add("RAG_QUERY_MODE must be 'dense' or 'hybrid'.");

        var rerank = configuration["RAG_QUERY_RERANK"];
        if (!string.IsNullOrWhiteSpace(rerank) && !bool.TryParse(rerank, out _))
            errors.Add("RAG_QUERY_RERANK must be 'true' or 'false'.");

        var api = configuration["RAG_QUERY_API"];
        if (!string.IsNullOrWhiteSpace(api) && !Apis.Contains(api.Trim(), StringComparer.OrdinalIgnoreCase))
            errors.Add("RAG_QUERY_API must be 'single' or 'multi'.");

        if (errors.Count == 0 && options.QueryRerank && options.QueryMode != "hybrid")
            errors.Add("RAG_QUERY_RERANK=true requires RAG_QUERY_MODE=hybrid.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// Rejects a bad CONVERSATION_SCOPE_MAX at startup (raw value, so a typo is not silently replaced by the default).
/// Above 5 is rejected because the pinned contract caps <c>scope</c> at 5 items: a larger Web limit would only turn into
/// a RAG 422 on the first big question. Raise it here when the contract does.
/// </summary>
public sealed class ConversationScopeOptionsValidator(IConfiguration configuration) : IValidateOptions<ConversationScopeOptions>
{
    public ValidateOptionsResult Validate(string? name, ConversationScopeOptions options)
    {
        var raw = configuration["CONVERSATION_SCOPE_MAX"];
        if (string.IsNullOrWhiteSpace(raw)) return ValidateOptionsResult.Success;

        return int.TryParse(raw.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var max)
               && max is >= 1 and <= ConversationScopeOptions.ContractMax
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"CONVERSATION_SCOPE_MAX must be a whole number from 1 to {ConversationScopeOptions.ContractMax}.");
    }
}
