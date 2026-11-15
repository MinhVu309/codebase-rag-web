using Application.Chat;

namespace Application.Tests;

public class ChatValidatorTests
{
    private readonly AskRequestValidator _ask = new();
    private readonly CreateConversationRequestValidator _create = new();

    [Theory]
    [InlineData("hello", null, true)]
    [InlineData("hello", 1, true)]
    [InlineData("hello", 20, true)]
    [InlineData("hello", 0, false)]
    [InlineData("hello", 21, false)]
    [InlineData("hello", -5, false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    public void Ask_Validation(string? question, int? topK, bool valid) =>
        Assert.Equal(valid, _ask.Validate(new AskRequest(question, topK)).IsValid);

    [Fact]
    public void Ask_Length_Limit_Is_Measured_After_Trimming()
    {
        var padded = "  " + new string('a', ChatRules.MaxQuestionLength) + "  ";
        Assert.True(_ask.Validate(new AskRequest(padded, null)).IsValid);
        Assert.False(_ask.Validate(new AskRequest(new string('a', ChatRules.MaxQuestionLength + 1), null)).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("A title", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a\nb", false)]
    [InlineData("a\tb", false)]
    [InlineData("  abc  ", true)]
    [InlineData("abc\n", true)] // trimmed before the control-character check, like rename
    public void Create_Conversation_Title_Validation(string? title, bool valid) =>
        Assert.Equal(valid, _create.Validate(new CreateConversationRequest(title)).IsValid);

    [Fact]
    public void Create_Title_Length_Is_Judged_After_Trimming()
    {
        Assert.True(_create.Validate(new CreateConversationRequest(new string('x', ChatRules.MaxTitleLength))).IsValid);
        Assert.True(_create.Validate(new CreateConversationRequest("  " + new string('x', ChatRules.MaxTitleLength) + "  ")).IsValid);
        Assert.False(_create.Validate(new CreateConversationRequest(new string('x', ChatRules.MaxTitleLength + 1))).IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("  abc  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("a\u0085b")]
    [InlineData("abc\n")]
    public void Create_And_Rename_Agree_On_Every_Non_Null_Title(string title) =>
        Assert.Equal(_create.Validate(new CreateConversationRequest(title)).IsValid,
            new UpdateConversationRequestValidator().Validate(new UpdateConversationRequest(title)).IsValid);
}
