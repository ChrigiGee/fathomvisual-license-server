// FathomVisual.LicenseServer/Controllers/AdminController.cs
// Admin API controller for license management

using FathomVisual.LicenseServer.Data;
using FathomVisual.LicenseServer.Models;
using FathomVisual.LicenseServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FathomVisual.LicenseServer.Controllers;

/// <summary>
/// Admin API controller for managing licenses.
/// </summary>
[ApiController]
[Route("api/v1/admin/licenses")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly LicenseDbContext _dbContext;
    private readonly LicenseGeneratorService _licenseGenerator;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(
        LicenseDbContext dbContext,
        LicenseGeneratorService licenseGenerator,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _licenseGenerator = licenseGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new license.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateLicenseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateLicenseAsync([FromBody] CreateLicenseRequest request)
    {
        // Validate API key (simple implementation - use proper auth in production)
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        _logger.LogInformation("Creating new license for {Email}", request.LicenseeEmail);

        // Generate a unique license key
        string licenseKey;
        do
        {
            licenseKey = _licenseGenerator.GenerateLicenseKey();
        } while (await _dbContext.Licenses.AnyAsync(l => l.LicenseKey == licenseKey));

        // Calculate features based on tier
        var features = GetFeaturesForTier(request.Tier);

        // Create the license entity
        var license = new LicenseEntity
        {
            Id = Guid.NewGuid(),
            LicenseKey = licenseKey,
            ProductId = request.ProductId ?? "fathom-visual",
            Tier = request.Tier,
            Type = request.Type,
            LicenseeName = request.LicenseeName,
            LicenseeEmail = request.LicenseeEmail,
            LicenseeOrganization = request.LicenseeOrganization,
            MaxActivations = request.MaxActivations ?? GetDefaultMaxActivations(request.Tier),
            MaxChannels = request.MaxChannels ?? GetDefaultMaxChannels(request.Tier),
            Features = (long)features,
            CreatedAt = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt
        };

        _dbContext.Licenses.Add(license);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created license {LicenseId} with key {LicenseKey}",
            license.Id, MaskLicenseKey(licenseKey));

        return CreatedAtAction(
            nameof(GetLicenseAsync),
            new { key = licenseKey },
            new CreateLicenseResponse
            {
                LicenseId = license.Id,
                LicenseKey = licenseKey,
                Tier = license.Tier.ToString(),
                Type = license.Type.ToString(),
                MaxActivations = license.MaxActivations,
                ExpiresAt = license.ExpiresAt,
                CreatedAt = license.CreatedAt
            });
    }

    /// <summary>
    /// Lists all licenses.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ListLicensesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListLicensesAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? email = null,
        [FromQuery] LicenseTier? tier = null,
        [FromQuery] bool? isActive = null)
    {
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        var query = _dbContext.Licenses
            .Include(l => l.Activations.Where(a => a.IsActive))
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(email))
        {
            query = query.Where(l => l.LicenseeEmail.Contains(email));
        }

        if (tier.HasValue)
        {
            query = query.Where(l => l.Tier == tier.Value);
        }

        if (isActive.HasValue)
        {
            if (isActive.Value)
            {
                query = query.Where(l => !l.IsRevoked && !l.IsSuspended &&
                    (!l.ExpiresAt.HasValue || l.ExpiresAt > DateTime.UtcNow));
            }
            else
            {
                query = query.Where(l => l.IsRevoked || l.IsSuspended ||
                    (l.ExpiresAt.HasValue && l.ExpiresAt <= DateTime.UtcNow));
            }
        }

        var totalCount = await query.CountAsync();

        var licenses = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LicenseSummaryDto
            {
                LicenseId = l.Id,
                LicenseKey = MaskLicenseKey(l.LicenseKey),
                Tier = l.Tier.ToString(),
                Type = l.Type.ToString(),
                LicenseeName = l.LicenseeName,
                LicenseeEmail = l.LicenseeEmail,
                LicenseeOrganization = l.LicenseeOrganization,
                IsValid = l.IsValid,
                IsExpired = l.IsExpired,
                IsRevoked = l.IsRevoked,
                IsSuspended = l.IsSuspended,
                ExpiresAt = l.ExpiresAt,
                ActiveActivations = l.CurrentActivations,
                MaxActivations = l.MaxActivations,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();

        return Ok(new ListLicensesResponse
        {
            Licenses = licenses,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Gets a specific license by key.
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(LicenseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLicenseAsync(string key)
    {
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        var normalizedKey = key.Trim().ToUpperInvariant();

        var license = await _dbContext.Licenses
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.LicenseKey == normalizedKey);

        if (license == null)
        {
            return NotFound(new { Message = "License not found" });
        }

        return Ok(new LicenseDetailDto
        {
            LicenseId = license.Id,
            LicenseKey = license.LicenseKey,
            ProductId = license.ProductId,
            Tier = license.Tier.ToString(),
            Type = license.Type.ToString(),
            LicenseeName = license.LicenseeName,
            LicenseeEmail = license.LicenseeEmail,
            LicenseeOrganization = license.LicenseeOrganization,
            MaxActivations = license.MaxActivations,
            CurrentActivations = license.CurrentActivations,
            MaxChannels = license.MaxChannels,
            Features = license.Features,
            IsValid = license.IsValid,
            IsExpired = license.IsExpired,
            IsRevoked = license.IsRevoked,
            IsSuspended = license.IsSuspended,
            RevocationReason = license.RevocationReason,
            SuspensionReason = license.SuspensionReason,
            CreatedAt = license.CreatedAt,
            IssuedAt = license.IssuedAt,
            ExpiresAt = license.ExpiresAt,
            RevokedAt = license.RevokedAt,
            LastModifiedAt = license.LastModifiedAt,
            Activations = license.Activations.Select(a => new ActivationDto
            {
                ActivationId = a.Id,
                HardwareFingerprint = MaskFingerprint(a.HardwareFingerprint),
                MachineName = a.MachineName,
                OperatingSystem = a.OperatingSystem,
                ProductVersion = a.ProductVersion,
                IsActive = a.IsActive,
                ActivatedAt = a.ActivatedAt,
                LastSeenAt = a.LastSeenAt,
                DeactivatedAt = a.DeactivatedAt,
                DeactivationReason = a.DeactivationReason
            }).ToList()
        });
    }

    /// <summary>
    /// Updates a license.
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(LicenseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLicenseAsync(string key, [FromBody] UpdateLicenseRequest request)
    {
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        var normalizedKey = key.Trim().ToUpperInvariant();

        var license = await _dbContext.Licenses.FirstOrDefaultAsync(l => l.LicenseKey == normalizedKey);

        if (license == null)
        {
            return NotFound(new { Message = "License not found" });
        }

        // Update fields
        if (request.Tier.HasValue)
        {
            license.Tier = request.Tier.Value;
            license.Features = (long)GetFeaturesForTier(request.Tier.Value);
            license.MaxChannels = GetDefaultMaxChannels(request.Tier.Value);
        }

        if (request.Type.HasValue)
            license.Type = request.Type.Value;

        if (request.MaxActivations.HasValue)
            license.MaxActivations = request.MaxActivations.Value;

        if (request.MaxChannels.HasValue)
            license.MaxChannels = request.MaxChannels.Value;

        if (request.ExpiresAt.HasValue)
            license.ExpiresAt = request.ExpiresAt.Value;

        if (!string.IsNullOrEmpty(request.LicenseeName))
            license.LicenseeName = request.LicenseeName;

        if (!string.IsNullOrEmpty(request.LicenseeEmail))
            license.LicenseeEmail = request.LicenseeEmail;

        if (request.LicenseeOrganization != null)
            license.LicenseeOrganization = request.LicenseeOrganization;

        if (request.IsSuspended.HasValue)
        {
            license.IsSuspended = request.IsSuspended.Value;
            license.SuspensionReason = request.SuspensionReason;
        }

        license.LastModifiedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated license {LicenseId}", license.Id);

        return await GetLicenseAsync(key);
    }

    /// <summary>
    /// Revokes a license.
    /// </summary>
    [HttpDelete("{key}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeLicenseAsync(string key, [FromBody] RevokeLicenseRequest? request = null)
    {
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        var normalizedKey = key.Trim().ToUpperInvariant();

        var license = await _dbContext.Licenses
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.LicenseKey == normalizedKey);

        if (license == null)
        {
            return NotFound(new { Message = "License not found" });
        }

        license.IsRevoked = true;
        license.RevocationReason = request?.Reason ?? "Administrative revocation";
        license.RevokedAt = DateTime.UtcNow;
        license.LastModifiedAt = DateTime.UtcNow;

        // Deactivate all activations
        foreach (var activation in license.Activations.Where(a => a.IsActive))
        {
            activation.IsActive = false;
            activation.DeactivatedAt = DateTime.UtcNow;
            activation.DeactivationReason = "License revoked";
        }
        license.CurrentActivations = 0;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Revoked license {LicenseId}. Reason: {Reason}",
            license.Id, license.RevocationReason);

        return Ok(new { Message = "License revoked successfully", LicenseId = license.Id });
    }

    /// <summary>
    /// Gets the public key for license verification.
    /// </summary>
    [HttpGet("/api/v1/admin/public-key")]
    [ProducesResponseType(typeof(PublicKeyResponse), StatusCodes.Status200OK)]
    public IActionResult GetPublicKey()
    {
        if (!ValidateAdminKey())
        {
            return Unauthorized(new { Message = "Invalid admin key" });
        }

        return Ok(new PublicKeyResponse
        {
            PublicKey = _licenseGenerator.GetPublicKeyPem(),
            Algorithm = "RS256"
        });
    }

    private bool ValidateAdminKey()
    {
        // Simple API key validation - use proper authentication in production
        var apiKey = Request.Headers["X-Admin-Key"].FirstOrDefault();
        var configuredKey = Environment.GetEnvironmentVariable("ADMIN_API_KEY") ?? "dev-admin-key";
        return apiKey == configuredKey;
    }

    private static LicenseFeatures GetFeaturesForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.None => LicenseFeatures.None,
        LicenseTier.Trial => LicenseFeatures.CoreFeatures | LicenseFeatures.AIDetection,
        LicenseTier.Basic => LicenseFeatures.CoreFeatures,
        LicenseTier.Professional => LicenseFeatures.CoreFeatures | LicenseFeatures.ProfessionalFeatures,
        LicenseTier.Enterprise => LicenseFeatures.AllFeatures,
        LicenseTier.Site => LicenseFeatures.AllFeatures,
        _ => LicenseFeatures.None
    };

    private static int GetDefaultMaxActivations(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 1,
        LicenseTier.Basic => 2,
        LicenseTier.Professional => 3,
        LicenseTier.Enterprise => int.MaxValue,
        LicenseTier.Site => int.MaxValue,
        _ => 1
    };

    private static int GetDefaultMaxChannels(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 2,
        LicenseTier.Basic => 2,
        LicenseTier.Professional => 6,
        LicenseTier.Enterprise => int.MaxValue,
        LicenseTier.Site => int.MaxValue,
        _ => 0
    };

    private static string MaskLicenseKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 10)
            return "****";
        return $"****-****-****-****-{key[^5..]}";
    }

    private static string MaskFingerprint(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint) || fingerprint.Length < 16)
            return "****";
        return $"{fingerprint[..8]}...{fingerprint[^8..]}";
    }
}

