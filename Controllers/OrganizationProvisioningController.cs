using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Organizations;
using NexaApi.Services;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/orgs")]
public class OrganizationProvisioningController : ControllerBase
{
    private readonly IOrganizationsService _organizationsService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrganizationProvisioningController> _logger;

    public OrganizationProvisioningController(
        IOrganizationsService organizationsService,
        IConfiguration configuration,
        ILogger<OrganizationProvisioningController> logger)
    {
        _organizationsService = organizationsService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Provisions a new organization and its admin user in a single operation (no end-user JWT required).
    /// Protected by X-Api-Key (Provisioning:ApiKey).
    /// </summary>
    [HttpPost("provision")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ProvisionOrganizationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ProvisionOrganization(
        [FromBody] ProvisionOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidProvisioningApiKey())
        {
            return Unauthorized(new { error = "Invalid or missing provisioning API key" });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Organization name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            return BadRequest(new { error = "Admin email is required" });
        }

        if (!TryNormalizeEmail(request.AdminEmail, out var adminEmail, out var emailError))
        {
            return BadRequest(new { error = emailError });
        }

        var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim();

        var result = await _organizationsService.ProvisionOrganizationAsync(
            request.Name.Trim(),
            timezone,
            adminEmail,
            request.AdminFullName,
            cancellationToken);

        if (result == null)
        {
            return BadRequest(new { error = "Failed to provision organization. Admin user may be disabled." });
        }

        return Created($"/v1/orgs/{result.OrganizationId}", result);
    }

    private bool IsValidProvisioningApiKey()
    {
        var configuredKey = _configuration["Provisioning:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogError("Provisioning:ApiKey is not configured");
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
        {
            return false;
        }

        return string.Equals(configuredKey, providedKey.ToString(), StringComparison.Ordinal);
    }

    private static bool TryNormalizeEmail(string email, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        try
        {
            var trimmed = email.Trim();
            var mailAddress = new System.Net.Mail.MailAddress(trimmed);
            if (!string.Equals(mailAddress.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid email format";
                return false;
            }

            normalized = trimmed;
            return true;
        }
        catch
        {
            error = "Invalid email format";
            return false;
        }
    }
}
