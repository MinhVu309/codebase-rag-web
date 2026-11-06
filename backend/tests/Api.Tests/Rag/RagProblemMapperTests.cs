using Api.Middleware;
using Application.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Rag;

public class RagProblemMapperTests
{
    [Theory]
    [InlineData(RagFailureKind.NotReady, "INDEX_NOT_READY", 409, "INDEX_NOT_READY")]
    [InlineData(RagFailureKind.NotFound, "FILE_NOT_FOUND", 404, "FILE_NOT_FOUND")]
    [InlineData(RagFailureKind.NotFound, "INDEX_NOT_FOUND", 404, "INDEX_NOT_FOUND")]
    [InlineData(RagFailureKind.Invalid, "INVALID_RANGE", 400, "INVALID_RANGE")]
    [InlineData(RagFailureKind.Invalid, "INVALID_REPO", 422, "INVALID_REPO")]
    [InlineData(RagFailureKind.Invalid, "REPO_TOO_LARGE", 422, "REPO_TOO_LARGE")]
    [InlineData(RagFailureKind.LlmError, "LLM_ERROR", 502, "LLM_ERROR")]
    [InlineData(RagFailureKind.Timeout, "TIMEOUT", 504, "TIMEOUT")]
    [InlineData(RagFailureKind.Unavailable, "RAG_UNAVAILABLE", 502, "RAG_UNAVAILABLE")]
    [InlineData(RagFailureKind.Internal, "INTERNAL_ERROR", 502, "RAG_UNAVAILABLE")]
    [InlineData(RagFailureKind.InvalidResponse, "RAG_INVALID_RESPONSE", 502, "RAG_UNAVAILABLE")]
    // A rejected API key is our deployment problem: shown to users as a generic outage, not as "unauthorized".
    [InlineData(RagFailureKind.Unauthorized, "UNAUTHORIZED", 502, "RAG_UNAVAILABLE")]
    public void Maps_Status_And_Stable_Code(RagFailureKind kind, string ragCode, int status, string code)
    {
        var problem = RagProblemMapper.ToProblem(new RagException(kind, ragCode, upstreamMessage: "internal detail"));

        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.DoesNotContain("internal detail", problem.Detail);
    }

    [Fact]
    public async Task ExceptionHandler_Writes_The_Mapped_Status_On_The_Response()
    {
        var writer = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(writer, NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(context,
            new RagException(RagFailureKind.NotReady, RagErrorCodes.IndexNotReady, 409), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal(409, writer.Last!.ProblemDetails.Status);
    }

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Last { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Last = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Last = context;
            return ValueTask.FromResult(true);
        }
    }
}
