using Application.Abstractions;
using Application.Common;
using Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth;

public class AuthService(
    IAppDbContext db,
    IPasswordService passwords,
    ITokenService tokens,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    TimeProvider clock)
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.EnsureValidAsync(request, ct);

        var email = NormalizeEmail(request.Email!);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            PasswordHash = passwords.Hash(request.Password!),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Users.Add(user);

        try
        {
            return await IssueTokensAsync(user, ct);
        }
        catch (DbUpdateException)
        {
            // Possibly lost a race with a concurrent registration of the same email (unique index).
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != user.Id, ct))
                throw new ConflictException("An account with this email already exists.");
            throw;
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.EnsureValidAsync(request, ct);

        var email = NormalizeEmail(request.Email!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Same work as a real check so response time does not reveal which emails exist.
            passwords.BurnVerifyTime(request.Password!);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!passwords.Verify(user.PasswordHash, request.Password!))
            throw new UnauthorizedException(InvalidCredentials);

        return await IssueTokensAsync(user, ct);
    }

    /// <summary>Rotates the refresh token. Presenting an already-revoked token revokes the user's whole token family.</summary>
    public async Task<AuthResult> RefreshAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw new UnauthorizedException("Refresh token is missing.");

        var now = clock.GetUtcNow();
        var hash = tokens.HashRefreshToken(rawRefreshToken);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            throw new UnauthorizedException("Refresh token is invalid.");

        if (stored.RevokedAt is not null)
        {
            // Reuse of a rotated/revoked token: assume theft and kill every active session.
            await RevokeAllForUserAsync(stored.UserId, now, ct);
            throw new UnauthorizedException("Refresh token is invalid.");
        }

        if (stored.ExpiresAt <= now)
            throw new UnauthorizedException("Refresh token has expired.");

        // Atomic compare-and-set so two concurrent refreshes cannot both succeed.
        var revoked = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        if (revoked == 0)
        {
            await RevokeAllForUserAsync(stored.UserId, now, ct);
            throw new UnauthorizedException("Refresh token is invalid.");
        }

        return await IssueTokensAsync(stored.User, ct);
    }

    /// <summary>Idempotent: unknown, expired or already-revoked tokens are ignored.</summary>
    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;

        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var now = clock.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new UnauthorizedException("User no longer exists.");
        return ToDto(user);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = refresh.Hash,
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = refresh.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);

        return new AuthResult(ToDto(user), access.Value, access.ExpiresAt, refresh.Raw, refresh.ExpiresAt);
    }

    private Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.CreatedAt);
}
