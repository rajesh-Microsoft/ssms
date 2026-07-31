using System.Security.Cryptography;
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
        "admin", "www", "api", "ssms"
    };

    /// <summary>Direct super-admin onboarding: creates an Active society AND its database in one step.</summary>
    public async Task<Society> ProvisionAsync(
        string key,
        string displayName,
        string? plan,
        string? adminUsername,
        string? adminPassword,
        int flatCount,
        DateTime? expiryDate)
    {
        var society = await CreateSocietyRecordAsync(
            key, displayName, plan, flatCount, SocietyStatus.Active,
            adminName: null, adminEmail: null, phone: null, address: null, expiryDate);

        tenantStore.Reload();
        await InitializeTenantDatabaseAsync(society, adminUsername, adminPassword);
        return society;
    }

    /// <summary>Self-service registration: records a PENDING society with contact details. Does NOT
    /// create a database or make the tenant resolvable — that only happens on approval.</summary>
    public async Task<Society> RegisterPendingAsync(
        string key, string displayName, string? adminName, string? adminEmail,
        string? phone, string? address, string? plan, int flatCount)
    {
        return await CreateSocietyRecordAsync(
            key, displayName, plan, flatCount, SocietyStatus.Pending,
            adminName, adminEmail, phone, address, expiryDate: null);
    }

    /// <summary>Approves a pending society: flips it to Active, provisions + seeds its database, and sets
    /// the initial admin credentials. Returns those credentials so the console can display/email them.</summary>
    public async Task<(Society Society, string AdminUsername, string AdminPassword)> ApproveAsync(
        string key, string? adminUsername, string? adminPassword, DateTime? expiryDate)
    {
        var society = await controlDb.Societies.FirstOrDefaultAsync(s => s.Key == key)
            ?? throw new InvalidOperationException("Society not found.");

        if (!string.Equals(society.Status, SocietyStatus.Pending, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a society that is pending approval can be approved.");

        society.Status = SocietyStatus.Active;
        if (expiryDate.HasValue) society.ExpiryDate = expiryDate.Value.Date;
        await controlDb.SaveChangesAsync();

        tenantStore.Reload();

        var finalUsername = string.IsNullOrWhiteSpace(adminUsername) ? "admin" : adminUsername.Trim();
        var finalPassword = string.IsNullOrWhiteSpace(adminPassword) ? GenerateTempPassword() : adminPassword;

        try
        {
            await InitializeTenantDatabaseAsync(society, finalUsername, finalPassword);
        }
        catch
        {
            // Provisioning failed after the status flip — roll back to Pending so we don't leave an
            // "Active but database-less" society that 500s on every login.
            society.Status = SocietyStatus.Pending;
            await controlDb.SaveChangesAsync();
            tenantStore.Reload();
            throw;
        }

        return (society, finalUsername, finalPassword);
    }

    /// <summary>Validates the key/name and inserts a control-plane Society row (no database yet).</summary>
    private async Task<Society> CreateSocietyRecordAsync(
        string key, string displayName, string? plan, int flatCount, string status,
        string? adminName, string? adminEmail, string? phone, string? address, DateTime? expiryDate)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();

        if (!SlugRegex().IsMatch(key))
            throw new InvalidOperationException("Society code must be a lowercase URL-safe slug (letters, digits, hyphens).");

        if (ReservedKeys.Contains(key))
            throw new InvalidOperationException($"'{key}' is a reserved code and cannot be used.");

        if (await controlDb.Societies.AnyAsync(s => s.Key == key))
            throw new InvalidOperationException("A society with this code already exists.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("Society name is required.");

        var society = new Society
        {
            Key = key,
            DisplayName = displayName.Trim(),
            DbName = "SmmsDb_" + char.ToUpperInvariant(key[0]) + key[1..],
            Status = status,
            Plan = string.IsNullOrWhiteSpace(plan) ? null : plan.Trim(),
            FlatCount = flatCount,
            AdminName = adminName?.Trim(),
            AdminEmail = adminEmail?.Trim(),
            Phone = phone?.Trim(),
            Address = address?.Trim(),
            ExpiryDate = expiryDate
        };

        controlDb.Societies.Add(society);
        await controlDb.SaveChangesAsync();
        return society;
    }

    /// <summary>Creates + migrates + seeds the tenant database, then applies the initial admin credentials.</summary>
    private async Task InitializeTenantDatabaseAsync(Society society, string? adminUsername, string? adminPassword)
    {
        var tenant = tenantStore.GetByKey(society.Key)
            ?? throw new InvalidOperationException("Provisioned society could not be resolved after reload.");

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        var tenantDb = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();

        await tenantDb.Database.MigrateAsync();
        DbSeeder.Seed(tenantDb, society.DisplayName);

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
    }

    /// <summary>Readable 12-char temp password (no ambiguous 0/O/1/l/I) for a newly approved admin.</summary>
    private static string GenerateTempPassword()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var chars = new char[12];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}
