using System.Text.Json;
using Application.Abstractions;
using Application.Common;
using Application.Rag;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Repositories;

public class RepositoryService(
    IAppDbContext db,
    IRagClient rag,
    IValidator<ImportRepositoryRequest> importValidator,
    TimeProvider clock,
    ILogger<RepositoryService> logger)
{
    /// <summary>An import whose RAG call never returned an index id (e.g. process died) is given up on after this.</summary>
    private static readonly TimeSpan InterruptedImportAfter = TimeSpan.FromMinutes(5);

    public async Task<RepositoryDto> ImportAsync(Guid userId, Guid projectId, ImportRepositoryRequest request, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw new NotFoundException("Project not found.");

        await importValidator.EnsureValidAsync(request, ct);
        GitHubRepoUrl.TryNormalize(request.RepoUrl, out var repoUrl);
        var gitRef = request.Ref ?? GitHubRepoUrl.DefaultRef;

        var duplicate = await db.Repositories.AnyAsync(r =>
            r.ProjectId == projectId && r.RepoUrl == repoUrl && r.Ref == gitRef && r.Status != IndexStatus.Failed, ct);
        if (duplicate)
            throw new ConflictException("This repository (and ref) is already imported in the project.");

        var now = clock.GetUtcNow();
        var repo = new CodeRepository
        {
            Id = Guid.CreateVersion7(), // also the client_ref sent to RAG
            ProjectId = projectId,
            RepoUrl = repoUrl,
            Ref = gitRef,
            Status = IndexStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync(ct);

        // From here on the row exists, so finish the bookkeeping even if the caller disconnects.
        try
        {
            var created = await rag.CreateIndexAsync(repoUrl, gitRef, repo.Id.ToString(), CancellationToken.None);
            repo.IndexId = created.IndexId;
            repo.Status = created.Status;
            repo.UpdatedAt = clock.GetUtcNow();
            project.ActiveIndexId = created.IndexId; // D12: the latest import is the active index
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (RagException ex)
        {
            // RAG may or may not have started work (e.g. on timeout). Keep a visible failed row instead of hiding it.
            repo.Status = IndexStatus.Failed;
            repo.ErrorCode = ex.Code;
            repo.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return RepositoryDto.From(repo, project.ActiveIndexId);
    }

    /// <summary>Returns the latest status; asks RAG only while the index is still being built.</summary>
    public async Task<RepositoryDto> GetAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        var repo = await FindOwnedAsync(userId, repositoryId, ct);
        var stale = await RefreshAsync(repo, ct);
        return RepositoryDto.From(repo, repo.Project.ActiveIndexId, stale);
    }

    /// <summary>
    /// Deletes the index in RAG (idempotent) and then the row. If RAG is unreachable the row is kept and the error
    /// surfaces, so a retry is safe and no index is orphaned.
    /// </summary>
    public async Task DeleteAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        var repo = await FindOwnedAsync(userId, repositoryId, ct);

        if (repo.IndexId is not null)
            await rag.DeleteIndexAsync(repo.IndexId, ct);

        var wasActive = repo.IndexId is not null && repo.IndexId == repo.Project.ActiveIndexId;
        db.Repositories.Remove(repo);
        await db.SaveChangesAsync(ct);

        if (wasActive)
        {
            // Fall back to the newest remaining repository that has an index, if any.
            repo.Project.ActiveIndexId = await db.Repositories
                .Where(r => r.ProjectId == repo.ProjectId && r.IndexId != null && r.Status != IndexStatus.Failed)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.IndexId)
                .FirstOrDefaultAsync(ct);
            await db.SaveChangesAsync(ct);
        }
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<CodeRepository> FindOwnedAsync(Guid userId, Guid repositoryId, CancellationToken ct) =>
        await db.Repositories.Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == repositoryId && r.Project.UserId == userId, ct)
        ?? throw new NotFoundException("Repository not found.");

    /// <summary>
    /// Brings a tracked repository up to date with RAG while it is still being built (no-op once ready/failed).
    /// </summary>
    /// <returns>true when the state could not be refreshed and the cached one is being returned.</returns>
    public async Task<bool> RefreshAsync(CodeRepository repo, CancellationToken ct)
    {
        if (RepositoryDto.IsTerminal(repo.Status)) return false;

        if (repo.IndexId is null)
        {
            if (clock.GetUtcNow() - repo.CreatedAt > InterruptedImportAfter)
            {
                repo.Status = IndexStatus.Failed;
                repo.ErrorCode = "IMPORT_INTERRUPTED";
                repo.UpdatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        try
        {
            var s = await rag.GetIndexAsync(repo.IndexId, ct);
            repo.Status = s.Status;
            repo.Stage = s.Stage;
            repo.FilesDone = s.FilesDone;
            repo.TotalFiles = s.TotalFiles;
            repo.CommitSha = s.CommitSha;
            repo.Stats = s.Files is null && s.Chunks is null
                ? null
                : JsonSerializer.Serialize(new { files = s.Files, chunks = s.Chunks });
            repo.ErrorCode = s.Error?.Code;
            repo.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return false;
        }
        catch (RagException ex) when (ex.Kind == RagFailureKind.NotFound)
        {
            // RAG no longer knows this index (e.g. its storage was reset): it can never become ready.
            repo.Status = IndexStatus.Failed;
            repo.ErrorCode = RagErrorCodes.IndexNotFound;
            repo.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return false;
        }
        catch (RagException ex)
        {
            // A blip in RAG must not look like a failed import to someone polling every couple of seconds.
            logger.LogWarning("Could not refresh repository {RepositoryId}: {Kind} {Code}", repo.Id, ex.Kind, ex.Code);
            return true;
        }
    }
}
