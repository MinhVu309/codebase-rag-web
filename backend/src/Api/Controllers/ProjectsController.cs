using Application.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController(ProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ProjectSummaryDto>> List(CancellationToken ct) =>
        await projects.ListAsync(User.GetUserId(), ct);

    [HttpPost]
    [ProducesResponseType<ProjectDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var project = await projects.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    }

    [HttpGet("{id:guid}")]
    public async Task<ProjectDetailDto> Get(Guid id, CancellationToken ct) =>
        await projects.GetAsync(User.GetUserId(), id, ct);

    [HttpPut("{id:guid}")]
    public async Task<ProjectDetailDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        await projects.UpdateAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
