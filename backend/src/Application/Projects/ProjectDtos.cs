using Application.Repositories;

namespace Application.Projects;

public record CreateProjectRequest(string? Name);

public record UpdateProjectRequest(string? Name);

public record ProjectSummaryDto(Guid Id, string Name, DateTimeOffset CreatedAt, int RepositoryCount);

public record ProjectDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RepositoryDto> Repositories);
