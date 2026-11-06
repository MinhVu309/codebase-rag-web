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

    public int? LatencyMs { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
