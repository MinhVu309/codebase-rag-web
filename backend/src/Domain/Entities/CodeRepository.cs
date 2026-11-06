using Domain.Enums;

namespace Domain.Entities;

/// <summary>A GitHub repository imported into a project. <see cref="Id"/> is sent to RAG as <c>client_ref</c>.</summary>
public class CodeRepository
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string RepoUrl { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;

    /// <summary>RAG <c>index_id</c>; null until <c>POST /v1/indexes</c> succeeds.</summary>
    public string? IndexId { get; set; }

    public IndexStatus Status { get; set; } = IndexStatus.Pending;
    public string? Stage { get; set; }
    public int FilesDone { get; set; }
    public int TotalFiles { get; set; }
    public string? CommitSha { get; set; }

    /// <summary>Raw JSON (<c>{files, chunks}</c>) stored as jsonb.</summary>
    public string? Stats { get; set; }

    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
}
