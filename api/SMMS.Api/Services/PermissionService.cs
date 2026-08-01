using System.Security.Claims;
using System.Text.Json;

namespace SMMS.Api.Services;

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

    public static readonly string[] All = [Collections, Expenses, Members, Complaints, Settings, Liabilities];

    public static readonly string[] ValidLevels = ["None", "View", "Edit"];
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

    /// <summary>True if the user is an Admin, or has "View"/"Edit" granted for the module.</summary>
    public static bool CanView(this ClaimsPrincipal user, string module) =>
        user.IsInRole("Admin") || user.FindAll($"perm:{module}").Any(c => c.Value is "View" or "Edit");

    /// <summary>True if the user is an Admin, or has "Edit" granted for the module.</summary>
    public static bool CanEdit(this ClaimsPrincipal user, string module) =>
        user.IsInRole("Admin") || user.FindAll($"perm:{module}").Any(c => c.Value == "Edit");
}
