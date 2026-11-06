using Application.Auth;

namespace Application.Tests;

public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _register = new();
    private readonly LoginRequestValidator _login = new();

    [Theory]
    [InlineData("user@example.com", "12345678", true)]
    [InlineData("user@example.com", "1234567", false)]          // too short
    [InlineData("user@example.com", null, false)]
    [InlineData("not-an-email", "12345678", false)]
    [InlineData(null, "12345678", false)]
    public void Register_Validation(string? email, string? password, bool valid)
    {
        Assert.Equal(valid, _register.Validate(new RegisterRequest(email, password)).IsValid);
    }

    [Fact]
    public void Register_Rejects_Overlong_Password_And_Email()
    {
        var longPassword = new string('a', AuthRules.MaxPasswordLength + 1);
        var longEmail = new string('a', AuthRules.MaxEmailLength) + "@example.com";

        Assert.False(_register.Validate(new RegisterRequest("user@example.com", longPassword)).IsValid);
        Assert.False(_register.Validate(new RegisterRequest(longEmail, "12345678")).IsValid);
    }

    [Theory]
    [InlineData("user@example.com", "anything", true)]
    [InlineData("", "anything", false)]
    [InlineData("user@example.com", "", false)]
    public void Login_Validation(string email, string password, bool valid)
    {
        Assert.Equal(valid, _login.Validate(new LoginRequest(email, password)).IsValid);
    }
}
