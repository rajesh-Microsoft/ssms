using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>
/// A single credit (deposit) row imported from a bank statement, used to reconcile real
/// money received against resident-submitted <see cref="PaymentProof"/>s. Only incoming
/// maintenance payments are stored; debits are ignored on import. Flows Unmatched -&gt;
/// Matched (linked to a proof/charge, which gets marked Paid) or Ignored.
/// </summary>
public class BankTransaction
{
    public int Id { get; set; }

    /// <summary>Value/transaction date from the statement.</summary>
    public DateTime TxnDate { get; set; }

    /// <summary>Raw narration/remarks text from the statement row.</summary>
    [MaxLength(500)]
    public string Narration { get; set; } = string.Empty;

    /// <summary>UTR/RRN reference parsed out of the narration (12-digit UPI ref when present).</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>"Unmatched", "Matched", or "Ignored".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Unmatched";

    /// <summary>The payment proof this transaction was reconciled to (once matched).</summary>
    public int? MatchedPaymentProofId { get; set; }

    /// <summary>The maintenance charge (Collection) settled by this transaction (once matched).</summary>
    public int? MatchedCollectionId { get; set; }

    /// <summary>Groups all rows that came in from the same statement upload.</summary>
    [MaxLength(40)]
    public string ImportBatch { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
