using Application.Abstractions;
using Application.Common;
using Application.Rag;
using Application.Repositories;
using FluentValidation;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Projects;

/// <summary>Every method takes the caller's <c>userId</c> and only ever touches that user's projects.</summary>
public class ProjectService(
    IAppDbContext db,
    IRagClient rag,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<ProjectSummaryDto>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectSummaryDto(p.Id, p.Name, p.CreatedAt, p.Repositories.Count))
            .ToListAsync(ct);

    public async Task<ProjectDetailDto> GetAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Repositories)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
            ?? throw NotFound();

        var repos = project.Repositories
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => RepositoryDto.From(r, project.ActiveIndexId))
            .ToList();
        return new ProjectDetailDto(project.Id, project.Name, project.CreatedAt, repos);
    }

    public async Task<ProjectDetailDto> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct)
    {
        await createValidator.EnsureValidAsync(request, ct);

        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = request.Name!.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return new ProjectDetailDto(project.Id, project.Name, project.CreatedAt, []);
    }

    public async Task<ProjectDetailDto> UpdateAsync(Guid userId, Guid projectId, UpdateProjectRequest request, CancellationToken ct)
    {
        await updateValidator.EnsureValidAsync(request, ct);

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw NotFound();
        project.Name = request.Name!.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, projectId, ct);
    }

    /// <summary>
    /// Removes the project's indexes from RAG first (DELETE is idempotent), then the project itself.
    /// If RAG fails the project is kept, so the user can simply retry and nothing is orphaned.
    /// </summary>
    public async Task DeleteAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Repositories)
                          .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct)
                      ?? throw NotFound();

        foreach (var repo in project.Repositories.Where(r => r.IndexId is not null))
            await rag.DeleteIndexAsync(repo.IndexId!, ct);

        db.Projects.Remove(project); // repositories, conversations and messages cascade
        await db.SaveChangesAsync(ct);
    }

    private static NotFoundException NotFound() => new("Project not found.");
}
