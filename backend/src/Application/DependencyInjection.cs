using Application.Auth;
using Application.Projects;
using Application.Repositories;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>();
        services.AddScoped<AuthService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<RepositoryService>();
        return services;
    }
}
