using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>"Admin" or "Member".</summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = "Member";

    /// <summary>"Pending" (awaiting admin approval), "Active", or "Inactive".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(20)]
    public string? Flat { get; set; }

    [MaxLength(20)]
    public string? Floor { get; set; }

    [MaxLength(300)]
    public string? SecurityQuestion { get; set; }

    public string? SecurityAnswerHash { get; set; }

    /// <summary>JSON-serialized per-module permission overrides, e.g. {"Collections":"Edit","Settings":"None"}.
    /// Admin role always has full access regardless of this. Modules not present default to "View".</summary>
    public string? Permissions { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
