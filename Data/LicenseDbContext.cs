// FathomVisual.LicenseServer/Data/LicenseDbContext.cs
// Entity Framework Core database context for license storage

using FathomVisual.LicenseServer.Models;
using Microsoft.EntityFrameworkCore;

namespace FathomVisual.LicenseServer.Data;

/// <summary>
/// Database context for license management.
/// </summary>
public class LicenseDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseDbContext"/> class.
    /// </summary>
    public LicenseDbContext(DbContextOptions<LicenseDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the licenses table.
    /// </summary>
    public DbSet<LicenseEntity> Licenses => Set<LicenseEntity>();

    /// <summary>
    /// Gets or sets the activations table.
    /// </summary>
    public DbSet<ActivationEntity> Activations => Set<ActivationEntity>();

    /// <summary>
    /// Configures the model and relationships.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Determine if we're using PostgreSQL or SQLite
        var isPostgres = Database.IsNpgsql();

        // License entity configuration
        modelBuilder.Entity<LicenseEntity>(entity =>
        {
            entity.ToTable("Licenses");

            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.LicenseKey)
                .IsUnique();

            entity.HasIndex(e => e.LicenseeEmail);

            entity.HasIndex(e => e.ProductId);

            entity.Property(e => e.Tier)
                .HasConversion<int>();

            entity.Property(e => e.Type)
                .HasConversion<int>();

            entity.HasMany(e => e.Activations)
                .WithOne(a => a.License)
                .HasForeignKey(a => a.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Activation entity configuration
        modelBuilder.Entity<ActivationEntity>(entity =>
        {
            entity.ToTable("Activations");

            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.LicenseId);

            entity.HasIndex(e => e.HardwareFingerprint);

            // Use database-specific filter syntax for partial unique index
            var indexBuilder = entity.HasIndex(e => new { e.LicenseId, e.HardwareFingerprint })
                .IsUnique();

            // PostgreSQL uses quoted column names, SQLite uses brackets
            if (isPostgres)
            {
                indexBuilder.HasFilter("\"IsActive\" = true");
            }
            else
            {
                indexBuilder.HasFilter("[IsActive] = 1");
            }
        });
    }
}
