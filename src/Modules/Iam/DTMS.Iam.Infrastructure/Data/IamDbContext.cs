using DTMS.Iam.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Data;

public class IamDbContext : DbContext
{
    public const string Schema = "iam";

    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<PermissionAuditEntry> AuditLog { get; set; } = null!;

    // Phase S.2 — federated source-system integration.
    public DbSet<SystemClient> SystemClients { get; set; } = null!;
    public DbSet<SystemClientPermission> SystemClientPermissions { get; set; } = null!;
    public DbSet<SystemCredential> SystemCredentials { get; set; } = null!;
    public DbSet<SystemRequestLogEntry> SystemRequestLog { get; set; } = null!;

    public IamDbContext(DbContextOptions<IamDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Permission>(b =>
        {
            b.ToTable("Permissions");
            b.HasKey(p => p.Code);
            b.Property(p => p.Code).HasMaxLength(120).IsRequired();
            b.Property(p => p.Description).HasMaxLength(300).IsRequired();
            b.Property(p => p.Module).HasMaxLength(50).IsRequired();
            b.Property(p => p.CreatedAt).IsRequired();
            b.HasIndex(p => p.Module);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.ToTable("RolePermissions");
            b.HasKey(rp => new { rp.Role, rp.PermissionCode });
            b.Property(rp => rp.Role).HasMaxLength(50).IsRequired();
            b.Property(rp => rp.PermissionCode).HasMaxLength(120).IsRequired();
            b.HasIndex(rp => rp.Role);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(r => r.Name);
            b.Property(r => r.Name).HasMaxLength(50).IsRequired();
            b.Property(r => r.Description).HasMaxLength(300).IsRequired();
            b.Property(r => r.IsSystem).IsRequired();
            b.Property(r => r.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<PermissionAuditEntry>(b =>
        {
            b.ToTable("PermissionAuditLog");
            b.HasKey(a => a.Id);
            b.Property(a => a.OccurredAt).IsRequired();
            b.Property(a => a.ActorEmployeeId).HasMaxLength(50).IsRequired();
            b.Property(a => a.Action).HasMaxLength(50).IsRequired();
            b.Property(a => a.Role).HasMaxLength(50);
            b.Property(a => a.PermissionCode).HasMaxLength(120);
            b.Property(a => a.Details).HasColumnType("text");
            // Indexes match the ones the migration creates (newest-first
            // listing + filter by actor + filter by role).
        });

        // ── Phase S.2 — federated source-system integration ─────────────

        modelBuilder.Entity<SystemClient>(b =>
        {
            b.ToTable("SystemClients");
            b.HasKey(s => s.Key);
            b.Property(s => s.Key).HasMaxLength(50).IsRequired();
            b.Property(s => s.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(s => s.Description).HasMaxLength(500);
            b.Property(s => s.IsActive).HasDefaultValue(true);
            b.Property(s => s.OwnerContact).HasMaxLength(200);
            b.Property(s => s.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<SystemClientPermission>(b =>
        {
            b.ToTable("SystemClientPermissions");
            b.HasKey(p => new { p.SystemKey, p.PermissionCode });
            b.Property(p => p.SystemKey).HasMaxLength(50).IsRequired();
            b.Property(p => p.PermissionCode).HasMaxLength(120).IsRequired();
            b.Property(p => p.GrantedAt).IsRequired();
            b.Property(p => p.GrantedBy).HasMaxLength(50);
            b.HasIndex(p => p.SystemKey);
        });

        modelBuilder.Entity<SystemCredential>(b =>
        {
            b.ToTable("SystemCredentials");
            b.HasKey(c => c.SystemKey);
            b.Property(c => c.SystemKey).HasMaxLength(50).IsRequired();
            b.Property(c => c.AuthScheme).HasMaxLength(30).IsRequired();
            b.Property(c => c.AuthConfig).HasColumnType("jsonb").IsRequired();
            b.Property(c => c.CallbackBaseUrl).HasMaxLength(500);
            b.Property(c => c.CallbackAuthScheme).HasMaxLength(30);
            b.Property(c => c.CallbackAuthConfig).HasColumnType("jsonb");
            b.Property(c => c.CallbackTimeoutMs).HasDefaultValue(10_000);
            b.Property(c => c.RetryMaxAttempts).HasDefaultValue(3);
            b.Property(c => c.CircuitFailureThreshold).HasDefaultValue(5);
            b.Property(c => c.CircuitDurationSeconds).HasDefaultValue(30);
            b.Property(c => c.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<SystemRequestLogEntry>(b =>
        {
            b.ToTable("SystemRequestLog");
            // Partition key + Id form the composite PK — Postgres
            // requires the partition key to participate in the PK on
            // RANGE-partitioned tables.
            b.HasKey(e => new { e.OccurredAt, e.Id });
            b.Property(e => e.Id).IsRequired();
            b.Property(e => e.OccurredAt).IsRequired();
            b.Property(e => e.SystemKey).HasMaxLength(50).IsRequired();
            b.Property(e => e.Method).HasMaxLength(10).IsRequired();
            b.Property(e => e.Path).HasMaxLength(500).IsRequired();
            b.Property(e => e.StatusCode).IsRequired();
            b.Property(e => e.IdempotencyKey).HasMaxLength(200);
            b.Property(e => e.CorrelationId).HasMaxLength(100);
        });
    }
}
