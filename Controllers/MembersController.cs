using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Members;
using NexaApi.Services;
using System.Security.Claims;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/orgs/{orgId}/members")]
[Authorize]
public class MembersController : ControllerBase
{
    private readonly IMembersService _membersService;
    private readonly ILogger<MembersController> _logger;

    public MembersController(IMembersService membersService, ILogger<MembersController> logger)
    {
        _membersService = membersService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MemberResponse>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembers(Guid orgId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var members = await _membersService.ListMembersAsync(orgId, userId, cancellationToken);

        return Ok(members);
    }

    [HttpPatch("{memberId}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberRole(Guid orgId, Guid memberId, [FromBody] UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { error = "Role is required" });
        }

        var result = await _membersService.ChangeRoleAsync(orgId, userId, memberId, request.Role, cancellationToken);

        if (!result)
        {
            return NotFound(new { error = "Member not found, access denied, insufficient permissions, or cannot change last owner" });
        }

        return Ok(new { message = "Member role updated successfully" });
    }

    [HttpDelete("{memberId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid orgId, Guid memberId, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var result = await _membersService.RemoveMemberAsync(orgId, userId, memberId, cancellationToken);

        if (!result)
        {
            return NotFound(new { error = "Member not found, access denied, insufficient permissions, or cannot remove last owner" });
        }

        return Ok(new { message = "Member removed successfully" });
    }
}

