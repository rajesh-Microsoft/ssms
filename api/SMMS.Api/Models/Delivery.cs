using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>A parcel held at the gate until the resident collects it.</summary>
public class Delivery : IAuditable
{
    public int Id { get; set; }

    /// <summary>Amazon, Flipkart, Swiggy, Zomato, Courier, Medicine, Milk, Other.</summary>
    [Required, MaxLength(60)]
    public string Courier { get; set; } = "Other";

    [Required, MaxLength(20)]
    public string Flat { get; set; } = string.Empty;

    /// <summary>"Waiting" while it sits at the gate, "Collected" once handed over.</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Waiting";

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CollectedAt { get; set; }

    [MaxLength(150)]
    public string? CollectedBy { get; set; }

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
