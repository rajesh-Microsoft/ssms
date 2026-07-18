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
    }
}
