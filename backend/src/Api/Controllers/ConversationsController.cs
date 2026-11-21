using Api.Security;
using Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Authorize]
public class ConversationsController(ChatService chat) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/conversations")]
    public async Task<IReadOnlyList<ConversationSummaryDto>> List(Guid projectId, CancellationToken ct) =>
        await chat.ListConversationsAsync(User.GetUserId(), projectId, ct);

    [HttpPost("api/projects/{projectId:guid}/conversations")]
    [ProducesResponseType<ConversationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid projectId, CreateConversationRequest request, CancellationToken ct)
    {
        var conversation = await chat.CreateConversationAsync(User.GetUserId(), projectId, request, ct);
        return CreatedAtAction(nameof(Messages), new { id = conversation.Id }, conversation);
    }

    /// <summary>Renames a conversation (title 1-200 characters after trimming). 404 if it is not yours.</summary>
    [HttpPatch("api/conversations/{id:guid}")]
    public async Task<ConversationDto> Rename(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateConversationRequest? request, CancellationToken ct) =>
        await chat.RenameConversationAsync(User.GetUserId(), id, request, ct);

    /// <summary>Deletes a conversation with all its messages and citation snapshots. 404 if it is not yours.</summary>
    [HttpDelete("api/conversations/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await chat.DeleteConversationAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Latest <c>limit</c> messages (default 200, max 500), oldest first.</summary>
    [HttpGet("api/conversations/{id:guid}/messages")]
    public async Task<IReadOnlyList<MessageDto>> Messages(Guid id, [FromQuery] int? limit, CancellationToken ct) =>
        await chat.GetMessagesAsync(User.GetUserId(), id, limit, ct);

    /// <summary>Asks one question. Stores the user message and the answer (with its citation snapshot).</summary>
    [HttpPost("api/conversations/{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.Ask)]
    [ProducesResponseType<AskResultDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Ask(Guid id, AskRequest request, CancellationToken ct)
    {
        var result = await chat.AskAsync(User.GetUserId(), id, request, ct);
        return CreatedAtAction(nameof(Messages), new { id }, result);
    }
}
