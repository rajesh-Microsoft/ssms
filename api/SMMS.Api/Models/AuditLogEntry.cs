using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(50)]
    public string User { get; set; } = "System";

    [Required, MaxLength(50)]
    public string Module { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }
}
