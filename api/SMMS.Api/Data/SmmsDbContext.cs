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
    }
}
