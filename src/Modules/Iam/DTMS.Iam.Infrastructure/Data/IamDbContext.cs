using DTMS.Iam.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Data;

public class IamDbContext : DbContext
{
    public const string Schema = "iam";

    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;

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
    }
}
