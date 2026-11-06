using Api.Controllers;
using Api.Middleware;
using Application;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddScoped<AuthCookies>();
builder.Services.AddAuthorization();

var app = builder.Build();

// Apply pending EF migrations on startup (MVP; compose waits for postgres to be healthy).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<RequestIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(); // ProblemDetails body for bare 401/404/etc.

// No HTTPS redirection: TLS is terminated by the reverse proxy (nginx) in front of the API.
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
