// FathomVisual.LicenseServer/Controllers/LicensesController.cs
// API controller for license operations

using System.Text.Json;
using FathomVisual.LicenseServer.Data;
using FathomVisual.LicenseServer.Models;
using FathomVisual.LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FathomVisual.LicenseServer.Controllers;

/// <summary>
/// API controller for license validation, activation, and deactivation.
/// </summary>
[ApiController]
[Route("api/v1/licenses")]
[Produces("application/json")]
public class LicensesController : ControllerBase
{
    private readonly LicenseDbContext _dbContext;
    private readonly LicenseGeneratorService _licenseGenerator;
    private readonly ILogger<LicensesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicensesController"/> class.
    /// </summary>
    public LicensesController(
        LicenseDbContext dbContext,
        LicenseGeneratorService licenseGenerator,
        ILogger<LicensesController> logger)
    {
        _dbContext = dbContext;
        _licenseGenerator = licenseGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Validates a license.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateAsync([FromBody] ValidationRequest request)
    {
        _logger.LogDebug("Validating license {LicenseId}", request.LicenseId);

        var activation = await _dbContext.Activations
            .Include(a => a.License)
            .FirstOrDefaultAsync(a =>
                a.LicenseId == request.LicenseId &&
                a.HardwareFingerprint == request.HardwareFingerprint &&
                a.IsActive);

        if (activation?.License == null)
        {
            return NotFound(new ValidationResponse
            {
                IsValid = false,
                Status = (int)LicenseStatusCode.NotFound,
                Message = "License not found or not activated on this machine"
            });
        }

        var license = activation.License;

        // Update last seen
        activation.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Check license validity
        if (license.IsRevoked)
        {
            return Ok(new ValidationResponse
            {
                IsValid = false,
                Status = (int)LicenseStatusCode.Revoked,
                Message = "License has been revoked",
                IsRevoked = true,
                RevocationReason = license.RevocationReason
            });
        }

        if (license.IsSuspended)
        {
            return Ok(new ValidationResponse
            {
                IsValid = false,
                Status = (int)LicenseStatusCode.Suspended,
                Message = "License is suspended: " + license.SuspensionReason
            });
        }

        if (license.IsExpired)
        {
            return Ok(new ValidationResponse
            {
                IsValid = false,
                Status = (int)LicenseStatusCode.Expired,
                Message = "License has expired"
            });
        }

        // License is valid - return updated signed license
        var signedLicense = _licenseGenerator.CreateSignedLicense(license, request.HardwareFingerprint);

        return Ok(new ValidationResponse
        {
            IsValid = true,
            Status = (int)LicenseStatusCode.Valid,
            Message = "License is valid",
            ServerTimestamp = DateTime.UtcNow,
            NextValidationRequired = DateTime.UtcNow.AddDays(7),
            UpdatedLicense = signedLicense
        });
    }

    /// <summary>
    /// Activates a license.
    /// </summary>
    [HttpPost("activate")]
    [ProducesResponseType(typeof(ActivationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateAsync([FromBody] ActivationRequest request)
    {
        _logger.LogInformation("Processing activation request {RequestId} for key {LicenseKey}",
            request.RequestId, MaskLicenseKey(request.LicenseKey));

        // Normalize license key
        var normalizedKey = NormalizeLicenseKey(request.LicenseKey);

        // Find the license
        var license = await _dbContext.Licenses
            .Include(l => l.Activations.Where(a => a.IsActive))
            .FirstOrDefaultAsync(l => l.LicenseKey == normalizedKey);

        if (license == null)
        {
            _logger.LogWarning("License key not found: {LicenseKey}", MaskLicenseKey(request.LicenseKey));
            return NotFound(new ActivationResponse
            {
                Success = false,
                RequestId = request.RequestId,
                ErrorCode = (int)ActivationErrorCode.KeyNotFound,
                ErrorMessage = "License key not found"
            });
        }

        // Check if license is valid
        if (license.IsRevoked)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ActivationResponse
            {
                Success = false,
                RequestId = request.RequestId,
                ErrorCode = (int)ActivationErrorCode.KeyRevoked,
                ErrorMessage = "License has been revoked"
            });
        }

        if (license.IsExpired)
        {
            return StatusCode(StatusCodes.Status410Gone, new ActivationResponse
            {
                Success = false,
                RequestId = request.RequestId,
                ErrorCode = (int)ActivationErrorCode.LicenseExpired,
                ErrorMessage = "License has expired"
            });
        }

        // Check if already activated on this machine
        var existingActivation = license.Activations
            .FirstOrDefault(a => a.HardwareFingerprint == request.HardwareFingerprint && a.IsActive);

        if (existingActivation != null)
        {
            _logger.LogInformation("License already activated on this machine");

            // Update activation info
            existingActivation.LastSeenAt = DateTime.UtcNow;
            existingActivation.ProductVersion = request.ProductVersion;
            await _dbContext.SaveChangesAsync();

            var signedLicense = _licenseGenerator.CreateSignedLicense(license, request.HardwareFingerprint);

            return Ok(new ActivationResponse
            {
                Success = true,
                RequestId = request.RequestId,
                License = signedLicense,
                RemainingActivations = license.RemainingActivations
            });
        }

        // Check activation limit
        if (!license.HasAvailableActivations)
        {
            _logger.LogWarning("Max activations reached for license {LicenseId}", license.Id);
            return Conflict(new ActivationResponse
            {
                Success = false,
                RequestId = request.RequestId,
                ErrorCode = (int)ActivationErrorCode.MaxActivationsReached,
                ErrorMessage = $"Maximum activations ({license.MaxActivations}) reached",
                RemainingActivations = 0
            });
        }

        // Create new activation
        var activation = new ActivationEntity
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            HardwareFingerprint = request.HardwareFingerprint,
            HardwareComponentsJson = request.HardwareComponents != null
                ? JsonSerializer.Serialize(request.HardwareComponents)
                : null,
            MachineName = request.MachineName,
            OperatingSystem = request.OperatingSystem,
            ProductVersion = request.ProductVersion,
            ActivatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IsActive = true,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        };

        _dbContext.Activations.Add(activation);
        license.CurrentActivations++;
        license.LastModifiedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Successfully activated license {LicenseId} on machine {MachineName}",
            license.Id, request.MachineName);

        var newSignedLicense = _licenseGenerator.CreateSignedLicense(license, request.HardwareFingerprint);

        return Ok(new ActivationResponse
        {
            Success = true,
            RequestId = request.RequestId,
            License = newSignedLicense,
            RemainingActivations = license.RemainingActivations,
            GeneratedAt = DateTime.UtcNow,
            ServerTimestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Deactivates a license.
    /// </summary>
    [HttpPost("deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync([FromBody] DeactivationRequest request)
    {
        _logger.LogInformation("Processing deactivation request for license {LicenseId}", request.LicenseId);

        var activation = await _dbContext.Activations
            .Include(a => a.License)
            .FirstOrDefaultAsync(a =>
                a.LicenseId == request.LicenseId &&
                a.HardwareFingerprint == request.HardwareFingerprint &&
                a.IsActive);

        if (activation == null)
        {
            return NotFound(new { Message = "Activation not found" });
        }

        activation.IsActive = false;
        activation.DeactivatedAt = DateTime.UtcNow;
        activation.DeactivationReason = request.Reason;

        if (activation.License != null)
        {
            activation.License.CurrentActivations = Math.Max(0, activation.License.CurrentActivations - 1);
            activation.License.LastModifiedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Successfully deactivated license {LicenseId} from machine", request.LicenseId);

        return Ok(new { Message = "Deactivation successful" });
    }

    /// <summary>
    /// Reactivates a license after hardware change.
    /// </summary>
    [HttpPost("reactivate")]
    [ProducesResponseType(typeof(ActivationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAsync([FromBody] ReactivationRequest request)
    {
        _logger.LogInformation("Processing reactivation request for license {LicenseId}", request.LicenseId);

        var license = await _dbContext.Licenses
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.Id == request.LicenseId);

        if (license == null)
        {
            return NotFound(new ActivationResponse
            {
                Success = false,
                RequestId = Guid.NewGuid(),
                ErrorCode = (int)ActivationErrorCode.KeyNotFound,
                ErrorMessage = "License not found"
            });
        }

        // Deactivate all existing activations for this license
        foreach (var activation in license.Activations.Where(a => a.IsActive))
        {
            activation.IsActive = false;
            activation.DeactivatedAt = DateTime.UtcNow;
            activation.DeactivationReason = "Reactivation on new hardware";
        }

        // Create new activation
        var newActivation = new ActivationEntity
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            HardwareFingerprint = request.NewHardwareFingerprint,
            ActivatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IsActive = true,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

        _dbContext.Activations.Add(newActivation);
        license.CurrentActivations = 1;
        license.LastModifiedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        var signedLicense = _licenseGenerator.CreateSignedLicense(license, request.NewHardwareFingerprint);

        return Ok(new ActivationResponse
        {
            Success = true,
            RequestId = Guid.NewGuid(),
            License = signedLicense,
            RemainingActivations = license.RemainingActivations
        });
    }

    /// <summary>
    /// Gets the license status.
    /// </summary>
    [HttpGet("{key}/status")]
    [ProducesResponseType(typeof(LicenseStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatusAsync(string key)
    {
        var normalizedKey = NormalizeLicenseKey(key);

        var license = await _dbContext.Licenses
            .Include(l => l.Activations.Where(a => a.IsActive))
            .FirstOrDefaultAsync(l => l.LicenseKey == normalizedKey);

        if (license == null)
        {
            return NotFound(new { Message = "License not found" });
        }

        return Ok(new LicenseStatusResponse
        {
            LicenseId = license.Id,
            Tier = license.Tier.ToString(),
            Type = license.Type.ToString(),
            IsValid = license.IsValid,
            IsExpired = license.IsExpired,
            IsRevoked = license.IsRevoked,
            IsSuspended = license.IsSuspended,
            ExpiresAt = license.ExpiresAt,
            ActiveActivations = license.CurrentActivations,
            MaxActivations = license.MaxActivations,
            RemainingActivations = license.RemainingActivations
        });
    }

    /// <summary>
    /// Refreshes the license data.
    /// </summary>
    [HttpGet("{licenseId:guid}/refresh")]
    [ProducesResponseType(typeof(SignedLicenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshAsync(Guid licenseId)
    {
        var activation = await _dbContext.Activations
            .Include(a => a.License)
            .FirstOrDefaultAsync(a => a.LicenseId == licenseId && a.IsActive);

        if (activation?.License == null)
        {
            return NotFound();
        }

        activation.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var signedLicense = _licenseGenerator.CreateSignedLicense(
            activation.License,
            activation.HardwareFingerprint);

        return Ok(signedLicense);
    }

    /// <summary>
    /// Receives a heartbeat from the client.
    /// </summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType(typeof(HeartbeatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> HeartbeatAsync([FromBody] HeartbeatRequest request)
    {
        var activation = await _dbContext.Activations
            .Include(a => a.License)
            .FirstOrDefaultAsync(a =>
                a.LicenseId == request.LicenseId &&
                a.HardwareFingerprint == request.HardwareFingerprint &&
                a.IsActive);

        if (activation == null)
        {
            return Ok(new HeartbeatResponse
            {
                Accepted = false,
                Status = (int)LicenseStatusCode.NotFound,
                NextHeartbeatIntervalMinutes = 5,
                ActionRequired = true,
                RequiredAction = "Reactivate",
                ActionMessage = "License activation not found"
            });
        }

        activation.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var license = activation.License!;

        if (license.IsRevoked)
        {
            return Ok(new HeartbeatResponse
            {
                Accepted = false,
                Status = (int)LicenseStatusCode.Revoked,
                ActionRequired = true,
                RequiredAction = "Revoked"
            });
        }

        if (license.IsExpired)
        {
            return Ok(new HeartbeatResponse
            {
                Accepted = false,
                Status = (int)LicenseStatusCode.Expired,
                ActionRequired = true,
                RequiredAction = "RefreshLicense"
            });
        }

        return Ok(new HeartbeatResponse
        {
            Accepted = true,
            Status = (int)LicenseStatusCode.Valid,
            ServerTimestamp = DateTime.UtcNow,
            NextHeartbeatIntervalMinutes = 15
        });
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("/api/v1/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Receives usage reports from clients.
    /// </summary>
    [HttpPost("/api/v1/usage/report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ReportUsage([FromBody] object report)
    {
        // In a real implementation, store usage data for analytics
        _logger.LogDebug("Received usage report");
        return Ok();
    }

    private static string NormalizeLicenseKey(string key)
    {
        return key?.Trim().ToUpperInvariant().Replace(" ", "") ?? string.Empty;
    }

    private static string MaskLicenseKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 10)
            return "****";
        return $"****-****-****-****-{key[^5..]}";
    }
}

#region Request/Response DTOs

public class ValidationRequest
{
    public Guid LicenseId { get; set; }
    public required string HardwareFingerprint { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ValidationResponse
{
    public bool IsValid { get; set; }
    public int Status { get; set; }
    public string? Message { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime? NextValidationRequired { get; set; }
    public SignedLicenseDto? UpdatedLicense { get; set; }
}

public class ActivationRequest
{
    public Guid RequestId { get; set; }
    public required string LicenseKey { get; set; }
    public required string ProductId { get; set; }
    public required string ProductVersion { get; set; }
    public required string HardwareFingerprint { get; set; }
    public Dictionary<string, string>? HardwareComponents { get; set; }
    public string? MachineName { get; set; }
    public string? OperatingSystem { get; set; }
    public DateTime RequestedAt { get; set; }
    public bool IsOffline { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class ActivationResponse
{
    public bool Success { get; set; }
    public Guid RequestId { get; set; }
    public SignedLicenseDto? License { get; set; }
    public int ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RemainingActivations { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
    public byte[]? Signature { get; set; }
}

public class DeactivationRequest
{
    public Guid LicenseId { get; set; }
    public required string HardwareFingerprint { get; set; }
    public string? Reason { get; set; }
    public DateTime RequestedAt { get; set; }
}

public class ReactivationRequest
{
    public Guid LicenseId { get; set; }
    public required string NewHardwareFingerprint { get; set; }
    public DateTime Timestamp { get; set; }
}

public class HeartbeatRequest
{
    public Guid LicenseId { get; set; }
    public required string HardwareFingerprint { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ApplicationVersion { get; set; }
}

public class HeartbeatResponse
{
    public bool Accepted { get; set; }
    public int Status { get; set; }
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
    public int NextHeartbeatIntervalMinutes { get; set; } = 15;
    public bool ActionRequired { get; set; }
    public string? RequiredAction { get; set; }
    public string? ActionMessage { get; set; }
}

public class LicenseStatusResponse
{
    public Guid LicenseId { get; set; }
    public required string Tier { get; set; }
    public required string Type { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int ActiveActivations { get; set; }
    public int MaxActivations { get; set; }
    public int RemainingActivations { get; set; }
}

public enum LicenseStatusCode
{
    NotFound = 0,
    Valid = 1,
    ExpiredGracePeriod = 2,
    Expired = 3,
    InvalidSignature = 4,
    HardwareMismatch = 5,
    HardwareMismatchGracePeriod = 6,
    Revoked = 7,
    RequiresActivation = 8,
    Corrupted = 9,
    ValidOffline = 10,
    Suspended = 11,
    MaxActivationsReached = 12,
    Pending = 13,
    Tampered = 14
}

public enum ActivationErrorCode
{
    None = 0,
    InvalidKeyFormat = 1,
    KeyNotFound = 2,
    KeyRevoked = 3,
    MaxActivationsReached = 4,
    LicenseExpired = 5,
    NetworkError = 6,
    ServerError = 7,
    HardwareFingerprintError = 8,
    StorageError = 9,
    InvalidResponse = 10,
    SignatureVerificationFailed = 11,
    ProductMismatch = 12,
    Unknown = 99
}

#endregion
