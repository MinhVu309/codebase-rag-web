using Application.Abstractions;
using Application.Common;
using Application.Repositories;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Chat;

/// <summary>
/// Conversation Scope (D40, S1): the user-managed set of repositories a conversation may search.
/// Every method takes the caller's <c>userId</c>. It never touches RAG (readiness is the status stored by the
/// repository polling) and never exposes or logs an <c>index_id</c>, title or repository URL.
///
/// <para>Order of checks, so ids cannot be probed: the conversation (404, the same answer for "not yours" and "does not
/// exist") -> the request body (400) -> the repository (404) -> state (409).</para>
///
/// <para>Changes that depend on the current size of the Scope (add against the cap, remove against "last member") run
/// in a transaction that first locks the conversation's row (<c>SELECT ... FOR UPDATE</c>), so two requests for the
/// same conversation cannot both pass the check and overshoot the cap or empty the Scope. Requests for different
/// conversations do not wait for each other.</para>
/// </summary>
public class ConversationScopeService(
    IAppDbContext db,
    ConversationScopeSettings settings,
    IValidator<AddScopeRepositoryRequest> addValidator,
    TimeProvider clock)
{
    /// <summary>The Scope, oldest member first.</summary>
    public async Task<IReadOnlyList<ScopeRepositoryDto>> ListAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        await EnsureOwnedConversationAsync(userId, conversationId, ct);
        return await ReadScopeAsync(conversationId, ct);
    }

    /// <summary>
    /// Adds a ready repository of the conversation's project. Already a member: 200 with the unchanged Scope. At the
    /// cap (<see cref="ConversationScopeSettings.Max"/>): 409 SCOPE_LIMIT_REACHED and nothing changes.
    /// </summary>
    public async Task<IReadOnlyList<ScopeRepositoryDto>> AddAsync(Guid userId, Guid conversationId, AddScopeRepositoryRequest? request, CancellationToken ct)
    {
        var projectId = await EnsureOwnedConversationAsync(userId, conversationId, ct);

        request ??= new AddScopeRepositoryRequest(null);
        await addValidator.EnsureValidAsync(request, ct);
        var repositoryId = request.RepositoryId!.Value;

        // Same project as the conversation (which the user owns), or it is simply "not found".
        var repo = await db.Repositories.AsNoTracking()
                       .Where(r => r.Id == repositoryId && r.ProjectId == projectId)
                       .Select(r => new { r.Id, r.Status })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Repository not found.");

        // Idempotent: already a member is a plain 200, whatever its status is now.
        if (await db.ConversationRepositories.AnyAsync(x => x.ConversationId == conversationId && x.RepositoryId == repositoryId, ct))
            return await ReadScopeAsync(conversationId, ct);

        if (repo.Status != IndexStatus.Ready)
            throw new ConflictException("Repository chưa sẵn sàng. Chờ trạng thái 'ready' rồi thêm vào hội thoại.",
                ScopeErrorCodes.RepositoryNotReady);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockConversationAsync(conversationId, ct);

            // Re-check under the lock: a concurrent request may have added it (or filled the Scope) a moment ago.
            var members = await db.ConversationRepositories.CountAsync(x => x.ConversationId == conversationId, ct);
            var already = await db.ConversationRepositories.AnyAsync(x => x.ConversationId == conversationId && x.RepositoryId == repositoryId, ct);
            if (!already)
            {
                if (members >= settings.Max)
                    throw new ConflictException(
                        $"Hội thoại đã có đủ {settings.Max} repository. Hãy bỏ bớt một repository rồi thêm lại.",
                        ScopeErrorCodes.ScopeLimitReached);

                db.ConversationRepositories.Add(new ConversationRepository
                {
                    ConversationId = conversationId,
                    RepositoryId = repositoryId,
                    AddedAt = clock.GetUtcNow(),
                    AddedByUserId = userId,
                });
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Backstop behind the lock: the primary key rejected a duplicate (treated as "already a member"), or the
            // repository / conversation was deleted in between (404). Anything else is a real failure.
            db.ChangeTracker.Clear();
            if (!await db.Repositories.AnyAsync(r => r.Id == repositoryId, ct)
                || !await db.Conversations.AnyAsync(c => c.Id == conversationId, ct))
                throw new NotFoundException("Repository not found.");
            if (!await db.ConversationRepositories.AnyAsync(x => x.ConversationId == conversationId && x.RepositoryId == repositoryId, ct))
                throw;
        }

        return await ReadScopeAsync(conversationId, ct);
    }

    /// <summary>Removes a member. Not a member: 404. The last member: 409 SCOPE_CANNOT_BE_EMPTY. Past messages are untouched.</summary>
    public async Task RemoveAsync(Guid userId, Guid conversationId, Guid repositoryId, CancellationToken ct)
    {
        await EnsureOwnedConversationAsync(userId, conversationId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockConversationAsync(conversationId, ct);

        var member = await db.ConversationRepositories
                         .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.RepositoryId == repositoryId, ct)
                     ?? throw new NotFoundException("Repository is not in this conversation.");

        var members = await db.ConversationRepositories.CountAsync(x => x.ConversationId == conversationId, ct);
        if (members <= 1)
            throw new ConflictException("Hội thoại phải có ít nhất một repository.", ScopeErrorCodes.ScopeCannotBeEmpty);

        db.ConversationRepositories.Remove(member);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------

    /// <returns>The conversation's project id.</returns>
    private async Task<Guid> EnsureOwnedConversationAsync(Guid userId, Guid conversationId, CancellationToken ct) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId && c.Project.UserId == userId)
            .Select(c => (Guid?)c.ProjectId)
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Conversation not found.");

    /// <summary>Row lock held until the surrounding transaction ends; serializes Scope changes of one conversation.</summary>
    private async Task LockConversationAsync(Guid conversationId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM conversations WHERE id = {conversationId} FOR UPDATE", ct);

    private async Task<IReadOnlyList<ScopeRepositoryDto>> ReadScopeAsync(Guid conversationId, CancellationToken ct)
    {
        var rows = await db.ConversationRepositories.AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.AddedAt).ThenBy(x => x.RepositoryId)
            .Select(x => new { x.RepositoryId, x.Repository.RepoUrl, x.Repository.Status, x.Repository.CommitSha, x.AddedAt })
            .ToListAsync(ct);

        return rows
            .Select(r => new ScopeRepositoryDto(r.RepositoryId, GitHubRepoUrl.DisplayName(r.RepoUrl), r.RepoUrl,
                ScopeStatus.Format(r.Status), r.CommitSha, r.AddedAt))
            .ToList();
    }
}