#region Request/Response DTOs

public class CreateLicenseRequest
{
    public required string LicenseeName { get; set; }
    public required string LicenseeEmail { get; set; }
    public string? LicenseeOrganization { get; set; }
    public string? ProductId { get; set; }
    public LicenseTier Tier { get; set; } = LicenseTier.Basic;
    public LicenseType Type { get; set; } = LicenseType.Subscription;
    public int? MaxActivations { get; set; }
    public int? MaxChannels { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class CreateLicenseResponse
{
    public Guid LicenseId { get; set; }
    public required string LicenseKey { get; set; }
    public required string Tier { get; set; }
    public required string Type { get; set; }
    public int MaxActivations { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ListLicensesResponse
{
    public List<LicenseSummaryDto> Licenses { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class LicenseSummaryDto
{
    public Guid LicenseId { get; set; }
    public required string LicenseKey { get; set; }
    public required string Tier { get; set; }
    public required string Type { get; set; }
    public required string LicenseeName { get; set; }
    public required string LicenseeEmail { get; set; }
    public string? LicenseeOrganization { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int ActiveActivations { get; set; }
    public int MaxActivations { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LicenseDetailDto
{
    public Guid LicenseId { get; set; }
    public required string LicenseKey { get; set; }
    public required string ProductId { get; set; }
    public required string Tier { get; set; }
    public required string Type { get; set; }
    public required string LicenseeName { get; set; }
    public required string LicenseeEmail { get; set; }
    public string? LicenseeOrganization { get; set; }
    public int MaxActivations { get; set; }
    public int CurrentActivations { get; set; }
    public int MaxChannels { get; set; }
    public long Features { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsSuspended { get; set; }
    public string? RevocationReason { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public List<ActivationDto> Activations { get; set; } = new();
}

public class ActivationDto
{
    public Guid ActivationId { get; set; }
    public required string HardwareFingerprint { get; set; }
    public string? MachineName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? ProductVersion { get; set; }
    public bool IsActive { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivationReason { get; set; }
}

public class UpdateLicenseRequest
{
    public LicenseTier? Tier { get; set; }
    public LicenseType? Type { get; set; }
    public int? MaxActivations { get; set; }
    public int? MaxChannels { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? LicenseeName { get; set; }
    public string? LicenseeEmail { get; set; }
    public string? LicenseeOrganization { get; set; }
    public bool? IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }
}

public class RevokeLicenseRequest
{
    public string? Reason { get; set; }
}

public class PublicKeyResponse
{
    public required string PublicKey { get; set; }
    public required string Algorithm { get; set; }
}

#endregion
