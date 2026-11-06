using System.Text.Json;
using Domain.Entities;
using Domain.Enums;

namespace Application.Repositories;

/// <summary>Omitting <c>Ref</c> uses <see cref="GitHubRepoUrl.DefaultRef"/>.</summary>
public record ImportRepositoryRequest(string? RepoUrl, string? Ref);

/// <summary>
/// What the frontend sees. The RAG <c>index_id</c> is intentionally absent: the frontend never talks to RAG (D10).
/// <c>Stale</c> is true when RAG could not be reached just now and this is the last known state.
/// </summary>
public record RepositoryDto(
    Guid Id,
    Guid ProjectId,
    string RepoUrl,
    string Ref,
    string Status,
    string? Stage,
    int FilesDone,
    int TotalFiles,
    string? CommitSha,
    int? Files,
    int? Chunks,
    string? ErrorCode,
    bool IsActive,
    bool Stale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static RepositoryDto From(CodeRepository r, string? activeIndexId, bool stale = false)
    {
        int? files = null, chunks = null;
        if (r.Stats is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(r.Stats);
                if (doc.RootElement.TryGetProperty("files", out var f) && f.TryGetInt32(out var fv)) files = fv;
                if (doc.RootElement.TryGetProperty("chunks", out var c) && c.TryGetInt32(out var cv)) chunks = cv;
            }
            catch (JsonException)
            {
                // Stats is informational; a malformed value must not break the endpoint.
            }
        }

        return new RepositoryDto(
            r.Id, r.ProjectId, r.RepoUrl, r.Ref,
            r.Status.ToString().ToLowerInvariant(), r.Stage, r.FilesDone, r.TotalFiles, r.CommitSha,
            files, chunks, r.ErrorCode,
            IsActive: r.IndexId is not null && r.IndexId == activeIndexId,
            stale, r.CreatedAt, r.UpdatedAt);
    }

    public static bool IsTerminal(IndexStatus status) => status is IndexStatus.Ready or IndexStatus.Failed;
}
