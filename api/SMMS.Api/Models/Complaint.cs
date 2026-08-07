using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

public class Complaint : IAuditable, ISoftDelete
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>e.g. Plumbing, Electrical, Security, Housekeeping, Parking, Noise, Other.</summary>
    [Required, MaxLength(60)]
    public string Category { get; set; } = "Other";

    /// <summary>"Low", "Medium", or "High".</summary>
    [Required, MaxLength(20)]
    public string Priority { get; set; } = "Medium";

    /// <summary>"Open", "In Progress", "Resolved", or "Closed".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Open";

    [Required]
    public int RaisedByUserId { get; set; }

    [ForeignKey(nameof(RaisedByUserId))]
    public User? RaisedByUser { get; set; }

    // Denormalized snapshot of the raiser's flat/floor at submission time —
    // mirrors the same non-FK denormalization pattern already used by User.Flat/Floor.
    [MaxLength(20)]
    public string? Flat { get; set; }

    [MaxLength(10)]
    public string? Floor { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(1000)]
    public string? ResolutionNotes { get; set; }

    [MaxLength(150)]
    public string? AssignedTo { get; set; }

    /// <summary>Storage-owned path of the photo attached when the complaint was raised.
    /// Set by the caretaker app, where a photo is faster than typing a description.</summary>
    [MaxLength(300)]
    public string? PhotoPath { get; set; }

    [MaxLength(100)]
    public string? PhotoContentType { get; set; }

    // ── Audit (IAuditable) + soft delete (ISoftDelete) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}
