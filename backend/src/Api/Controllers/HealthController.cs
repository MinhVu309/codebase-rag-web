using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    /// <summary>Liveness probe. Does not touch the database or the RAG service.</summary>
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new { status = "healthy" });
}
