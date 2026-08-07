using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>A person logged in at the gate by the caretaker. Holds personal data
/// (name, mobile, vehicle), so it is readable only by the caretaker and admins.</summary>
public class Visitor : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Mobile { get; set; }

    /// <summary>The flat being visited. Free text, because a visitor may arrive for a
    /// flat that has no member row yet.</summary>
    [Required, MaxLength(20)]
    public string Flat { get; set; } = string.Empty;

    /// <summary>Guest, Delivery, Courier, Maid, Electrician, Plumber, Cab, Other.</summary>
    [Required, MaxLength(40)]
    public string Purpose { get; set; } = "Guest";

    [MaxLength(20)]
    public string? VehicleNumber { get; set; }

    public DateTime InAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the visitor is still inside; set when the caretaker marks them out.</summary>
    public DateTime? OutAt { get; set; }

    [MaxLength(200)]
    public string? Notes { get; set; }

    [Required]
    public int RecordedByUserId { get; set; }

    [ForeignKey(nameof(RecordedByUserId))]
    public User? RecordedByUser { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
