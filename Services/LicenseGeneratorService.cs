// FathomVisual.LicenseServer/Services/LicenseGeneratorService.cs
// Service for generating and signing licenses

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FathomVisual.LicenseServer.Models;

namespace FathomVisual.LicenseServer.Services;

/// <summary>
/// Service for generating license keys and signing licenses.
/// </summary>
public class LicenseGeneratorService
{
    private readonly RSA _rsaPrivateKey;
    private readonly RSA _rsaPublicKey;
    private readonly ILogger<LicenseGeneratorService> _logger;
    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseGeneratorService"/> class.
    /// </summary>
    public LicenseGeneratorService(
        IConfiguration configuration,
        ILogger<LicenseGeneratorService> logger)
    {
        _logger = logger;

        var keysPath = configuration["License:KeysPath"] ?? "keys";
        _privateKeyPath = Path.Combine(keysPath, "private.pem");
        _publicKeyPath = Path.Combine(keysPath, "public.pem");

        // Ensure keys directory exists
        Directory.CreateDirectory(keysPath);

        // Load or generate RSA keys
        if (File.Exists(_privateKeyPath) && File.Exists(_publicKeyPath))
        {
            _rsaPrivateKey = RSA.Create();
            _rsaPrivateKey.ImportFromPem(File.ReadAllText(_privateKeyPath));

            _rsaPublicKey = RSA.Create();
            _rsaPublicKey.ImportFromPem(File.ReadAllText(_publicKeyPath));

            _logger.LogInformation("Loaded existing RSA key pair from {Path}", keysPath);
        }
        else
        {
            _logger.LogInformation("Generating new RSA key pair...");

            _rsaPrivateKey = RSA.Create(4096);
            _rsaPublicKey = RSA.Create();
            _rsaPublicKey.ImportParameters(_rsaPrivateKey.ExportParameters(false));

            // Export and save keys
            var privateKeyPem = _rsaPrivateKey.ExportRSAPrivateKeyPem();
            var publicKeyPem = _rsaPublicKey.ExportRSAPublicKeyPem();

            File.WriteAllText(_privateKeyPath, privateKeyPem);
            File.WriteAllText(_publicKeyPath, publicKeyPem);

            _logger.LogInformation("Generated and saved new RSA key pair to {Path}", keysPath);
        }
    }

    /// <summary>
    /// Generates a new license key in XXXXX-XXXXX-XXXXX-XXXXX-XXXXX format.
    /// </summary>
    public string GenerateLicenseKey()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Exclude confusing chars
        var segments = new string[5];

        using var rng = RandomNumberGenerator.Create();
        var buffer = new byte[5];

        for (int i = 0; i < 5; i++)
        {
            var segment = new char[5];
            for (int j = 0; j < 5; j++)
            {
                rng.GetBytes(buffer, 0, 1);
                segment[j] = chars[buffer[0] % chars.Length];
            }
            segments[i] = new string(segment);
        }

