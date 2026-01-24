// FathomVisual.LicenseServer/Models/LicenseEntity.cs
// Database entity for license storage

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FathomVisual.LicenseServer.Models;

/// <summary>
/// Database entity representing a license.
/// </summary>
public class LicenseEntity
{
    /// <summary>
    /// Gets or sets the unique license identifier.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the license key (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX format).
    /// </summary>
    [Required]
    [MaxLength(29)]
    public required string LicenseKey { get; set; }

    /// <summary>
    /// Gets or sets the product ID this license is for.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public required string ProductId { get; set; }

    /// <summary>
    /// Gets or sets the license tier (Trial, Basic, Professional, Enterprise, Site).
    /// </summary>
    [Required]
    public LicenseTier Tier { get; set; }

    /// <summary>
    /// Gets or sets the license type.
    /// </summary>
    [Required]
    public LicenseType Type { get; set; }

    /// <summary>
    /// Gets or sets the licensee name.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string LicenseeName { get; set; }

    /// <summary>
    /// Gets or sets the licensee email.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string LicenseeEmail { get; set; }

    /// <summary>
    /// Gets or sets the licensee organization.
    /// </summary>
    [MaxLength(200)]
    public string? LicenseeOrganization { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of activations allowed.
    /// </summary>
    public int MaxActivations { get; set; } = 1;

    /// <summary>
    /// Gets or sets the current activation count.
    /// </summary>
    public int CurrentActivations { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of channels.
    /// </summary>
    public int MaxChannels { get; set; } = 2;

    /// <summary>
    /// Gets or sets the enabled features as a bitmask.
    /// </summary>
    public long Features { get; set; }

    /// <summary>
    /// Gets or sets when the license was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the license was issued.
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the license expires (null for perpetual).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets whether the license is revoked.
    /// </summary>
    public bool IsRevoked { get; set; } = false;

    /// <summary>
    /// Gets or sets the revocation reason.
    /// </summary>
    [MaxLength(500)]
    public string? RevocationReason { get; set; }

    /// <summary>
    /// Gets or sets when the license was revoked.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the license is suspended.
    /// </summary>
    public bool IsSuspended { get; set; } = false;

    /// <summary>
    /// Gets or sets the suspension reason.
    /// </summary>
    [MaxLength(500)]
    public string? SuspensionReason { get; set; }

    /// <summary>
    /// Gets or sets additional metadata as JSON.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Gets or sets the last modified timestamp.
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the activations for this license.
    /// </summary>
    public virtual ICollection<ActivationEntity> Activations { get; set; } = new List<ActivationEntity>();

    /// <summary>
    /// Gets whether the license has expired.
    /// </summary>
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// Gets whether the license is valid (not expired, not revoked, not suspended).
    /// </summary>
    [NotMapped]
    public bool IsValid => !IsExpired && !IsRevoked && !IsSuspended;

    /// <summary>
    /// Gets the remaining activations.
    /// </summary>
    [NotMapped]
    public int RemainingActivations => Math.Max(0, MaxActivations - CurrentActivations);

    /// <summary>
    /// Gets whether more activations are available.
    /// </summary>
    [NotMapped]
    public bool HasAvailableActivations => RemainingActivations > 0 || MaxActivations == int.MaxValue;
}

/// <summary>
/// License tier enumeration matching the client.
/// </summary>
public enum LicenseTier
{
    None = 0,
    Trial = 1,
    Basic = 2,
    Professional = 3,
    Enterprise = 4,
    Site = 5
}

/// <summary>
/// License type enumeration matching the client.
/// </summary>
public enum LicenseType
{
    Trial = 0,
    Subscription = 1,
    Perpetual = 2,
    Floating = 3,
    Site = 4,
    Educational = 5,
    NFR = 6
}

/// <summary>
/// License feature flags matching the client.
/// </summary>
[Flags]
public enum LicenseFeatures : long
{
    None = 0,
    LiveRecording = 1L << 0,
    VideoPlayback = 1L << 1,
    BasicOverlay = 1L << 2,
    VideoExport = 1L << 3,
    ScreenshotCapture = 1L << 4,
    AdvancedOverlay = 1L << 10,
    AIDetection = 1L << 11,
    PointCloud3D = 1L << 12,
    FullReporting = 1L << 13,
    CloudSync = 1L << 14,
    Collaboration = 1L << 15,
    CustomFormats = 1L << 16,
    ExternalIntegration = 1L << 17,
    FloatingLicense = 1L << 20,
    ApiAccess = 1L << 21,
    CustomBranding = 1L << 22,
    AirGappedMode = 1L << 23,
    UnlimitedChannels = 1L << 24,
    PriorityProcessing = 1L << 25,
    AuditLogging = 1L << 26,
    SSOIntegration = 1L << 27,
    WorkflowAutomation = 1L << 28,

    CoreFeatures = LiveRecording | VideoPlayback | BasicOverlay | VideoExport | ScreenshotCapture,
    ProfessionalFeatures = AdvancedOverlay | AIDetection | PointCloud3D | FullReporting |
                           CloudSync | Collaboration | CustomFormats | ExternalIntegration,
    EnterpriseFeatures = FloatingLicense | ApiAccess | CustomBranding | AirGappedMode |
                         UnlimitedChannels | PriorityProcessing | AuditLogging |
                         SSOIntegration | WorkflowAutomation,
    AllFeatures = CoreFeatures | ProfessionalFeatures | EnterpriseFeatures
}
