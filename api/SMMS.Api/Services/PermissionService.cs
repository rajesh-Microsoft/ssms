using System.Security.Claims;
using System.Text.Json;

namespace SMMS.Api.Services;

/// <summary>Role names that carry authorization meaning. Other roles (Treasurer, Secretary, ...)
/// are labels only and behave like Member until permissions are granted.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Member = "Member";

    /// <summary>Gate and premises staff. Runs the caretaker app and nothing else.</summary>
    public const string Caretaker = "Caretaker";
}

/// <summary>Grantable module names for the custom per-user permission system.
/// Users/AuditLog are deliberately NOT grantable here — they stay Admin-only
/// to avoid privilege escalation and audit-tampering risk.</summary>
public static class PermissionModules
{
    public const string Collections = "Collections";
    public const string Expenses = "Expenses";
    public const string Members = "Members";
    public const string Complaints = "Complaints";
    public const string Settings = "Settings";
    public const string Liabilities = "Liabilities";
    public const string Income = "Income";
    public const string Budgets = "Budgets";

    /// <summary>Stock register. Parse() defaults every module to "View", so adding this grants
    /// existing users read-only visibility of society stock — which is the intent (transparency).
    /// Recording stock still requires "Edit".</summary>
    public const string Inventory = "Inventory";

    public static readonly string[] All = [Collections, Expenses, Members, Complaints, Settings, Liabilities, Income, Budgets, Inventory];

    public static readonly string[] ValidLevels = ["None", "View", "Edit"];
}

/// <summary>Groups whose membership is not stored. A user inherits whichever of these matches
/// their <c>OccupancyType</c>, so that one field stays the single source of truth and group
/// membership can never drift from it.</summary>
public static class UserGroups
{
    public const string Owner = "Owner";
    public const string Tenant = "Tenant";

    /// <summary>Occupancy is what the committee recorded, so an account with none set is treated
    /// as a tenant: the cautious answer, and the one that keeps a flat's accounts private until
    /// somebody confirms who lives there.</summary>
    public static string ForOccupancy(string? occupancyType) =>
        string.Equals(occupancyType?.Trim(), Owner, StringComparison.OrdinalIgnoreCase) ? Owner : Tenant;
}

public static class PermissionHelper
{
    /// <summary>Parses a user's stored Permissions JSON into a module -&gt; level map.
    /// Missing/invalid/malformed entries default to "View" (preserves the original
    /// read-only-Member behavior unless an Admin explicitly restricts or grants Edit).</summary>
    public static Dictionary<string, string> Parse(string? permissionsJson)
    {
        var result = PermissionModules.All.ToDictionary(m => m, _ => "View");
        if (string.IsNullOrWhiteSpace(permissionsJson)) return result;

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(permissionsJson);
            if (stored is null) return result;
            foreach (var (module, level) in stored)
            {
                if (result.ContainsKey(module) && PermissionModules.ValidLevels.Contains(level))
                    result[module] = level;
            }
        }
        catch (JsonException) { /* malformed data — fall back to defaults */ }

        return result;
    }

    public static string Serialize(Dictionary<string, string> permissions)
    {
        var clean = PermissionModules.All.ToDictionary(
            m => m,
            m => permissions.TryGetValue(m, out var level) && PermissionModules.ValidLevels.Contains(level) ? level : "View");
        return JsonSerializer.Serialize(clean);
    }

    private static int Rank(string level) => level switch { "Edit" => 2, "View" => 1, _ => 0 };

    /// <summary>
    /// Combines the permissions of every group a user belongs to. Where two groups disagree the
    /// more permissive level wins — a Treasurer who also sits on the Festival Committee keeps the
    /// Treasurer's Edit rather than losing it to the committee's View.
    /// Modules no group mentions stay at the "View" default, so joining a group never removes the
    /// read access a member already had.
    /// </summary>
    public static Dictionary<string, string> Combine(IEnumerable<string?> groupPermissionsJson)
    {
        var result = PermissionModules.All.ToDictionary(m => m, _ => "View");
        var seenAny = false;

        foreach (var json in groupPermissionsJson)
        {
            seenAny = true;
            foreach (var (module, level) in Parse(json))
                if (Rank(level) > Rank(result[module])) result[module] = level;
        }

        if (!seenAny) return result;

        // A group that grants nothing must be able to say so, but "View everywhere" is the
        // baseline for a member in no group at all, so only lower a module when every group
        // that mentions it says None.
        foreach (var module in PermissionModules.All)
        {
            var levels = groupPermissionsJson.Select(j => Parse(j)[module]).ToList();
            if (levels.Count > 0 && levels.All(l => l == "None")) result[module] = "None";
        }

        return result;
    }

    // Caretakers are excluded outright rather than by storing "None" everywhere, because Parse()
    // falls back to "View" for any module missing from the JSON: a caretaker account saved without
    // explicit permissions would otherwise be able to read collections, expenses and members.

    /// <summary>True if the user is an Admin, or has "View"/"Edit" granted for the module.</summary>
    public static bool CanView(this ClaimsPrincipal user, string module) =>
        !user.IsInRole(Roles.Caretaker) &&
        (user.IsInRole(Roles.Admin) || user.FindAll($"perm:{module}").Any(c => c.Value is "View" or "Edit"));

    /// <summary>True if the user is an Admin, or has "Edit" granted for the module.</summary>
    public static bool CanEdit(this ClaimsPrincipal user, string module) =>
        !user.IsInRole(Roles.Caretaker) &&
        (user.IsInRole(Roles.Admin) || user.FindAll($"perm:{module}").Any(c => c.Value == "Edit"));
}
