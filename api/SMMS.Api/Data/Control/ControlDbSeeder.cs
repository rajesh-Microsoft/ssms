using Microsoft.AspNetCore.Identity;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Data.Control;

public static class ControlDbSeeder
{
    private static readonly PasswordHasher<PlatformUser> Hasher = new();

    public static void Seed(ControlDbContext db, IConfiguration config)
    {
        // 1. First super admin (credentials from ControlPlane:SuperAdmin).
        var suUsername = config["ControlPlane:SuperAdmin:Username"];
        var suPassword = config["ControlPlane:SuperAdmin:Password"];

        if (!string.IsNullOrWhiteSpace(suUsername) && !string.IsNullOrWhiteSpace(suPassword)
            && !db.PlatformUsers.Any(u => u.Username == suUsername))
        {
            var su = new PlatformUser
            {
                Username = suUsername,
                DisplayName = config["ControlPlane:SuperAdmin:DisplayName"] ?? suUsername,
                IsActive = true
            };
            su.PasswordHash = Hasher.HashPassword(su, suPassword);
            db.PlatformUsers.Add(su);
        }

        // 2. Back-fill existing societies (source-of-truth migration from config → control DB).
        SeedSociety(db, "aadya", "NLC Aadya", "SmmsDb_Aadya");
        SeedSociety(db, "aaradya", "NLC Aaradya", "SmmsDb_Aaradya");

        db.SaveChanges();
    }

    private static void SeedSociety(ControlDbContext db, string key, string displayName, string dbName)
    {
        if (db.Societies.Any(s => s.Key == key)) return;

        db.Societies.Add(new Society
        {
            Key = key,
            DisplayName = displayName,
            DbName = dbName,
            Status = SocietyStatus.Active,
            Plan = "Standard"
        });
    }
}
