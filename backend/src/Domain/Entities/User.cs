namespace Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
}
