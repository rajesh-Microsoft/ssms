using Microsoft.AspNetCore.Identity;
using SMMS.Api.Models;

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

        // Seed a starter set of maintenance components so the rule engine produces invoices
        // out of the box. Admins can add/edit/remove these from the Maintenance tab.
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
                });
        }

        db.SaveChanges();
    }
}
