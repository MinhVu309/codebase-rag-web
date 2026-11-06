using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Common;
using Application.Rag;
using Application.Repositories;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Chat;

/// <summary>
/// Conversations and Q&amp;A. Every method takes the caller's <c>userId</c>. Questions and answers are user
/// content: they are stored in the database but must never be written to logs.
/// </summary>
public partial class ChatService(
    IAppDbContext db,
    IRagClient rag,
    RepositoryService repositories,
    IValidator<CreateConversationRequest> createValidator,
    IValidator<AskRequest> askValidator,
    TimeProvider clock,
    ILogger<ChatService> logger)
{
    public async Task<IReadOnlyList<ConversationSummaryDto>> ListConversationsAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        await EnsureProjectOwnedAsync(userId, projectId, ct);

        var rows = await db.Conversations.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.UserId == userId)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.CreatedAt,
                LastMessageAt = c.Messages.Max(m => (DateTimeOffset?)m.CreatedAt),
                MessageCount = c.Messages.Count,
            })
            .ToListAsync(ct);

        // Ordered in memory: a project has few conversations, and this avoids translating a computed sort key.
        return rows
            .Select(r => new ConversationSummaryDto(r.Id, r.Title, r.CreatedAt, r.LastMessageAt ?? r.CreatedAt, r.MessageCount))
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();
    }

    public async Task<ConversationDto> CreateConversationAsync(Guid userId, Guid projectId, CreateConversationRequest request, CancellationToken ct)
    {
        await EnsureProjectOwnedAsync(userId, projectId, ct);
        await createValidator.EnsureValidAsync(request, ct);

        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            UserId = userId,
            Title = request.Title?.Trim() ?? ChatRules.DefaultTitle,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return new ConversationDto(conversation.Id, conversation.ProjectId, conversation.Title, conversation.CreatedAt);
    }

    /// <summary>The latest <paramref name="limit"/> messages, oldest first.</summary>
    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid userId, Guid conversationId, int? limit, CancellationToken ct)
    {
        await FindOwnedConversationAsync(userId, conversationId, ct);
        var take = Math.Clamp(limit ?? ChatRules.DefaultHistoryLimit, 1, ChatRules.MaxHistoryLimit);

        var latest = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        latest.Reverse();
        return latest.Select(MessageDto.From).ToList();
    }

    public async Task<AskResultDto> AskAsync(Guid userId, Guid conversationId, AskRequest request, CancellationToken ct)
    {
        await askValidator.EnsureValidAsync(request, ct);
        var question = request.Question!.Trim();
        var topK = request.TopK ?? ChatRules.DefaultTopK;

        var conversation = await FindOwnedConversationAsync(userId, conversationId, ct);
        var repo = await GetReadyRepositoryAsync(conversation.Project, ct);

        var askedAt = clock.GetUtcNow();
        RagAnswer answer;
        try
        {
            answer = await rag.QueryAsync(repo.IndexId!, question, topK, ct: ct);
        }
        catch (RagException ex) when (IsRecordedFailure(ex.Kind))
        {
            // The turn really happened, so history should show it (question + error marker). The error still reaches the caller.
            AddTurn(conversation, question, askedAt, user => AssistantAfter(user, conversationId, "")
                .WithError(ex.Code));
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (RagException ex) when (ex.Kind == RagFailureKind.NotFound)
        {
            // RAG lost the index under us: it can never answer again.
            repo.Status = IndexStatus.Failed;
            repo.ErrorCode = RagErrorCodes.IndexNotFound;
            repo.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var citations = answer.Citations
            .Select(c => new CitationDto(c.RefId, c.FilePath, c.Symbol, c.SymbolKind, c.StartLine, c.EndLine,
                c.Snippet, c.Score, repo.Id, repo.CommitSha))
            .ToList();

        Message? assistant = null;
        var user = AddTurn(conversation, question, askedAt, u =>
        {
            assistant = AssistantAfter(u, conversationId, answer.Answer);
            assistant.Citations = MessageDto.SerializeCitations(citations); // D14 snapshot, independent of RAG from now on
            assistant.LatencyMs = answer.LatencyMs ?? (int)(assistant.CreatedAt - askedAt).TotalMilliseconds;
            return assistant;
        });
        await db.SaveChangesAsync(CancellationToken.None); // the answer already cost a RAG call: keep it even if the caller left

        logger.LogInformation("Answered conversation {ConversationId} with {CitationCount} citations in {Ms}ms",
            conversationId, citations.Count, assistant!.LatencyMs);
        return new AskResultDto(MessageDto.From(user), MessageDto.From(assistant));
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds the project's active repository and makes sure it is ready, refreshing from RAG if the stored status
    /// is behind (so users need not poll the repository before chatting).
    /// </summary>
    private async Task<CodeRepository> GetReadyRepositoryAsync(Project project, CancellationToken ct)
    {
        var repo = project.ActiveIndexId is null
            ? null
            : await db.Repositories.FirstOrDefaultAsync(r => r.ProjectId == project.Id && r.IndexId == project.ActiveIndexId, ct);

        if (repo is null)
            throw new ConflictException("Project chưa có repository nào được import.", "NO_REPOSITORY");

        if (repo.Status != IndexStatus.Ready)
            await repositories.RefreshAsync(repo, ct);

        return repo.Status switch
        {
            IndexStatus.Ready => repo,
            IndexStatus.Failed => throw new ConflictException(
                "Repository đã index thất bại. Hãy xoá và import lại.", "REPOSITORY_FAILED"),
            _ => throw new RagException(RagFailureKind.NotReady, RagErrorCodes.IndexNotReady),
        };
    }

    /// <summary>Infrastructure failures are part of the conversation; bad input and "not ready" are not.</summary>
    private static bool IsRecordedFailure(RagFailureKind kind) =>
        kind is RagFailureKind.Timeout or RagFailureKind.LlmError or RagFailureKind.Unavailable
            or RagFailureKind.Internal or RagFailureKind.InvalidResponse or RagFailureKind.Unauthorized;

    /// <summary>
    /// History is ordered by creation time, so the answer must be strictly later than the question (ids are not
    /// monotonic within a millisecond and a failed call can return instantly).
    /// </summary>
    private Message AssistantAfter(Message user, Guid conversationId, string content)
    {
        var now = clock.GetUtcNow();
        var at = now > user.CreatedAt ? now : user.CreatedAt.AddMilliseconds(1);
        return Message(conversationId, MessageRole.Assistant, content, at);
    }

    private Message AddTurn(Conversation conversation, string question, DateTimeOffset askedAt, Func<Message, Message> createAssistant)
    {
        var user = Message(conversation.Id, MessageRole.User, question, askedAt);
        db.Messages.Add(user);
        db.Messages.Add(createAssistant(user));

        if (conversation.Title == ChatRules.DefaultTitle)
            conversation.Title = TitleFrom(question);
        return user;
    }

    private static Message Message(Guid conversationId, MessageRole role, string content, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        ConversationId = conversationId,
        Role = role,
        Content = content,
        CreatedAt = at,
    };

    private static string TitleFrom(string question)
    {
        var oneLine = Whitespace().Replace(question, " ").Trim();
        return oneLine.Length <= ChatRules.MaxAutoTitleLength ? oneLine : oneLine[..(ChatRules.MaxAutoTitleLength - 1)] + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private async Task EnsureProjectOwnedAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId, ct))
            throw new NotFoundException("Project not found.");
    }

    private async Task<Conversation> FindOwnedConversationAsync(Guid userId, Guid conversationId, CancellationToken ct) =>
        await db.Conversations.Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.Project.UserId == userId, ct)
        ?? throw new NotFoundException("Conversation not found.");
}

internal static class MessageExtensions
{
    public static Message WithError(this Message message, string errorCode)
    {
        message.ErrorCode = errorCode;
        return message;
    }
}