        return string.Join("-", segments);
    }

    /// <summary>
    /// Creates a signed license from a license entity and hardware fingerprint.
    /// </summary>
    public SignedLicenseDto CreateSignedLicense(LicenseEntity license, string hardwareFingerprint)
    {
        var licenseData = new LicenseDataDto
        {
            LicenseId = license.Id,
            ProductId = license.ProductId,
            Tier = (int)license.Tier,
            Type = (int)license.Type,
            HardwareFingerprint = hardwareFingerprint,
            IssuedAt = license.IssuedAt,
            ExpiresAt = license.ExpiresAt,
            Limits = new LicenseLimitsDto
            {
                MaxChannels = license.MaxChannels,
                MaxActivations = license.MaxActivations,
                MaxConcurrentUsers = GetMaxConcurrentUsers(license.Tier),
                AIQuotaPerMonth = GetAIQuota(license.Tier),
                MaxRecordingMinutesPerSession = GetMaxRecordingMinutes(license.Tier),
                CloudStorageMB = GetCloudStorage(license.Tier),
                MaxExportsPerMonth = GetMaxExports(license.Tier)
            },
            Features = license.Features,
            Licensee = new LicenseeInfoDto
            {
                Name = license.LicenseeName,
                Email = license.LicenseeEmail,
                Organization = license.LicenseeOrganization
            }
        };

        // Serialize and sign
        var licenseJson = JsonSerializer.Serialize(licenseData, JsonOptions);
        var licenseBytes = Encoding.UTF8.GetBytes(licenseJson);
        var signature = _rsaPrivateKey.SignData(
            licenseBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return new SignedLicenseDto
        {
            Data = licenseData,
            Signature = signature,
            SignatureAlgorithm = "RS256",
            LastValidated = DateTime.UtcNow,
            OfflineExpiresAt = DateTime.UtcNow.AddDays(30),
            Version = 1
        };
    }

    /// <summary>
    /// Verifies a signed license.
    /// </summary>
    public bool VerifySignature(SignedLicenseDto signedLicense)
    {
        try
        {
            var licenseJson = JsonSerializer.Serialize(signedLicense.Data, JsonOptions);
            var licenseBytes = Encoding.UTF8.GetBytes(licenseJson);

            return _rsaPublicKey.VerifyData(
                licenseBytes,
                signedLicense.Signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify license signature");
            return false;
        }
    }

    /// <summary>
    /// Gets the public key in PEM format for client distribution.
    /// </summary>
    public string GetPublicKeyPem()
    {
        return _rsaPublicKey.ExportRSAPublicKeyPem();
    }

    private static int? GetMaxConcurrentUsers(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 1,
        LicenseTier.Basic => 1,
        LicenseTier.Professional => 3,
        LicenseTier.Enterprise => null,
        LicenseTier.Site => null,
        _ => 0
    };

    private static int? GetAIQuota(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 10,
        LicenseTier.Basic => 0,
        LicenseTier.Professional => 10000,
        LicenseTier.Enterprise => null,
        LicenseTier.Site => null,
        _ => 0
    };

    private static int? GetMaxRecordingMinutes(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 120,
        _ => null
    };

    private static long? GetCloudStorage(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 0,
        LicenseTier.Basic => 0,
        LicenseTier.Professional => 50000,
        LicenseTier.Enterprise => null,
        LicenseTier.Site => null,
        _ => 0
    };

    private static int? GetMaxExports(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 5,
        _ => null
    };
}

#region DTOs for API responses

/// <summary>
/// License data DTO matching client expectations.
/// </summary>
public class LicenseDataDto
{
    public Guid LicenseId { get; set; }
    public required string ProductId { get; set; }
    public int Tier { get; set; }
    public int Type { get; set; }
    public required string HardwareFingerprint { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public required LicenseLimitsDto Limits { get; set; }
    public long Features { get; set; }
    public required LicenseeInfoDto Licensee { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// License limits DTO matching client expectations.
/// </summary>
public class LicenseLimitsDto
{
    public int MaxChannels { get; set; }
    public int MaxActivations { get; set; }
    public int? MaxConcurrentUsers { get; set; }
    public int? AIQuotaPerMonth { get; set; }
    public int? MaxRecordingMinutesPerSession { get; set; }
    public long? CloudStorageMB { get; set; }
    public int? MaxExportsPerMonth { get; set; }
}

/// <summary>
/// Licensee info DTO matching client expectations.
/// </summary>
public class LicenseeInfoDto
{
    public required string Name { get; set; }
    public string? Organization { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>
/// Signed license DTO matching client expectations.
/// </summary>
public class SignedLicenseDto
{
    public required LicenseDataDto Data { get; set; }
    public required byte[] Signature { get; set; }
    public required string SignatureAlgorithm { get; set; }
    public DateTime? LastValidated { get; set; }
    public DateTime? OfflineExpiresAt { get; set; }
    public int Version { get; set; } = 1;
}

#endregion
