using Application.Common;

namespace Application.Rag;

public enum RagFailureKind
{
    /// <summary>RAG rejected our API key (401). A configuration problem, never the end user's fault.</summary>
    Unauthorized,
    NotFound,
    /// <summary>409 INDEX_NOT_READY.</summary>
    NotReady,
    /// <summary>400/422: bad URL, bad range, repo too large, ...</summary>
    Invalid,
    /// <summary>502 LLM_ERROR.</summary>
    LlmError,
    /// <summary>504 TIMEOUT from RAG, or our own client-side timeout.</summary>
    Timeout,
    /// <summary>Connection refused, DNS failure, 5xx from a proxy, ...</summary>
    Unavailable,
    /// <summary>500 INTERNAL_ERROR or anything unexpected.</summary>
    Internal,
    /// <summary>RAG answered 2xx but the body did not match the contract.</summary>
    InvalidResponse,
}

public static class RagErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string RepoNotFound = "REPO_NOT_FOUND";
    public const string RepoTooLarge = "REPO_TOO_LARGE";
    public const string InvalidRepo = "INVALID_REPO";
    public const string IndexNotReady = "INDEX_NOT_READY";
    public const string IndexNotFound = "INDEX_NOT_FOUND";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string InvalidRange = "INVALID_RANGE";
    public const string LlmError = "LLM_ERROR";
    public const string Timeout = "TIMEOUT";
    public const string InternalError = "INTERNAL_ERROR";

    // Raised by this client, not by RAG.
    public const string Unavailable = "RAG_UNAVAILABLE";
    public const string InvalidResponse = "RAG_INVALID_RESPONSE";
}

/// <summary>A failed call to the RAG service, already classified. <c>UpstreamMessage</c> is for logs only.</summary>
public sealed class RagException(RagFailureKind kind, string code, int? upstreamStatus = null, string? upstreamMessage = null)
    : AppException($"RAG call failed: {code}")
{
    public RagFailureKind Kind { get; } = kind;
    public string Code { get; } = code;
    public int? UpstreamStatus { get; } = upstreamStatus;
    public string? UpstreamMessage { get; } = upstreamMessage;
}
