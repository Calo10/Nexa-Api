using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Invites;
using NexaApi.Services;
using System.Security.Claims;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/orgs/{orgId}/invites")]
public class InvitesController : ControllerBase
{
    private readonly IInvitesService _invitesService;
    private readonly ILogger<InvitesController> _logger;

    public InvitesController(IInvitesService invitesService, ILogger<InvitesController> logger)
    {
        _invitesService = invitesService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<InviteResponse>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListPendingInvites(Guid orgId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var invites = await _invitesService.ListPendingInvitesAsync(orgId, userId, cancellationToken);
        return Ok(invites);
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(InviteResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendInvite(Guid orgId, [FromBody] SendInviteRequest request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { error = "Role is required" });
        }

        var invite = await _invitesService.SendInviteAsync(orgId, userId, request.Email, request.Role, cancellationToken);

        if (invite == null)
        {
            return NotFound(new { error = "Organization not found, access denied, or failed to send invite" });
        }

        return CreatedAtAction(nameof(SendInvite), new { orgId, inviteId = invite.InviteId }, invite);
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Token is required" });
        }

        var result = await _invitesService.AcceptInviteAsync(request.Token, cancellationToken);

        if (!result)
        {
            return Unauthorized(new { error = "Invalid, expired, or already used token" });
        }

        return Ok(new { message = "Invite accepted successfully" });
    }

    [HttpPost("{inviteId}/resend")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvite(Guid orgId, Guid inviteId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var result = await _invitesService.ResendInviteAsync(orgId, userId, inviteId, cancellationToken);

        if (!result)
        {
            return NotFound(new { error = "Invite not found, access denied, or failed to resend" });
        }

        return Ok(new { message = "Invite resent successfully" });
    }

    [HttpDelete("{inviteId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeInvite(Guid orgId, Guid inviteId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var result = await _invitesService.RevokeInviteAsync(orgId, userId, inviteId, cancellationToken);

        if (!result)
        {
            return NotFound(new { error = "Invite not found, access denied, or already accepted" });
        }

        return NoContent();
    }
}

