using Domain.Enums;

namespace Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Citation snapshot (D14) as raw JSON, stored as jsonb.</summary>
    public string? Citations { get; set; }

    /// <summary>
    /// Versioned execution snapshot (D40, H2) as raw JSON, stored as jsonb: Scope at send time, per-repository
    /// outcomes, fallback, statuses. For assistant messages; written by the multi-repository question flow, not yet.
    /// </summary>
    public string? RetrievalSnapshot { get; set; }

    /// <summary>Focus the user chose for this question (JSON array of repository ids, jsonb). For user messages; not written yet.</summary>
    public string? FocusRepositoryIds { get; set; }

    public int? LatencyMs { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
