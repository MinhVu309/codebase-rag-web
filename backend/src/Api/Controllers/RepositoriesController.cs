using Application.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
public class RepositoriesController(RepositoryService repositories) : ControllerBase
{
    /// <summary>Starts indexing. Returns 202 with the repository in <c>pending</c>; poll GET for progress.</summary>
    [HttpPost("api/projects/{projectId:guid}/repositories")]
    [ProducesResponseType<RepositoryDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Import(Guid projectId, ImportRepositoryRequest request, CancellationToken ct)
    {
        var repo = await repositories.ImportAsync(User.GetUserId(), projectId, request, ct);
        return AcceptedAtAction(nameof(Get), new { id = repo.Id }, repo);
    }

    [HttpGet("api/repositories/{id:guid}")]
    public async Task<RepositoryDto> Get(Guid id, CancellationToken ct) =>
        await repositories.GetAsync(User.GetUserId(), id, ct);

    [HttpDelete("api/repositories/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await repositories.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
