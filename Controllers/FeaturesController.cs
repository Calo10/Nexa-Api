using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Features;
using NexaApi.Services;
using System.Security.Claims;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/features")]
[Authorize]
public class FeaturesController : ControllerBase
{
    private readonly IFeaturesService _featuresService;
    private readonly ILogger<FeaturesController> _logger;

    public FeaturesController(IFeaturesService featuresService, ILogger<FeaturesController> logger)
    {
        _featuresService = featuresService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Dictionary<string, object>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFeatures(CancellationToken cancellationToken)
    {
        var organizationId = GetActiveOrganizationId();

        if (organizationId == null)
        {
            return BadRequest(new { error = "No active organization" });
        }

        var features = await _featuresService.GetResolvedFeaturesAsync(organizationId.Value, cancellationToken);

        return Ok(features);
    }

    private Guid? GetActiveOrganizationId()
    {
        // Get active organization ID from JWT claims (org_id)
        var orgIdClaim = User.FindFirst("org_id")?.Value;
        if (Guid.TryParse(orgIdClaim, out var orgId))
        {
            return orgId;
        }
        return null;
    }
}

