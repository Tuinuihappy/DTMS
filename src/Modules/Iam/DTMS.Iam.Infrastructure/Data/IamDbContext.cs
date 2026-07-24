using DTMS.Iam.Application.Security;
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

    // Phase S.3.1b — outbound callback subscriptions.
    public DbSet<SystemEventSubscription> SystemEventSubscriptions { get; set; } = null!;

    // Phase S.8c — audit + revocation backing store for admin-issued JWTs.
    public DbSet<SystemIssuedToken> SystemIssuedTokens { get; set; } = null!;

    // Encrypt-at-rest — protects CallbackAuthConfig via the value converter
    // below. Nullable + optional ctor so tests and ad-hoc harnesses can spin
    // the context up without DI; they then read/write plaintext, which the
    // protector's prefix discrimination keeps interoperable. CAVEAT: EF
    // caches the model per context type, so the FIRST instance in a process
    // decides whether the converter exists — never mix protector-less and
    // protector-ful instances in one process (DI always supplies it; the
    // singleton registration makes every instance share one protector).
    private readonly ICallbackTokenProtector? _callbackProtector;

    public IamDbContext(DbContextOptions<IamDbContext> options, ICallbackTokenProtector callbackProtector)
        : base(options)
        => _callbackProtector = callbackProtector;

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
            // Encrypt-at-rest — column is text (not jsonb) because the
            // stored value is Data Protection ciphertext ("CfDJ8…"), not
            // JSON. The converter encrypts on write / decrypts on read so
            // everything above this layer keeps seeing the plaintext JSON
            // config. EF never passes NULL through converters, so the
            // protector's null handling is belt-and-braces only.
            var callbackConfig = b.Property(c => c.CallbackAuthConfig).HasColumnType("text");
            // TokenRefreshConfig is also a JSON object stored as Data Protection
            // ciphertext, so it takes the same text column + converter as
            // CallbackAuthConfig. Same protector is safe: the value is JSON-shaped
            // ("{…}"), which the protector's prefix discriminator requires.
            var tokenRefreshConfig = b.Property(c => c.TokenRefreshConfig).HasColumnType("text");
            if (_callbackProtector is not null)
            {
                var protector = _callbackProtector;
                callbackConfig.HasConversion(
                    v => protector.Protect(v)!,
                    v => protector.TryUnprotect(v)!);
                tokenRefreshConfig.HasConversion(
                    v => protector.Protect(v)!,
                    v => protector.TryUnprotect(v)!);
            }
            b.Property(c => c.CallbackTimeoutMs).HasDefaultValue(10_000);
            b.Property(c => c.RetryMaxAttempts).HasDefaultValue(3);
            b.Property(c => c.CircuitFailureThreshold).HasDefaultValue(5);
            b.Property(c => c.CircuitDurationSeconds).HasDefaultValue(30);
            b.Property(c => c.UpdatedAt).IsRequired();
            // Optimistic concurrency via the Postgres xmin system column. The
            // detached AsNoTracking()+Update() path carries the loaded value into
            // the UPDATE's WHERE clause, so a concurrent write bumps xmin and this
            // save throws DbUpdateConcurrencyException instead of lost-updating.
            b.Property(c => c.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<SystemEventSubscription>(b =>
        {
            b.ToTable("SystemEventSubscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.SystemKey).HasMaxLength(50).IsRequired();
            b.Property(s => s.EventType).HasMaxLength(128).IsRequired();
            b.Property(s => s.PayloadFormatKey).HasMaxLength(64).IsRequired();
            b.Property(s => s.Enabled).IsRequired().HasDefaultValue(true);
            b.Property(s => s.CreatedAtUtc).IsRequired();
            b.Property(s => s.UpdatedAtUtc).IsRequired();
            b.HasIndex(s => new { s.SystemKey, s.EventType }).IsUnique();
        });

        modelBuilder.Entity<SystemIssuedToken>(b =>
        {
            b.ToTable("SystemIssuedTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.SystemKey).HasMaxLength(50).IsRequired();
            b.Property(t => t.Jti).HasMaxLength(64).IsRequired();
            b.Property(t => t.IssuedAt).IsRequired();
            // Nullable: perpetual tokens (Phase S.8d) carry no exp.
            b.Property(t => t.ExpiresAt);
            b.Property(t => t.IssuedBy).HasMaxLength(50).IsRequired();
            b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(t => t.RevokedBy).HasMaxLength(50);
            b.Property(t => t.RevokeReason).HasMaxLength(300);
            // Jti is globally unique per mint — enforce so a duplicated
            // insert (retry, race) surfaces loud instead of quietly
            // producing 2 rows sharing an id.
            b.HasIndex(t => t.Jti).IsUnique();
            // Admin UI lists tokens per system newest-first.
            b.HasIndex(t => new { t.SystemKey, t.IssuedAt });
            b.HasOne<SystemClient>()
                .WithMany()
                .HasForeignKey(t => t.SystemKey)
                .HasPrincipalKey(s => s.Key)
                .OnDelete(DeleteBehavior.Cascade);
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
