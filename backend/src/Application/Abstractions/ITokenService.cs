using Domain.Entities;

namespace Application.Abstractions;

public record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <param name="Raw">Sent to the client only; never persisted.</param>
/// <param name="Hash">What gets stored in the database.</param>
public record NewRefreshToken(string Raw, string Hash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    NewRefreshToken CreateRefreshToken();
    string HashRefreshToken(string rawToken);
}
