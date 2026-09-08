using Microsoft.AspNetCore.Identity;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Data;

public static class DbSeeder
{
    /// <summary>Seeds a default Admin account and default settings row if the database is empty.</summary>
    public static void Seed(SmmsDbContext db, string? societyName = null)
    {
        if (!db.Settings.Any())
        {
            db.Settings.Add(new SocietySettings
            {
                SocietyName = societyName ?? "Our Society"
            });
        }

        if (!db.Users.Any())
        {
            var hasher = new PasswordHasher<User>();
            var admin = new User
            {
                Username = "Admin",
                Role = "Admin",
                Status = "Active",
                Email = "admin@society.com"
            };
            admin.PasswordHash = hasher.HashPassword(admin, "admin123");
            db.Users.Add(admin);
        }

        // Seed a starter set of recurring collection categories so the rule engine produces invoices
        // out of the box. Core charges are active; optional ones are seeded inactive for the admin
        // to configure and enable from the Collections tab.
        if (!db.MaintenanceComponents.Any())
        {
            db.MaintenanceComponents.AddRange(
                new MaintenanceComponent
                {
                    Name = "Maintenance Charges",
                    Method = CalculationMethod.FixedAmount,
                    Amount = 2000,
                    SortOrder = 10
                },
                new MaintenanceComponent
                {
                    Name = "Sinking Fund",
                    Method = CalculationMethod.FixedAmount,
                    Amount = 200,
                    SortOrder = 20
                },
                new MaintenanceComponent
                {
                    Name = "Parking Charges",
                    Method = CalculationMethod.CustomPerFlat,
                    Amount = 300,
                    ApplyToAllFlats = false,
                    SortOrder = 30
                },
                new MaintenanceComponent { Name = "Water Charges", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 40 },
                new MaintenanceComponent { Name = "Club House Fee", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 50 },
                new MaintenanceComponent { Name = "Lift Maintenance", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 60 },
                new MaintenanceComponent { Name = "Security Charges", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 70 },
                new MaintenanceComponent { Name = "Generator Charges", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 80 },
                new MaintenanceComponent { Name = "Common Electricity", Method = CalculationMethod.FixedAmount, Amount = 0, IsActive = false, SortOrder = 90 });
        }

        if (!db.UtilityProviders.Any(p => p.Code == "TGSPDCL"))
        {
            db.UtilityProviders.Add(new UtilityProvider
            {
                Code = "TGSPDCL",
                ProviderName = "TGSPDCL",
                Category = "Electricity",
                SupportsAutoFetch = true
            });
        }

        if (!db.UtilityProviders.Any(p => p.Code == "HMWSSB"))
        {
            db.UtilityProviders.Add(new UtilityProvider
            {
                Code = "HMWSSB",
                ProviderName = "HMWSSB",
                Category = "Water",
                SupportsAutoFetch = true
            });
        }

        SeedSystemGroups(db);

        db.SaveChanges();
    }

    /// <summary>
    /// Adds any built-in position that is missing. Runs on every start, including for societies
    /// that already have data, which is how existing databases pick these up. An existing group is
    /// never rewritten: a society that has retuned its Treasurer must keep those changes.
    /// </summary>
    private static void SeedSystemGroups(SmmsDbContext db)
    {
        // "View" is the platform default for anything unlisted, so each definition only needs to
        // name the modules that differ from plain read access.
        var defaults = new (string Name, string Description, Dictionary<string, string> Permissions)[]
        {
            // Occupancy baselines. Membership is not stored: a user inherits whichever of these
            // matches their OccupancyType, so the field stays the single source of truth.
            (UserGroups.Tenant, "Rents a flat. Pays maintenance and raises complaints; the society's accounts are not theirs to see.", new()
            {
                [PermissionModules.Collections] = "None",
                [PermissionModules.Expenses] = "None",
                [PermissionModules.Income] = "None",
                [PermissionModules.Liabilities] = "None",
                [PermissionModules.Budgets] = "None",
                [PermissionModules.Inventory] = "None",
                [PermissionModules.Members] = "None",
                [PermissionModules.Settings] = "None",
                [PermissionModules.Complaints] = "View"
            }),
            (UserGroups.Owner, "Owns a flat. Entitled to see how the society's money is collected and spent, without running it.", new()
            {
                [PermissionModules.Collections] = "View",
                [PermissionModules.Expenses] = "View",
                [PermissionModules.Income] = "View",
                [PermissionModules.Liabilities] = "View",
                [PermissionModules.Budgets] = "View",
                [PermissionModules.Inventory] = "View",
                [PermissionModules.Members] = "View",
                [PermissionModules.Complaints] = "View",
                [PermissionModules.Settings] = "None"
            }),
            ("Treasurer", "Handles money: expenses, liabilities, reimbursements and the budget.", new()
            {
                [PermissionModules.Expenses] = "Edit",
                [PermissionModules.Liabilities] = "Edit",
                [PermissionModules.Budgets] = "Edit",
                [PermissionModules.Income] = "Edit",
                [PermissionModules.Collections] = "Edit",
                [PermissionModules.Settings] = "None"
            }),
            ("Secretary", "Runs day-to-day administration: members, complaints and inventory.", new()
            {
                [PermissionModules.Members] = "Edit",
                [PermissionModules.Complaints] = "Edit",
                [PermissionModules.Inventory] = "Edit",
                [PermissionModules.Settings] = "None"
            }),
            ("Chairman", "Oversight across the society, with approval of contributions.", new()
            {
                [PermissionModules.Complaints] = "Edit",
                [PermissionModules.Liabilities] = "Edit",
                [PermissionModules.Settings] = "None"
            }),
            ("Committee Member", "Read access across the society's records.", new()
            {
                [PermissionModules.Settings] = "None"
            })
        };

        foreach (var (name, description, permissions) in defaults)
        {
            if (db.UserGroups.Any(g => g.Name == name)) continue;
            db.UserGroups.Add(new UserGroup
            {
                Name = name,
                Description = description,
                IsSystem = true,
                IsActive = true,
                Permissions = PermissionHelper.Serialize(permissions)
            });
        }
    }
}
