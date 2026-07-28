using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

/// <summary>Cross-tenant audit trail for platform (super-admin) actions:
/// society create/suspend, impersonation, etc.</summary>
public class PlatformAuditLog
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ActorUsername { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? TargetType { get; set; }

    [MaxLength(100)]
    public string? TargetKey { get; set; }

    public string? Details { get; set; }

    /// <summary>Set when the action was performed while impersonating a society admin.</summary>
    [MaxLength(50)]
    public string? ImpersonatedTenant { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
