using Application.Repositories;

namespace Application.Tests;

public class GitHubRepoUrlTests
{
    [Theory]
    [InlineData("https://github.com/octo/hello", "https://github.com/octo/hello")]
    [InlineData("  https://github.com/octo/hello  ", "https://github.com/octo/hello")]
    [InlineData("https://github.com/octo/hello/", "https://github.com/octo/hello")]
    [InlineData("https://github.com/octo/hello.git", "https://github.com/octo/hello")]
    [InlineData("https://github.com/octo/hello.git/", "https://github.com/octo/hello")]
    [InlineData("https://github.com/Octo-Cat/my.repo_name-2", "https://github.com/Octo-Cat/my.repo_name-2")]
    [InlineData("https://github.com/a/b", "https://github.com/a/b")]
    public void Accepts_And_Normalizes_Valid_Urls(string input, string expected)
    {
        Assert.True(GitHubRepoUrl.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://github.com/o/r")]
    [InlineData("https://www.github.com/o/r")]
    [InlineData("https://github.com.evil.io/o/r")]
    [InlineData("https://github.com/o/r/issues")]
    [InlineData("https://github.com/o/r?tab=readme")]
    [InlineData("https://github.com/o/r#x")]
    [InlineData("https://u:p@github.com/o/r")]
    [InlineData("https://github.com:443/o/r")]
    [InlineData("https://github.com/o")]
    [InlineData("https://github.com//r")]
    [InlineData("https://github.com/o/.")]
    [InlineData("https://github.com/o/..")]
    [InlineData("https://github.com/-o/r")]
    [InlineData("https://github.com/o-/r")]
    [InlineData("https://github.com/o/r r")]
    [InlineData("https://github.com/o/r\nhttps://evil.com")]
    public void Rejects_Everything_Else(string? input)
    {
        Assert.False(GitHubRepoUrl.TryNormalize(input, out var normalized));
        Assert.Equal("", normalized);
    }

    [Fact]
    public void Owner_Longer_Than_39_Chars_Is_Rejected()
    {
        Assert.True(GitHubRepoUrl.TryNormalize($"https://github.com/{new string('a', 39)}/r", out _));
        Assert.False(GitHubRepoUrl.TryNormalize($"https://github.com/{new string('a', 40)}/r", out _));
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("develop", true)]
    [InlineData("release/1.2.3", true)]
    [InlineData("v1.0", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("feature/a_b-c", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    [InlineData("--upload-pack=x", false)]
    [InlineData("/abs", false)]
    [InlineData("a..b", false)]
    [InlineData("a//b", false)]
    [InlineData("a/", false)]
    [InlineData("a b", false)]
    [InlineData("a;b", false)]
    [InlineData("a$(id)", false)]
    [InlineData("a\nb", false)]
    [InlineData("branch.lock", false)]
    public void Ref_Validation(string? gitRef, bool valid) => Assert.Equal(valid, GitHubRepoUrl.IsValidRef(gitRef));

    [Fact]
    public void Ref_Longer_Than_255_Is_Rejected() =>
        Assert.False(GitHubRepoUrl.IsValidRef(new string('a', 256)));
}
