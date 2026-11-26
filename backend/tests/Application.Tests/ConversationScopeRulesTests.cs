using Application.Chat;
using Application.Repositories;

namespace Application.Tests;

public class ConversationScopeRulesTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    // ---------- CreateConversationRequestValidator ----------

    [Fact]
    public void Initial_Repository_Is_Optional_Without_Settings_And_In_Single_Mode()
    {
        var plain = new CreateConversationRequestValidator();
        var single = new CreateConversationRequestValidator(new ConversationScopeSettings(5, InitialRepositoryRequired: false));

        Assert.True(plain.Validate(new CreateConversationRequest("t")).IsValid);
        Assert.True(single.Validate(new CreateConversationRequest(null)).IsValid);
        Assert.True(single.Validate(new CreateConversationRequest("t", Repo)).IsValid);
    }

    [Fact]
    public void Initial_Repository_Is_Required_In_Multi_Mode()
    {
        var multi = new CreateConversationRequestValidator(new ConversationScopeSettings(5, InitialRepositoryRequired: true));

        var missing = multi.Validate(new CreateConversationRequest("t"));

        Assert.False(missing.IsValid);
        Assert.Equal("InitialRepositoryId", Assert.Single(missing.Errors).PropertyName);
        Assert.True(multi.Validate(new CreateConversationRequest("t", Repo)).IsValid);
    }

    [Fact]
    public void Multi_Mode_Reports_Title_And_Repository_Errors_Together()
    {
        var multi = new CreateConversationRequestValidator(new ConversationScopeSettings(5, true));

        var result = multi.Validate(new CreateConversationRequest("a\nb"));

        Assert.Equal(["InitialRepositoryId", "Title"], result.Errors.Select(e => e.PropertyName).Distinct().Order());
    }

    [Fact]
    public void The_Old_One_Argument_Construction_Still_Works_And_Defaults_The_New_Field()
    {
        var request = new CreateConversationRequest("t");

        Assert.Null(request.InitialRepositoryId);
    }

    // ---------- AddScopeRepositoryRequestValidator ----------

    [Fact]
    public void Add_Needs_A_Repository_Id()
    {
        var validator = new AddScopeRepositoryRequestValidator();

        Assert.False(validator.Validate(new AddScopeRepositoryRequest(null)).IsValid);
        Assert.True(validator.Validate(new AddScopeRepositoryRequest(Repo)).IsValid);
    }

    // ---------- GitHubRepoUrl.DisplayName ----------

    [Theory]
    [InlineData("https://github.com/pallets/flask", "pallets/flask")]
    [InlineData("https://github.com/octo/hello.world", "octo/hello.world")]
    [InlineData("  https://github.com/o/r_1-x  ", "o/r_1-x")]
    public void DisplayName_Is_Owner_Slash_Repo(string url, string expected) =>
        Assert.Equal(expected, GitHubRepoUrl.DisplayName(url));

    [Fact]
    public void DisplayName_Of_Something_Unexpected_Is_Kept_Not_Thrown_And_Capped_At_300()
    {
        Assert.Equal("", GitHubRepoUrl.DisplayName(null));
        Assert.Equal("not a url", GitHubRepoUrl.DisplayName("not a url"));
        Assert.Equal(300, GitHubRepoUrl.DisplayName("https://example.com/" + new string('a', 500)).Length);
    }

    [Fact]
    public void DisplayName_Of_A_Normalized_Url_Round_Trips()
    {
        Assert.True(GitHubRepoUrl.TryNormalize("https://github.com/pallets/flask.git/", out var normalized));

        Assert.Equal("pallets/flask", GitHubRepoUrl.DisplayName(normalized));
    }
}
