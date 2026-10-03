using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<UserEntity>();
        user.ToTable("users");
        user.HasKey(x => x.Id);
        user.Property(x => x.Name).HasMaxLength(120).IsRequired();
        user.Property(x => x.Username).HasMaxLength(100).IsRequired();
        user.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        user.Property(x => x.Email).HasMaxLength(180);
        user.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        user.Property(x => x.PreferredLocale).HasMaxLength(5).IsRequired();

        var role = modelBuilder.Entity<RoleEntity>();
        role.ToTable("roles");
        role.HasKey(x => x.Id);
        role.Property(x => x.Name).HasMaxLength(100).IsRequired();
        role.Property(x => x.Label).HasMaxLength(120).IsRequired();
        role.HasIndex(x => x.Name).IsUnique();

        var permission = modelBuilder.Entity<PermissionEntity>();
        permission.ToTable("permissions");
        permission.HasKey(x => x.Id);
        permission.Property(x => x.Name).HasMaxLength(120).IsRequired();
        permission.Property(x => x.Label).HasMaxLength(180).IsRequired();
        permission.HasIndex(x => x.Name).IsUnique();

        user.HasMany(x => x.Roles)
            .WithMany(x => x.Users)
            .UsingEntity("user_roles");

        role.HasMany(x => x.Permissions)
            .WithMany(x => x.Roles)
            .UsingEntity("role_permissions");

        var audit = modelBuilder.Entity<AuditLogEntity>();
        audit.ToTable("audit_logs");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Event).HasMaxLength(120).IsRequired();
        audit.HasIndex(x => x.CreatedAt);
        audit.HasIndex(x => x.ActorUserId);

        base.OnModelCreating(modelBuilder);
    }
}
