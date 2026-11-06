using Application.Common;
using Application.Rag;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Middleware;

/// <summary>Maps application exceptions to RFC 7807 ProblemDetails. Request bodies are never logged.</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationFailedException v:
                problem = new ValidationProblemDetails(v.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed",
                };
                break;
            case NotFoundException n:
                problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = n.Message };
                break;
            case ConflictException c:
                problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = c.Message };
                if (c.Code is not null) problem.Extensions["code"] = c.Code;
                break;
            case UnauthorizedException u:
                problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = u.Message };
                break;
            case RagException r:
                problem = RagProblemMapper.ToProblem(r);
                logger.LogWarning("RAG call failed: {Kind} {Code} (upstream {Status})", r.Kind, r.Code, r.UpstreamStatus);
                break;
            default:
                // Log the exception type/stack only; exception messages here never carry credentials.
                logger.LogError(exception, "Unhandled exception");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal server error",
                };
                break;
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
