using Api.Controllers;
using Api.Middleware;
using Api.Security;
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
builder.Services.AddApiSecurity();

var app = builder.Build();

// Apply pending EF migrations on startup (MVP; compose waits for postgres to be healthy).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (string.Equals(app.Configuration["AUTH_COOKIE_SECURE"], "false", StringComparison.OrdinalIgnoreCase))
    app.Logger.LogWarning("AUTH_COOKIE_SECURE=false: auth cookies are sent over plain http. Local development only; production must serve https and leave this unset.");

app.UseApiForwardedHeaders();   // real client IP for the rate limiter, only from TRUSTED_PROXIES
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(); // ProblemDetails body for bare 401/404/etc.
app.UseMiddleware<RequestSizeLimitMiddleware>(); // after the exception handler: its 413 goes through ProblemDetails

// No HTTPS redirection: TLS is terminated by the reverse proxy (nginx) in front of the API.
app.UseApiCors();               // only when CORS_ORIGINS is set; before auth so preflights need no cookie
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();           // after auth: the ask/import limits are per user
app.MapControllers();

app.Run();

// Exposed so Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
