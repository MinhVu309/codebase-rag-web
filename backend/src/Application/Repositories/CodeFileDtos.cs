namespace Application.Repositories;

public static class FileRules
{
    public const int MaxPathLength = 1024;
    /// <summary>Most lines RAG returns per request (contract) and so the most the Web API asks for.</summary>
    public const int MaxLines = 2000;
}

/// <summary>Query of <c>GET /api/repositories/{id}/files</c>. <c>Start</c> defaults to 1; <c>End</c> to <c>Start + 1999</c>.</summary>
public record ReadFileRequest(string? Path, int? Start, int? End);

/// <summary>
/// A slice of a file from the indexed snapshot. <c>Content</c> is verbatim (may contain <c>&lt;script&gt;</c> or
/// markdown): the frontend must escape it. <c>Language</c> is null when RAG could not tell. <c>CommitSha</c> is the
/// commit the repository was indexed at (null if RAG did not report one).
/// </summary>
public record CodeFileDto(string Path, int Start, int End, string Content, string? Language, string? CommitSha);
