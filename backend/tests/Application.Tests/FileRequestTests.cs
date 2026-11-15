using Application.Common;
using Application.Repositories;

namespace Application.Tests;

public class FileRequestTests
{
    private static readonly ReadFileRequestValidator Validator = new();

    [Theory]
    [InlineData(null, null, 1, 2000)]
    [InlineData(1, null, 1, 2000)]
    [InlineData(34, 36, 34, 36)]
    [InlineData(5, 5, 5, 5)]
    [InlineData(1, 2000, 1, 2000)]
    [InlineData(2000, 3999, 2000, 3999)]
    [InlineData(int.MaxValue, null, int.MaxValue, int.MaxValue)]
    public void Valid_Ranges_Resolve(int? start, int? end, int expectedStart, int expectedEnd) =>
        Assert.Equal((expectedStart, expectedEnd), FileRange.Resolve(start, end));

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, 5)]
    [InlineData(1, 0)]
    [InlineData(null, -3)]
    [InlineData(10, 9)]
    [InlineData(1, 2001)]
    [InlineData(2, 2002)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void Invalid_Ranges_Throw(int? start, int? end) =>
        Assert.Throws<InvalidRangeException>(() => FileRange.Resolve(start, end));

    [Theory]
    [InlineData("src/flask/app.py")]
    [InlineData("README.md")]
    [InlineData("a b/c d.txt")]
    [InlineData("src/ünï/файл.cs")]
    public void Good_Paths_Pass(string path) =>
        Assert.True(Validator.Validate(new ReadFileRequest(path, null, null)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("a/..")]
    [InlineData("/abs")]
    [InlineData("\\abs")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\u007Fb")]
    public void Bad_Paths_Fail(string? path) =>
        Assert.False(Validator.Validate(new ReadFileRequest(path, null, null)).IsValid);

    [Fact]
    public void Path_Length_Boundary_Is_1024()
    {
        Assert.True(Validator.Validate(new ReadFileRequest(new string('a', 1024), null, null)).IsValid);
        Assert.False(Validator.Validate(new ReadFileRequest(new string('a', 1025), null, null)).IsValid);
    }
}
