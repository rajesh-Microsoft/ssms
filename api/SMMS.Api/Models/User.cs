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

    // ── Resident ("My Home") profile fields — editable by the member themselves via /api/me ──

    [MaxLength(150)]
    public string? Name { get; set; }

    /// <summary>"Owner" or "Tenant".</summary>
    [MaxLength(20)]
    public string? OccupancyType { get; set; }

    [MaxLength(200)]
    public string? EmergencyContact { get; set; }

    /// <summary>Profile photo as a data URI (e.g. "data:image/png;base64,...").</summary>
    public string? ProfilePhoto { get; set; }

    /// <summary>JSON array of family members: [{"relationship":"Spouse","name":"...","mobile":"..."}].</summary>
    public string? FamilyJson { get; set; }

    /// <summary>JSON array of vehicles: [{"number":"...","type":"Car","slot":"..."}].</summary>
    public string? VehiclesJson { get; set; }

    /// <summary>JSON object of notification preferences: {"sms":true,"email":true,"whatsapp":false}.</summary>
    public string? NotifyPrefsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
