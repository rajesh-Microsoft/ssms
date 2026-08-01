using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>How a liability settlement moved the money.</summary>
public enum LiabilitySettlementMethod
{
    /// <summary>Real cash paid back to the contributor — books a matching <see cref="Expense"/>.</summary>
    Repaid,
    /// <summary>Balance parked into the member's advance wallet to offset future maintenance.</summary>
    ConvertedToAdvance
}

/// <summary>
/// Append-only record of one settlement against a <see cref="SocietyLiability"/>. Never edited, so
/// the full repayment history (including partial repayments) is auditable.
/// </summary>
public class SocietyLiabilitySettlement
{
    public int Id { get; set; }

    [Required]
    public int LiabilityId { get; set; }

    [ForeignKey(nameof(LiabilityId))]
    public SocietyLiability? Liability { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    public LiabilitySettlementMethod Method { get; set; } = LiabilitySettlementMethod.Repaid;

    /// <summary>The cash-out expense created when <see cref="Method"/> is Repaid. Loose ref.</summary>
    public int? ExpenseId { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    public Expense? Expense { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}
