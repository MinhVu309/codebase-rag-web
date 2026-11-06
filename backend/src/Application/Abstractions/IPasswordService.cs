namespace Application.Abstractions;

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);

    /// <summary>Burns roughly the same CPU as <see cref="Verify"/>; used when the account does not exist.</summary>
    void BurnVerifyTime(string password);
}
