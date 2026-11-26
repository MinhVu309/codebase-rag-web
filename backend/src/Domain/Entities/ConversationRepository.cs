namespace Domain.Entities;

/// <summary>
/// One repository in a conversation's Scope (D40, S1): the set the conversation is allowed to search. The pair
/// (<see cref="ConversationId"/>, <see cref="RepositoryId"/>) is the primary key, so a repository is in a Scope at most
/// once. The row goes away with either side (database cascade); history (messages, citation and retrieval snapshots)
/// never references it by foreign key, so removing a member does not rewrite the past (H2).
/// </summary>
public class ConversationRepository
{
    public Guid ConversationId { get; set; }
    public Guid RepositoryId { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>The user who added it; null for rows created by the migration backfill of old conversations.</summary>
    public Guid? AddedByUserId { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public CodeRepository Repository { get; set; } = null!;
}
