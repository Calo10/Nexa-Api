using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Organizations;
using NexaApi.Services;
using System.Security.Claims;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/orgs")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationsService _organizationsService;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(IOrganizationsService organizationsService, ILogger<OrganizationsController> logger)
    {
        _organizationsService = organizationsService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(OrganizationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        // Try both 'sub' (JWT standard) and NameIdentifier (ASP.NET standard)
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Organization name is required" });
        }

        var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone;

        var organization = await _organizationsService.CreateOrganizationAsync(request.Name, timezone, userId, cancellationToken);

        if (organization == null)
        {
            return BadRequest(new { error = "Failed to create organization" });
        }

        return CreatedAtAction(nameof(GetOrganization), new { orgId = organization.OrganizationId }, organization);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<OrganizationResponse>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListOrganizations(CancellationToken cancellationToken)
    {
        // Try both 'sub' (JWT standard) and NameIdentifier (ASP.NET standard)
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var organizations = await _organizationsService.ListOrganizationsAsync(userId, cancellationToken);

        return Ok(organizations);
    }

    [HttpGet("{orgId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(OrganizationDetailResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganization(Guid orgId, CancellationToken cancellationToken)
    {
        // Try both 'sub' (JWT standard) and NameIdentifier (ASP.NET standard)
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var organization = await _organizationsService.GetOrganizationAsync(orgId, userId, cancellationToken);

        if (organization == null)
        {
            return NotFound(new { error = "Organization not found or access denied" });
        }

        return Ok(organization);
    }

    [HttpPost("{orgId}/switch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchActiveOrganization(Guid orgId, CancellationToken cancellationToken)
    {
        // Try both 'sub' (JWT standard) and NameIdentifier (ASP.NET standard)
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        // Get session ID from claim (we'll need to add this to JWT claims)
        // For now, we'll get it from a custom claim or header
        var sessionIdClaim = User.FindFirst("session_id")?.Value;
        if (string.IsNullOrEmpty(sessionIdClaim) || !Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            return BadRequest(new { error = "Session ID is required" });
        }

        var result = await _organizationsService.SwitchActiveOrganizationAsync(orgId, userId, sessionId, cancellationToken);

        if (!result)
        {
            return NotFound(new { error = "Organization not found, access denied, or switch failed" });
        }

        return Ok(new { message = "Active organization switched successfully" });
    }
}

