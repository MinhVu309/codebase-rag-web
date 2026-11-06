namespace Domain.Entities;

public class Project
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>RAG <c>index_id</c> of the active index (D12). Null until an index exists.</summary>
    public string? ActiveIndexId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public List<CodeRepository> Repositories { get; set; } = [];
    public List<Conversation> Conversations { get; set; } = [];
}
