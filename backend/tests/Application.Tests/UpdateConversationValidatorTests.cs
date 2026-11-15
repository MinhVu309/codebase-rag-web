using Application.Chat;

namespace Application.Tests;

public class UpdateConversationValidatorTests
{
    private static readonly UpdateConversationRequestValidator Validator = new();

    private static bool IsValid(string? title) => Validator.Validate(new UpdateConversationRequest(title)).IsValid;

    [Theory]
    [InlineData("Auth flow")]
    [InlineData("  padded  ")]
    [InlineData("  trailing newline is trimmed away\n")]
    [InlineData("Tiếng Việt có dấu – và emoji 🚀")]
    [InlineData("x")]
    public void Good_Titles_Pass(string title) => Assert.True(IsValid(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("a\u0085b")]   // NEL (C1 control)
    public void Bad_Titles_Fail(string? title) => Assert.False(IsValid(title));

    [Fact]
    public void Length_Is_Judged_After_Trimming_With_A_Limit_Of_200()
    {
        Assert.True(IsValid(new string('x', 200)));
        Assert.True(IsValid("   " + new string('x', 200) + "   "));
        Assert.False(IsValid(new string('x', 201)));
        Assert.False(IsValid("   " + new string('x', 201)));
    }
}
