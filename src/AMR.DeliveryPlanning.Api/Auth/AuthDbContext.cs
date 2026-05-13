using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.Auth;

public class AuthDbContext : DbContext
{
    public const string Schema = "auth";

    public DbSet<AppUser> Users { get; set; } = null!;

    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AppUser>(builder =>
        {
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Username).HasMaxLength(50);
            builder.HasIndex(u => u.Username).IsUnique();
            builder.Property(u => u.PasswordHash).HasMaxLength(100);
            builder.Property(u => u.Role).HasMaxLength(30);
        });
    }
}
