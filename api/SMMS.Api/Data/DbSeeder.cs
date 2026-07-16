using Microsoft.AspNetCore.Identity;
using SMMS.Api.Models;

namespace SMMS.Api.Data;

public static class DbSeeder
{
    /// <summary>Seeds a default Admin account and default settings row if the database is empty.</summary>
    public static void Seed(SmmsDbContext db)
    {
        if (!db.Settings.Any())
        {
            db.Settings.Add(new SocietySettings());
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

        db.SaveChanges();
    }
}
