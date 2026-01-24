// FathomVisual.LicenseServer/Models/ActivationEntity.cs
// Database entity for license activations

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FathomVisual.LicenseServer.Models;

/// <summary>
/// Database entity representing a license activation on a specific machine.
/// </summary>
public class ActivationEntity
{
    /// <summary>
    /// Gets or sets the unique activation identifier.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the license ID this activation belongs to.
    /// </summary>
    [Required]
    public Guid LicenseId { get; set; }

    /// <summary>
    /// Gets or sets the hardware fingerprint hash.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public required string HardwareFingerprint { get; set; }

    /// <summary>
    /// Gets or sets the individual hardware component hashes as JSON.
    /// </summary>
    public string? HardwareComponentsJson { get; set; }

    /// <summary>
    /// Gets or sets the machine name.
    /// </summary>
    [MaxLength(200)]
    public string? MachineName { get; set; }

    /// <summary>
    /// Gets or sets the operating system information.
    /// </summary>
    [MaxLength(200)]
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// Gets or sets the product version that was activated.
    /// </summary>
    [MaxLength(50)]
    public string? ProductVersion { get; set; }

    /// <summary>
    /// Gets or sets when the activation was created.
    /// </summary>
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the activation was last seen (heartbeat).
    /// </summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets whether the activation is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when the activation was deactivated.
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Gets or sets the deactivation reason.
    /// </summary>
    [MaxLength(100)]
    public string? DeactivationReason { get; set; }

    /// <summary>
    /// Gets or sets the IP address of the activation request.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Gets or sets the user agent of the activation request.
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets additional metadata as JSON.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Navigation property to the license.
    /// </summary>
    [ForeignKey(nameof(LicenseId))]
    public virtual LicenseEntity? License { get; set; }
}

/// <summary>
/// Deactivation reason enumeration.
/// </summary>
public enum DeactivationReason
{
    UserRequested,
    MachineRetirement,
    MachineTransfer,
    LicenseReturn,
    Administrative,
    Expired,
    Revoked
}
