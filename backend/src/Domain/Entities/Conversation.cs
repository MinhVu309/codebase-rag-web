namespace Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;
    public List<Message> Messages { get; set; } = [];
}
