using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>A single expected outgoing inside a month's budget, e.g. "Electricity, ~12,500, due 10th".
/// When the real bill arrives it is converted into an Expense and ExpenseId is stamped here, which is
/// what keeps a line from being booked to the ledger twice.</summary>
public class BudgetItem : IAuditable
{
    public int Id { get; set; }

    public int BudgetId { get; set; }
    public Budget? Budget { get; set; }

    /// <summary>Free text matching the society's configured expense categories, mirroring
    /// Expense.Category — categories live in settings, not in a table.</summary>
    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal EstimatedAmount { get; set; }

    /// <summary>Set only once the bill has arrived and been converted. Variance is derived from
    /// this rather than stored, so editing an amount can never leave a stale variance behind.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal? ActualAmount { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>"Expected" until the bill arrives, then "Actual".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Expected";

    public int? ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
