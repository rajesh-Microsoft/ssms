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
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

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

        modelBuilder.Entity<SupportTicket>()
            .HasIndex(t => t.TicketNumber)
            .IsUnique();

        modelBuilder.Entity<SupportTicket>()
            .HasOne(t => t.Society)
            .WithMany()
            .HasForeignKey(t => t.SocietyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupportTicketMessage>()
            .HasOne(m => m.Ticket)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlatformSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}
