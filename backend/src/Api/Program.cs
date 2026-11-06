var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

// No HTTPS redirection: TLS is terminated by the reverse proxy (nginx) in front of the API.
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
