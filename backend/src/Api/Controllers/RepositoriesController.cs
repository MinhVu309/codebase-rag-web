using Api.Security;
using Application.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Authorize]
public class RepositoriesController(RepositoryService repositories, CodeFileService files) : ControllerBase
{
    /// <summary>Starts indexing. Returns 202 with the repository in <c>pending</c>; poll GET for progress.</summary>
    [HttpPost("api/projects/{projectId:guid}/repositories")]
    [EnableRateLimiting(RateLimitPolicies.Import)]
    [ProducesResponseType<RepositoryDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Import(Guid projectId, ImportRepositoryRequest request, CancellationToken ct)
    {
        var repo = await repositories.ImportAsync(User.GetUserId(), projectId, request, ct);
        return AcceptedAtAction(nameof(Get), new { id = repo.Id }, repo);
    }

    [HttpGet("api/repositories/{id:guid}")]
    public async Task<RepositoryDto> Get(Guid id, CancellationToken ct) =>
        await repositories.GetAsync(User.GetUserId(), id, ct);

    /// <summary>
    /// Code viewer: a slice of one file of the indexed snapshot. <c>start</c> defaults to 1, <c>end</c> to
    /// <c>start + 1999</c>; at most 2000 lines. 404 for someone else's or unknown repository, 409 until it is ready.
    /// </summary>
    [HttpGet("api/repositories/{id:guid}/files")]
    public async Task<CodeFileDto> ReadFile(Guid id, [FromQuery] string? path, [FromQuery] int? start, [FromQuery] int? end, CancellationToken ct) =>
        await files.ReadAsync(User.GetUserId(), id, new ReadFileRequest(path, start, end), ct);

    [HttpDelete("api/repositories/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await repositories.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
