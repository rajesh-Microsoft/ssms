using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>One month's financial plan. Holds the cash position the month starts from and
/// what the committee expects to collect; the expected outgoings hang off it as BudgetItems.</summary>
public class Budget : IAuditable
{
    public int Id { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    public int Year { get; set; }

    /// <summary>Cash carried into the month. Seeded from the ledger but admin-editable, because
    /// the true bank position can differ from what has been entered so far.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal OpeningBalance { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal ExpectedCollection { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Draft";

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<BudgetItem> Items { get; set; } = new List<BudgetItem>();

    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
