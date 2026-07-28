using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

/// <summary>A super-admin (platform operator). Completely separate identity from tenant Users.</summary>
public class PlatformUser
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? DisplayName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}
