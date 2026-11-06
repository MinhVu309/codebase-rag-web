using Application.Auth;
using Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, AuthCookies cookies) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        cookies.Append(Response, result);
        return StatusCode(StatusCodes.Status201Created, result.User);
    }

    [HttpPost("login")]
    public async Task<ActionResult<UserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        cookies.Append(Response, result);
        return result.User;
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<UserDto>> Refresh(CancellationToken ct)
    {
        try
        {
            var result = await auth.RefreshAsync(Request.Cookies[AuthCookies.RefreshTokenCookie], ct);
            cookies.Append(Response, result);
            return result.User;
        }
        catch (UnauthorizedException)
        {
            cookies.Clear(Response); // a dead session should not keep sending stale cookies
            throw;
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[AuthCookies.RefreshTokenCookie], ct);
        cookies.Clear(Response);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        return await auth.GetCurrentUserAsync(User.GetUserId(), ct);
    }
}
