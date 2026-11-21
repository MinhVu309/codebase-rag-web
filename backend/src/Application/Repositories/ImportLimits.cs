namespace Application.Repositories;

/// <summary>How many repositories one user may have indexing at the same time. 0 = no limit.</summary>
public record ImportLimits(int MaxConcurrentPerUser = 1);
