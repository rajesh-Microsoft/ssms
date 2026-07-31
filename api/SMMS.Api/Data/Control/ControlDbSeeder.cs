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

        // 3. Default subscription plans (only if the catalogue is empty).
        if (!db.SubscriptionPlans.Any())
        {
            db.SubscriptionPlans.AddRange(
                new SubscriptionPlan { Code = "TRIAL", Name = "Trial", Price = 0m, BillingPeriodMonths = 1, Currency = "INR", IsActive = true },
                new SubscriptionPlan { Code = "STANDARD", Name = "Standard", Price = 4999m, BillingPeriodMonths = 12, Currency = "INR", IsActive = true },
                new SubscriptionPlan { Code = "PREMIUM", Name = "Premium", Price = 9999m, BillingPeriodMonths = 12, Currency = "INR", IsActive = true });
        }

        // 4. Default platform settings (only if none set yet).
        if (!db.PlatformSettings.Any())
        {
            db.PlatformSettings.AddRange(
                new PlatformSetting { Key = PlatformSettingKeys.BrandName, Value = "YuvaanSoft" },
                new PlatformSetting { Key = PlatformSettingKeys.DefaultPlanCode, Value = "STANDARD" },
                new PlatformSetting { Key = PlatformSettingKeys.ExpiryWarningDays, Value = "15" },
                new PlatformSetting { Key = PlatformSettingKeys.SmtpEnableSsl, Value = "true" });
        }

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
