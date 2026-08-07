using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>One day's premises inspection, so the committee can see the round was actually done.
/// One row per calendar day, enforced by a unique index on <see cref="Date"/>.</summary>
public class DailyChecklist : IAuditable
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required]
    public int SubmittedByUserId { get; set; }

    [ForeignKey(nameof(SubmittedByUserId))]
    public User? SubmittedByUser { get; set; }

    public ICollection<DailyChecklistItem> Items { get; set; } = new List<DailyChecklistItem>();

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>One tick on the daily round. The label is copied in rather than referenced so an
/// edit to the checklist template can never rewrite what was signed off in the past.</summary>
public class DailyChecklistItem
{
    public int Id { get; set; }

    [Required]
    public int ChecklistId { get; set; }

    [ForeignKey(nameof(ChecklistId))]
    public DailyChecklist? Checklist { get; set; }

    [Required, MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    public bool Done { get; set; }

    [MaxLength(200)]
    public string? Remark { get; set; }
}
