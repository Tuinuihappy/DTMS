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
    }
}
