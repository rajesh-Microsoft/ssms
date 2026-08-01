using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Models;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Control;

/// <summary>Result summary returned to the console after a generate/reset run.</summary>
public record DemoDataResult(
    string Key,
    string Preset,
    int Members,
    int Users,
    int Collections,
    int Expenses,
    int Complaints,
    int PaymentProofs);

/// <summary>Populates a <b>demo (sandbox)</b> tenant with a rich, realistic dataset so the product
/// can be demonstrated end-to-end. Every mutating operation is hard-guarded on
/// <see cref="Society.IsDemo"/> — it will refuse to touch any real customer tenant.</summary>
public class DemoDataService(
    ControlDbContext controlDb,
    DbTenantStore tenantStore,
    IServiceScopeFactory scopeFactory)
{
    public static readonly IReadOnlyDictionary<string, int> Presets =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["small"] = 40,
            ["medium"] = 120,
            ["large"] = 400,
        };

    private const int MonthsOfHistory = 8;

    /// <summary>Wipes and regenerates the demo tenant's data for the given preset.</summary>
    public async Task<DemoDataResult> GenerateAsync(string key, string preset)
    {
        var (society, flatCount, normalizedPreset) = await ResolveDemoAsync(key, preset);
        using var scope = CreateTenantScope(society);
        var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
        await db.Database.MigrateAsync();

        await WipeAsync(db);

        var rng = new Random(HashSeed(society.Key, normalizedPreset));
        var now = DateTime.UtcNow;

        // ── Members + resident user accounts ──
        var members = BuildMembers(rng, flatCount);
        db.Members.AddRange(members);
        await db.SaveChangesAsync();

        var hasher = new PasswordHasher<User>();
        var users = new List<User>();
        foreach (var m in members.Where(m => m.Status == "Active"))
        {
            var user = new User
            {
                Username = "res-" + m.Flat.Replace("-", "").ToLowerInvariant(),
                Role = "Member",
                Status = "Active",
                Name = m.Name,
                Flat = m.Flat,
                Floor = m.Floor,
                Email = m.Email,
                Mobile = m.Mobile,
                OccupancyType = rng.Next(100) < 62 ? "Owner" : "Tenant",
                CreatedAt = now.AddDays(-rng.Next(30, 400)),
            };
            user.PasswordHash = hasher.HashPassword(user, "demo123");
            users.Add(user);
        }
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var userByFlat = users.ToDictionary(u => u.Flat!, u => u, StringComparer.OrdinalIgnoreCase);

        // ── Collections (invoices) with per-component line breakdown ──
        int invoiceSeq = 1;
        var collections = new List<Collection>();
        var proofs = new List<PaymentProof>();
        var ledger = new List<AdvanceLedgerEntry>();

        foreach (var m in members.Where(m => m.Status == "Active"))
        {
            bool hasParking = rng.Next(100) < 70;
            decimal maintenance = 2000m;
            decimal sinking = 200m;
            decimal parking = hasParking ? 300m : 0m;
            decimal amount = maintenance + sinking + parking;

            for (int back = MonthsOfHistory - 1; back >= 0; back--)
            {
                var period = new DateTime(now.Year, now.Month, 1).AddMonths(-back);
                bool current = back == 0;

                var col = new Collection
                {
                    MemberId = m.Id,
                    Amount = amount,
                    Month = period.Month,
                    Year = period.Year,
                    CollectionType = "Monthly",
                    InvoiceNumber = $"INV{invoiceSeq++:D6}",
                    DueDate = new DateTime(period.Year, period.Month, 10),
                    Lines = new List<CollectionLine>
                    {
                        new() { ComponentName = "Maintenance Charges", Method = "FixedAmount", Amount = maintenance },
                        new() { ComponentName = "Sinking Fund", Method = "FixedAmount", Amount = sinking },
                    },
                };
                if (hasParking)
                    col.Lines.Add(new CollectionLine { ComponentName = "Parking Charges", Method = "CustomPerFlat", Amount = parking });

                // Status distribution — older months are mostly settled; the current month is mostly open.
                int roll = rng.Next(100);
                if (current)
                {
                    if (roll < 35) MarkPaid(col, rng, period);
                    else if (roll < 55) MarkPartial(col, rng, period);
                    else col.Status = "Unpaid";
                }
                else
                {
                    if (roll < 76) MarkPaid(col, rng, period);
                    else if (roll < 86) MarkPartial(col, rng, period);
                    else { col.Status = "Overdue"; col.LateFeeApplied = rng.Next(100) < 50; }
                }

                collections.Add(col);
            }
        }
        db.Collections.AddRange(collections);
        await db.SaveChangesAsync();

        // ── Payment proofs (approved / pending / rejected) against recent invoices ──
        var recentSettleable = collections
            .Where(c => c.Status is "Paid" or "Partial" or "Unpaid")
            .Where(c => userByFlat.Count > 0)
            .OrderByDescending(c => c.Year * 100 + c.Month)
            .Take(Math.Max(20, collections.Count / 6))
            .ToList();

        var memberById = members.ToDictionary(m => m.Id);
        foreach (var col in recentSettleable)
        {
            if (rng.Next(100) < 55) continue;
            var mem = memberById[col.MemberId];
            if (!userByFlat.TryGetValue(mem.Flat, out var u)) continue;

            int roll = rng.Next(100);
            string status = roll < 65 ? "Approved" : roll < 88 ? "Pending" : "Rejected";
            var submitted = new DateTime(col.Year, col.Month, 1).AddDays(rng.Next(5, 25));
            proofs.Add(new PaymentProof
            {
                CollectionId = col.Id,
                MemberId = col.MemberId,
                SubmittedByUserId = u.Id,
                Amount = col.Amount,
                UpiReference = $"UPI{rng.Next(100000000, 999999999)}",
                FileName = "payment-screenshot.jpg",
                ContentType = "image/jpeg",
                Status = status,
                SubmittedAt = submitted,
                ReviewedAt = status == "Pending" ? null : submitted.AddDays(rng.Next(1, 4)),
                ReviewRemarks = status == "Rejected" ? "Amount does not match the invoice." : null,
            });
        }
        db.PaymentProofs.AddRange(proofs);

        // ── A few members carry a small advance/wallet balance ──
        foreach (var m in members.Where(m => m.Status == "Active"))
        {
            if (rng.Next(100) >= 12) continue;
            decimal bal = rng.Next(1, 7) * 500m;
            m.AdvanceBalance = bal;
            ledger.Add(new AdvanceLedgerEntry
            {
                MemberId = m.Id,
                Type = "Credit",
                Amount = bal,
                BalanceAfter = bal,
                Source = "Payment",
                Date = now.AddDays(-rng.Next(5, 90)),
                Note = "Excess payment carried forward.",
            });
        }
        db.AdvanceLedger.AddRange(ledger);

        // ── Expenses ──
        var expenses = BuildExpenses(rng, ExpenseCountFor(flatCount), now);
        db.Expenses.AddRange(expenses);

        // ── Complaints ──
        var complaints = BuildComplaints(rng, ComplaintCountFor(flatCount), users, now);
        db.Complaints.AddRange(complaints);

        // ── Audit trail ──
        db.AuditLog.AddRange(BuildAuditLog(rng, now));

        await db.SaveChangesAsync();

        // Keep the control-plane flat count in sync with what we generated.
        society.FlatCount = flatCount;
        await controlDb.SaveChangesAsync();

        return new DemoDataResult(
            society.Key, normalizedPreset, members.Count, users.Count,
            collections.Count, expenses.Count, complaints.Count, proofs.Count);
    }

    /// <summary>Wipes all resident-facing data from the demo tenant, leaving the admin account,
    /// settings and maintenance component configuration intact.</summary>
    public async Task<DemoDataResult> ResetAsync(string key)
    {
        var (society, _, _) = await ResolveDemoAsync(key, "small");
        using var scope = CreateTenantScope(society);
        var db = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();
        await db.Database.MigrateAsync();
        await WipeAsync(db);
        return new DemoDataResult(society.Key, "reset", 0, 0, 0, 0, 0, 0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Guard + tenant resolution
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(Society Society, int FlatCount, string Preset)> ResolveDemoAsync(string key, string preset)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();
        var society = await controlDb.Societies.FirstOrDefaultAsync(s => s.Key == key)
            ?? throw new InvalidOperationException("Society not found.");

        if (!society.IsDemo)
            throw new InvalidOperationException(
                "This tool only runs against a demo (sandbox) society. Refusing to touch a real tenant.");

        var normalized = (preset ?? "small").Trim().ToLowerInvariant();
        if (!Presets.TryGetValue(normalized, out var flatCount))
            throw new InvalidOperationException("Preset must be one of: small, medium, large.");

        return (society, flatCount, normalized);
    }

    private IServiceScope CreateTenantScope(Society society)
    {
        tenantStore.Reload();
        var tenant = tenantStore.GetByKey(society.Key)
            ?? throw new InvalidOperationException("Demo tenant could not be resolved.");

        var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        return scope;
    }

    private async Task WipeAsync(SmmsDbContext db)
    {
        // FK-safe order: proofs + lines + ledger first, then collections, then members, then
        // resident users. Admin user, settings and maintenance components are preserved.
        await db.PaymentProofs.ExecuteDeleteAsync();
        await db.CollectionLines.ExecuteDeleteAsync();
        await db.AdvanceLedger.ExecuteDeleteAsync();
        await db.MaintenanceComponentFlatOverrides.ExecuteDeleteAsync();
        await db.Collections.ExecuteDeleteAsync();
        await db.Complaints.ExecuteDeleteAsync();
        await db.Members.ExecuteDeleteAsync();
        await db.Users.Where(u => u.Role != "Admin").ExecuteDeleteAsync();
        await db.Expenses.ExecuteDeleteAsync();
        await db.AuditLog.ExecuteDeleteAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Data builders
    // ──────────────────────────────────────────────────────────────────────────

    private static void MarkPaid(Collection col, Random rng, DateTime period)
    {
        col.Status = "Paid";
        col.AmountPaid = col.Amount;
        col.PaymentDate = new DateTime(period.Year, period.Month, 1).AddDays(rng.Next(2, 26));
        col.PaymentMode = PaymentModes[rng.Next(PaymentModes.Length)];
    }

    private static void MarkPartial(Collection col, Random rng, DateTime period)
    {
        col.Status = "Partial";
        col.AmountPaid = Math.Round(col.Amount * (rng.Next(30, 70) / 100m), 2);
        col.PaymentDate = new DateTime(period.Year, period.Month, 1).AddDays(rng.Next(2, 26));
        col.PaymentMode = PaymentModes[rng.Next(PaymentModes.Length)];
    }

    private static List<Member> BuildMembers(Random rng, int flatCount)
    {
        var members = new List<Member>(flatCount);
        int perFloor = 4;
        int floorsPerTower = 10;
        int flatsPerTower = perFloor * floorsPerTower;
        int made = 0;
        char tower = 'A';

        while (made < flatCount)
        {
            for (int floor = 1; floor <= floorsPerTower && made < flatCount; floor++)
            {
                for (int unit = 1; unit <= perFloor && made < flatCount; unit++)
                {
                    made++;
                    string flat = $"{tower}-{floor}{unit:D2}";
                    bool vacant = rng.Next(100) < 12;

                    decimal area = 450 + rng.Next(0, 24) * 50; // 450–1600
                    string flatType = area < 650 ? "1BHK" : area < 1050 ? "2BHK" : "3BHK";

                    var (name, mobile, email) = RandomIdentity(rng, flat);
                    members.Add(new Member
                    {
                        Name = vacant ? name + " (Vacant)" : name,
                        Flat = flat,
                        Floor = floor.ToString(),
                        Tower = $"Tower {tower}",
                        AreaSqFt = area,
                        FlatType = flatType,
                        Mobile = mobile,
                        Email = email,
                        Status = vacant ? "Inactive" : "Active",
                    });
                }
            }
            tower++;
        }
        return members;
    }

    private static (string Name, string Mobile, string Email) RandomIdentity(Random rng, string flat)
    {
        string first = FirstNames[rng.Next(FirstNames.Length)];
        string last = LastNames[rng.Next(LastNames.Length)];
        string name = $"{first} {last}";
        string mobile = "9" + rng.Next(100000000, 999999999).ToString();
        string email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{rng.Next(1, 99)}@example.com";
        return (name, mobile, email);
    }

    private static int ExpenseCountFor(int flats) => flats <= 40 ? 60 : flats <= 120 ? 120 : 200;
    private static int ComplaintCountFor(int flats) => flats <= 40 ? 25 : flats <= 120 ? 55 : 95;

    private static List<Expense> BuildExpenses(Random rng, int count, DateTime now)
    {
        var list = new List<Expense>(count);
        for (int i = 0; i < count; i++)
        {
            int back = rng.Next(0, MonthsOfHistory);
            var period = new DateTime(now.Year, now.Month, 1).AddMonths(-back);
            var cat = ExpenseCategories[rng.Next(ExpenseCategories.Length)];
            var (loAmt, hiAmt, desc, vendors) = cat;
            list.Add(new Expense
            {
                ExpenseDate = new DateTime(period.Year, period.Month, 1).AddDays(rng.Next(0, 27)),
                Category = desc,
                Description = desc + " — " + MonthName(period.Month) + " " + period.Year,
                Vendor = vendors[rng.Next(vendors.Length)],
                Amount = rng.Next(loAmt, hiAmt),
                PaymentMode = PaymentModes[rng.Next(PaymentModes.Length)],
                Month = period.Month,
                Year = period.Year,
            });
        }
        return list;
    }

    private static List<Complaint> BuildComplaints(Random rng, int count, List<User> users, DateTime now)
    {
        var list = new List<Complaint>(count);
        if (users.Count == 0) return list;

        for (int i = 0; i < count; i++)
        {
            var u = users[rng.Next(users.Count)];
            var (category, subjects) = ComplaintTemplates[rng.Next(ComplaintTemplates.Length)];
            var created = now.AddDays(-rng.Next(0, MonthsOfHistory * 30));

            int roll = rng.Next(100);
            string status = roll < 30 ? "Open" : roll < 50 ? "In Progress" : roll < 80 ? "Resolved" : "Closed";
            bool done = status is "Resolved" or "Closed";

            list.Add(new Complaint
            {
                Subject = subjects[rng.Next(subjects.Length)],
                Description = "Reported from flat " + u.Flat + ". " + subjects[rng.Next(subjects.Length)],
                Category = category,
                Priority = Priorities[rng.Next(Priorities.Length)],
                Status = status,
                RaisedByUserId = u.Id,
                Flat = u.Flat,
                Floor = u.Floor,
                CreatedAt = created,
                ResolvedAt = done ? created.AddDays(rng.Next(1, 12)) : null,
                ResolutionNotes = done ? "Issue attended to by the maintenance team." : null,
                AssignedTo = rng.Next(100) < 60 ? Staff[rng.Next(Staff.Length)] : null,
            });
        }
        return list;
    }

    private static List<AuditLogEntry> BuildAuditLog(Random rng, DateTime now)
    {
        var entries = new List<AuditLogEntry>();
        (string module, string action, string detail)[] rows =
        {
            ("Auth", "Login", "Admin signed in."),
            ("Billing", "GenerateInvoices", "Generated monthly maintenance invoices."),
            ("Members", "Create", "Added a new member."),
            ("Expenses", "Create", "Recorded a new expense."),
            ("Complaints", "Update", "Updated a complaint status."),
            ("Settings", "Update", "Updated society settings."),
            ("Payments", "Approve", "Approved a payment proof."),
        };
        foreach (var (module, action, detail) in rows)
            entries.Add(new AuditLogEntry
            {
                User = "Admin",
                Module = module,
                Action = action,
                Details = detail,
                Timestamp = now.AddDays(-rng.Next(0, 60)),
            });
        return entries;
    }

    private static int HashSeed(string key, string preset)
        => Math.Abs((key + ":" + preset).GetHashCode());

    private static string MonthName(int m) =>
        System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m);

    // ── Static content pools ──
    private static readonly string[] PaymentModes = { "UPI", "NEFT", "Cash", "Cheque", "Card" };
    private static readonly string[] Priorities = { "Low", "Medium", "Medium", "High" };
    private static readonly string[] Staff = { "Ramesh (Supervisor)", "Suresh (Electrician)", "Facility Desk", "Plumber on-call" };

    private static readonly string[] FirstNames =
    {
        "Arjun", "Priya", "Rahul", "Sneha", "Vikram", "Anjali", "Karthik", "Divya", "Sanjay", "Meera",
        "Rohit", "Kavya", "Amit", "Pooja", "Nikhil", "Shreya", "Rajesh", "Neha", "Suresh", "Deepa",
        "Manoj", "Aishwarya", "Vivek", "Lakshmi", "Aditya", "Swati", "Prakash", "Ritu", "Harish", "Ananya",
    };

    private static readonly string[] LastNames =
    {
        "Sharma", "Reddy", "Iyer", "Nair", "Gupta", "Patel", "Rao", "Menon", "Verma", "Shetty",
        "Kulkarni", "Desai", "Joshi", "Pillai", "Bhat", "Hegde", "Naidu", "Chauhan", "Malhotra", "Kapoor",
    };

    private static readonly (string Category, string[] Subjects)[] ComplaintTemplates =
    {
        ("Plumbing", new[] { "Water leakage in bathroom", "Low water pressure", "Blocked drainage", "Overflowing tank" }),
        ("Electrical", new[] { "Power fluctuation on floor", "Common area light not working", "Frequent tripping", "DG backup delay" }),
        ("Security", new[] { "Visitor entry not logged", "Gate not manned at night", "CCTV camera offline", "Intercom not working" }),
        ("Housekeeping", new[] { "Corridor not cleaned", "Garbage not collected", "Staircase dirty", "Lift lobby unclean" }),
        ("Parking", new[] { "Someone parked in my slot", "Visitor parking full", "Two-wheeler blocking exit", "Parking line faded" }),
        ("Noise", new[] { "Loud music late night", "Construction noise", "Dog barking", "Party disturbance" }),
        ("Lift", new[] { "Lift stuck between floors", "Lift making noise", "Lift out of service", "Lift button not working" }),
        ("Water", new[] { "No water supply", "Tanker not arrived", "Muddy water", "Water meter fault" }),
    };

    private static readonly (int Lo, int Hi, string Name, string[] Vendors)[] ExpenseCategories =
    {
        (18000, 45000, "Housekeeping", new[] { "CleanPro Services", "Sparkle Facility", "GreenClean" }),
        (25000, 60000, "Security", new[] { "SecureGuard", "Sentinel Security", "SafeShield" }),
        (8000, 40000, "Electricity", new[] { "State Electricity Board", "PowerGrid" }),
        (6000, 25000, "Water Charges", new[] { "City Water Board", "AquaTankers" }),
        (5000, 35000, "Repairs & Maintenance", new[] { "FixIt Services", "HandyPro", "BuildCare" }),
        (4000, 15000, "Gardening", new[] { "GreenThumb", "Bloom Landscapes" }),
        (9000, 22000, "Lift AMC", new[] { "OTIS", "Johnson Lifts", "Kone" }),
        (3000, 12000, "Common Area", new[] { "Local Supplier", "Hardware Mart" }),
        (5000, 30000, "Festival & Events", new[] { "EventCraft", "Decor Hub" }),
        (4000, 20000, "Legal & Professional", new[] { "AuditCo", "Legal Associates" }),
        (10000, 50000, "Insurance", new[] { "National Insurance", "HDFC Ergo" }),
        (3000, 18000, "Plumbing", new[] { "AquaFix", "PipeCare" }),
    };
}
