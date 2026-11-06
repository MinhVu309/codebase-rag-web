using Application.Rag;
using Domain.Enums;
using Infrastructure.Rag;

namespace Api.Tests.Rag;

public class FakeRagClientTests
{
    private readonly FakeRagClient _fake = new();

    private async Task<string> CreateReadyIndexAsync()
    {
        var created = await _fake.CreateIndexAsync("https://github.com/o/r", "main", "c");
        RagIndexStatus status;
        do { status = await _fake.GetIndexAsync(created.IndexId); } while (status.Status != IndexStatus.Ready);
        return created.IndexId;
    }

    [Fact]
    public async Task Index_Progresses_To_Ready_Then_Answers_Queries_With_Citations()
    {
        var created = await _fake.CreateIndexAsync("https://github.com/o/r", "main", "c");
        Assert.Equal(IndexStatus.Pending, created.Status);

        var seen = new List<IndexStatus>();
        RagIndexStatus status;
        do { status = await _fake.GetIndexAsync(created.IndexId); seen.Add(status.Status); }
        while (status.Status != IndexStatus.Ready);

        Assert.Equal([IndexStatus.Cloning, IndexStatus.Parsing, IndexStatus.Embedding, IndexStatus.Ready], seen);
        Assert.NotNull(status.CommitSha);

        var answer = await _fake.QueryAsync(created.IndexId, "anything", 5);
        Assert.Contains("[1]", answer.Answer);
        Assert.Equal(1, Assert.Single(answer.Citations).RefId);
    }

    [Fact]
    public async Task Query_Before_Ready_Is_409_NotReady()
    {
        var created = await _fake.CreateIndexAsync("https://github.com/o/r", "main", "c");

        var ex = await Assert.ThrowsAsync<RagException>(() => _fake.QueryAsync(created.IndexId, "q", 5));

        Assert.Equal(RagFailureKind.NotReady, ex.Kind);
    }

    [Fact]
    public async Task Unknown_Index_Is_NotFound()
    {
        var ex = await Assert.ThrowsAsync<RagException>(() => _fake.GetIndexAsync("nope"));
        Assert.Equal(RagErrorCodes.IndexNotFound, ex.Code);
    }

    [Fact]
    public async Task Too_Large_Repo_Ends_Failed_With_Error_Code()
    {
        var created = await _fake.CreateIndexAsync("https://github.com/o/too-large", "main", "c");
        await _fake.GetIndexAsync(created.IndexId);

        var status = await _fake.GetIndexAsync(created.IndexId);

        Assert.Equal(IndexStatus.Failed, status.Status);
        Assert.Equal(RagErrorCodes.RepoTooLarge, status.Error!.Code);
    }

    [Fact]
    public async Task Invalid_Repo_Is_Rejected_Synchronously()
    {
        var ex = await Assert.ThrowsAsync<RagException>(() => _fake.CreateIndexAsync("https://github.com/o/invalid", "main", "c"));
        Assert.Equal(RagFailureKind.Invalid, ex.Kind);
    }

    [Fact]
    public async Task Files_Validates_Range_And_Path()
    {
        var id = await CreateReadyIndexAsync();

        var file = await _fake.GetFileAsync(id, "src/Program.cs", 1, 2);
        Assert.Equal(2, file.Content.Split('\n').Length);

        Assert.Equal(RagErrorCodes.InvalidRange,
            (await Assert.ThrowsAsync<RagException>(() => _fake.GetFileAsync(id, "src/Program.cs", 5, 2))).Code);
        Assert.Equal(RagErrorCodes.InvalidRange,
            (await Assert.ThrowsAsync<RagException>(() => _fake.GetFileAsync(id, "src/Program.cs", 1, 2001))).Code);
        Assert.Equal(RagErrorCodes.FileNotFound,
            (await Assert.ThrowsAsync<RagException>(() => _fake.GetFileAsync(id, "missing.cs", 1, 2))).Code);
    }

    [Fact]
    public async Task Delete_Is_Idempotent()
    {
        var id = await CreateReadyIndexAsync();

        await _fake.DeleteIndexAsync(id);
        await _fake.DeleteIndexAsync(id);

        await Assert.ThrowsAsync<RagException>(() => _fake.GetIndexAsync(id));
    }
}
