using Microsoft.EntityFrameworkCore;
using SMMS.Api.Models;

namespace SMMS.Api.Data;

public class SmmsDbContext(DbContextOptions<SmmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<SocietySettings> Settings => Set<SocietySettings>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<PaymentProof> PaymentProofs => Set<PaymentProof>();
    public DbSet<MaintenanceComponent> MaintenanceComponents => Set<MaintenanceComponent>();
    public DbSet<MaintenanceComponentRate> MaintenanceComponentRates => Set<MaintenanceComponentRate>();
    public DbSet<MaintenanceComponentFlatOverride> MaintenanceComponentFlatOverrides => Set<MaintenanceComponentFlatOverride>();
    public DbSet<CollectionLine> CollectionLines => Set<CollectionLine>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<AdvanceLedgerEntry> AdvanceLedger => Set<AdvanceLedgerEntry>();
    public DbSet<SocietyLiability> SocietyLiabilities => Set<SocietyLiability>();
    public DbSet<SocietyLiabilitySettlement> SocietyLiabilitySettlements => Set<SocietyLiabilitySettlement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<Collection>()
            .HasOne(c => c.Member)
            .WithMany(m => m.Collections)
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Complaint>()
            .HasOne(c => c.RaisedByUser)
            .WithMany()
            .HasForeignKey(c => c.RaisedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentProof>()
            .HasOne(p => p.Collection)
            .WithMany()
            .HasForeignKey(p => p.CollectionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentProof>()
            .HasOne(p => p.Member)
            .WithMany()
            .HasForeignKey(p => p.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentProof>()
            .HasIndex(p => new { p.Status, p.SubmittedAt });

        // Speeds up "does an invoice already exist for this member/month?" checks that the
        // generation service uses to stay idempotent (requirement: no duplicate invoices).
        modelBuilder.Entity<Collection>()
            .HasIndex(c => new { c.MemberId, c.Year, c.Month });

        // Discriminates the monthly maintenance invoice from ad-hoc one-time charges.
        modelBuilder.Entity<Collection>()
            .Property(c => c.CollectionType)
            .HasMaxLength(20)
            .HasDefaultValue("Monthly");

        // Enum stored as string for admin readability and stability across enum reordering.
        modelBuilder.Entity<MaintenanceComponent>()
            .Property(c => c.Method)
            .HasConversion<string>()
            .HasMaxLength(30);

        modelBuilder.Entity<MaintenanceComponent>()
            .Property(c => c.CategoryType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CollectionCategoryType.Recurring);

        modelBuilder.Entity<MaintenanceComponent>()
            .Property(c => c.Frequency)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(BillingFrequency.Monthly);

        modelBuilder.Entity<MaintenanceComponent>()
            .Property(c => c.LateFeeApplicable)
            .HasDefaultValue(true);

        modelBuilder.Entity<MaintenanceComponentRate>()
            .HasOne(r => r.Component)
            .WithMany(c => c.Rates)
            .HasForeignKey(r => r.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaintenanceComponentRate>()
            .HasIndex(r => new { r.ComponentId, r.Key })
            .IsUnique();

        modelBuilder.Entity<MaintenanceComponentFlatOverride>()
            .HasOne(o => o.Component)
            .WithMany(c => c.FlatOverrides)
            .HasForeignKey(o => o.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaintenanceComponentFlatOverride>()
            .HasOne(o => o.Member)
            .WithMany()
            .HasForeignKey(o => o.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MaintenanceComponentFlatOverride>()
            .HasIndex(o => new { o.ComponentId, o.MemberId })
            .IsUnique();

        modelBuilder.Entity<CollectionLine>()
            .HasOne(l => l.Collection)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Speeds up unmatched-transaction scans and UTR-based reconciliation lookups.
        modelBuilder.Entity<BankTransaction>()
            .HasIndex(t => new { t.Status, t.TxnDate });
        modelBuilder.Entity<BankTransaction>()
            .HasIndex(t => t.Reference);

        modelBuilder.Entity<AdvanceLedgerEntry>()
            .HasOne(e => e.Member)
            .WithMany()
            .HasForeignKey(e => e.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Speeds up per-member ledger reads (Member Ledger / Advance Deduction History reports).
        modelBuilder.Entity<AdvanceLedgerEntry>()
            .HasIndex(e => new { e.MemberId, e.Date });

        // Enums stored as strings for readability + stability across enum reordering.
        modelBuilder.Entity<SocietyLiability>()
            .Property(l => l.Source).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<SocietyLiability>()
            .Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<SocietyLiabilitySettlement>()
            .Property(s => s.Method).HasConversion<string>().HasMaxLength(30);

        modelBuilder.Entity<SocietyLiability>()
            .HasOne(l => l.Member)
            .WithMany()
            .HasForeignKey(l => l.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SocietyLiabilitySettlement>()
            .HasOne(s => s.Liability)
            .WithMany(l => l.Settlements)
            .HasForeignKey(s => s.LiabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SocietyLiabilitySettlement>()
            .HasOne(s => s.Expense)
            .WithMany()
            .HasForeignKey(s => s.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Speeds up the outstanding-liabilities dashboard/list scans.
        modelBuilder.Entity<SocietyLiability>()
            .HasIndex(l => new { l.Status, l.Date });

        // Soft delete: hide logically-deleted rows from every query automatically.
        modelBuilder.Entity<Expense>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Complaint>().HasQueryFilter(c => !c.IsDeleted);
        modelBuilder.Entity<SocietyLiability>().HasQueryFilter(l => !l.IsDeleted);
    }
}
