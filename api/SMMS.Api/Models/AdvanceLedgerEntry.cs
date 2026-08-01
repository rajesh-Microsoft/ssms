using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>
/// Append-only ledger of a member's advance (wallet) movements. Every credit (money received but
/// not yet earned) and debit (advance applied to a month's invoice) is recorded here with a
/// running <see cref="BalanceAfter"/> snapshot, giving a full audit trail and letting the cached
/// <see cref="Member.AdvanceBalance"/> be rebuilt/verified at any time. Rows are never edited.
/// </summary>
public class AdvanceLedgerEntry
{
    public int Id { get; set; }

    [Required]
    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Member? Member { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>"Credit" (money into the wallet) or "Debit" (advance applied to an invoice).</summary>
    [Required, MaxLength(10)]
    public string Type { get; set; } = "Credit";

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>Wallet balance immediately after this entry was posted.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal BalanceAfter { get; set; }

    /// <summary>"Payment", "BillAdjustment", "ManualAdmin" or "Refund".</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = "Payment";

    /// <summary>The invoice this entry relates to (the one paid into, or debited against). Loose ref.</summary>
    public int? CollectionId { get; set; }

    /// <summary>The payment proof that produced a credit, when applicable. Loose ref.</summary>
    public int? PaymentProofId { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}
