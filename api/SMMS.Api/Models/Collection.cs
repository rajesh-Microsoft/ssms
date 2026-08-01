using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

public class Collection
{
    public int Id { get; set; }

    [Required]
    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Member? Member { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>Amount settled so far (from the advance wallet or partial payments). Balance still
    /// payable is <c>Amount - AmountPaid</c>; drives the "Partial" status.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal AmountPaid { get; set; }

    /// <summary>"Paid", "Unpaid", "Partial", or "Overdue".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Paid";

    [Range(1, 12)]
    public int Month { get; set; }

    public int Year { get; set; }

    public DateTime? PaymentDate { get; set; }

    [MaxLength(30)]
    public string? PaymentMode { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    // ── Invoice/UPI-payment fields ──

    /// <summary>Human-friendly invoice number, e.g. "INV000245". Null on legacy rows;
    /// callers fall back to a derived "INV{Id:D6}" when generating QR/receipts.</summary>
    [MaxLength(30)]
    public string? InvoiceNumber { get; set; }

    /// <summary>Due date for this charge. Null on legacy rows; derived from settings when needed.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>True once the overdue job has added the late fee to Amount, so the daily pass
    /// never double-charges the same invoice.</summary>
    public bool LateFeeApplied { get; set; } = false;

    /// <summary>"Monthly" for the recurring maintenance invoice, or "OneTime" for a special
    /// one-off charge (e.g. a festival fund) raised from a OneTime collection category.</summary>
    [Required, MaxLength(20)]
    public string CollectionType { get; set; } = "Monthly";

    /// <summary>For one-time charges: the originating collection category. Loose reference
    /// (no FK) so deleting a category never blocks or cascades into raised charges.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Friendly label shown to residents for one-time charges (e.g. "Diwali Fund").
    /// Null for monthly invoices, which fall back to the month/year billing label.</summary>
    [MaxLength(100)]
    public string? Title { get; set; }

    /// <summary>Per-component breakdown produced by the maintenance rule engine.</summary>
    public ICollection<CollectionLine> Lines { get; set; } = new List<CollectionLine>();
}
