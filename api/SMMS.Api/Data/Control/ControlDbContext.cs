using Microsoft.EntityFrameworkCore;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Data.Control;

/// <summary>The platform control-plane database (SmmsControlDb). Independent of tenant
/// SmmsDbContext — uses a fixed connection string, never tenant-resolved.</summary>
public class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
{
    public DbSet<Society> Societies => Set<Society>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Society>()
            .HasIndex(s => s.Key)
            .IsUnique();

        modelBuilder.Entity<PlatformUser>()
            .HasIndex(u => u.Username)
            .IsUnique();
    }
}
