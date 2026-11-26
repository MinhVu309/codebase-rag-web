using Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

/// <summary>
/// Conversation Scope (D40): the repositories a conversation may search. Responses never carry the RAG index id.
/// A conversation that is not yours answers 404, the same as one that does not exist.
/// </summary>
[ApiController]
[Authorize]
[Route("api/conversations/{id:guid}/repositories")]
public class ConversationScopeController(ConversationScopeService scope) : ControllerBase
{
    /// <summary>The Scope, oldest member first.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ScopeRepositoryDto>> List(Guid id, CancellationToken ct) =>
        await scope.ListAsync(User.GetUserId(), id, ct);

    /// <summary>Adds a ready repository of the same project. Already a member: 200, unchanged. Returns the Scope after the call.</summary>
    [HttpPost]
    public async Task<IReadOnlyList<ScopeRepositoryDto>> Add(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AddScopeRepositoryRequest? request, CancellationToken ct) =>
        await scope.AddAsync(User.GetUserId(), id, request, ct);

    /// <summary>Removes a member (204). The last member cannot be removed (409).</summary>
    [HttpDelete("{repositoryId:guid}")]
    public async Task<IActionResult> Remove(Guid id, Guid repositoryId, CancellationToken ct)
    {
        await scope.RemoveAsync(User.GetUserId(), id, repositoryId, ct);
        return NoContent();
    }
}
