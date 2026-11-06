using Application.Abstractions;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Auth;

/// <summary>Password hashing via ASP.NET Core Identity's PBKDF2-based <see cref="PasswordHasher{TUser}"/>.</summary>
public class IdentityPasswordService : IPasswordService
{
    private static readonly User Sentinel = new();
    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _dummyHash;

    public IdentityPasswordService() => _dummyHash = _hasher.HashPassword(Sentinel, Guid.NewGuid().ToString("N"));

    public string Hash(string password) => _hasher.HashPassword(Sentinel, password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(Sentinel, hash, password) is not PasswordVerificationResult.Failed;

    public void BurnVerifyTime(string password) => _hasher.VerifyHashedPassword(Sentinel, _dummyHash, password);
}
