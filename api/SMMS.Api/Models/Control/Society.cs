using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

/// <summary>Platform-level record of one onboarded society (tenant). Lives in SmmsControlDb,
/// NOT in any tenant database. This is the source of truth for tenant resolution.</summary>
public class Society
{
    public int Id { get; set; }

    /// <summary>URL-safe slug used as the subdomain and tenant key, e.g. "aadya".</summary>
    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Name of this tenant's SQL database, e.g. "SmmsDb_Aadya". The full connection
    /// string is built at runtime from ControlPlane:TenantConnectionTemplate.</summary>
    [Required, MaxLength(128)]
    public string DbName { get; set; } = string.Empty;

    /// <summary>"Active", "Trial", or "Suspended".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = SocietyStatus.Active;

    [MaxLength(50)]
    public string? Plan { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public int FlatCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class SocietyStatus
{
    public const string Active = "Active";
    public const string Trial = "Trial";
    public const string Suspended = "Suspended";
}
