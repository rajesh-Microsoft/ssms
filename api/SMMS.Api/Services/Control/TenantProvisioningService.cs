using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Models;
using SMMS.Api.Models.Control;

namespace SMMS.Api.Services.Control;

/// <summary>Provisions a new society (tenant) at runtime: inserts the control-plane record, makes it
/// resolvable, then creates + migrates + seeds its dedicated tenant database.</summary>
public partial class TenantProvisioningService(
    ControlDbContext controlDb,
    DbTenantStore tenantStore,
    IServiceScopeFactory scopeFactory)
{
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugRegex();

    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "www", "api"
    };

    public async Task<Society> ProvisionAsync(
        string key,
        string displayName,
        string? plan,
        string? adminUsername,
        string? adminPassword,
        int flatCount,
        DateTime? expiryDate)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();

        if (!SlugRegex().IsMatch(key))
            throw new InvalidOperationException("Society key must be a lowercase URL-safe slug (letters, digits, hyphens).");

        if (ReservedKeys.Contains(key))
            throw new InvalidOperationException($"'{key}' is a reserved key and cannot be used.");

        if (await controlDb.Societies.AnyAsync(s => s.Key == key))
            throw new InvalidOperationException("A society with this key already exists.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("Display name is required.");

        var dbName = "SmmsDb_" + char.ToUpperInvariant(key[0]) + key[1..];

        var society = new Society
        {
            Key = key,
            DisplayName = displayName.Trim(),
            DbName = dbName,
            Status = SocietyStatus.Active,
            Plan = string.IsNullOrWhiteSpace(plan) ? null : plan.Trim(),
            FlatCount = flatCount,
            ExpiryDate = expiryDate
        };

        controlDb.Societies.Add(society);
        await controlDb.SaveChangesAsync();

        // Make the new society resolvable by the tenant middleware.
        tenantStore.Reload();

        var tenant = tenantStore.GetByKey(key)
            ?? throw new InvalidOperationException("Provisioned society could not be resolved after reload.");

        // Create + migrate + seed the dedicated tenant database in its own scope.
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        var tenantDb = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();

        await tenantDb.Database.MigrateAsync();
        DbSeeder.Seed(tenantDb, society.DisplayName);

        // Apply custom admin credentials if supplied by the onboarding wizard.
        if (!string.IsNullOrWhiteSpace(adminUsername) || !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = await tenantDb.Users.FirstOrDefaultAsync(u => u.Role == "Admin");
            if (admin is not null)
            {
                if (!string.IsNullOrWhiteSpace(adminUsername))
                    admin.Username = adminUsername.Trim();
                if (!string.IsNullOrWhiteSpace(adminPassword))
                    admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, adminPassword);
                await tenantDb.SaveChangesAsync();
            }
        }

        return society;
    }
}
