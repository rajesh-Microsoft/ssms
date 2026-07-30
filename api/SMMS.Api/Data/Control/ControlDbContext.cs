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
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlatformInvoice> PlatformInvoices => Set<PlatformInvoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Society>()
            .HasIndex(s => s.Key)
            .IsUnique();

        modelBuilder.Entity<PlatformUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<SubscriptionPlan>()
            .HasIndex(p => p.Code)
            .IsUnique();

        modelBuilder.Entity<SubscriptionPlan>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PlatformInvoice>()
            .HasIndex(i => i.InvoiceNumber)
            .IsUnique();

        modelBuilder.Entity<PlatformInvoice>()
            .Property(i => i.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PlatformInvoice>()
            .HasOne(i => i.Society)
            .WithMany()
            .HasForeignKey(i => i.SocietyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PlatformInvoice>()
            .HasOne(i => i.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(i => i.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
