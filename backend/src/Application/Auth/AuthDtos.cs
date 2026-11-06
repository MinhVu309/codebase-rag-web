namespace Application.Auth;

public record RegisterRequest(string? Email, string? Password);

public record LoginRequest(string? Email, string? Password);

public record UserDto(Guid Id, string Email, DateTimeOffset CreatedAt);

public record AuthResult(
    UserDto User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
