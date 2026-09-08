using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Why stock moved. Stored as a string so the history stays readable in SQL and
/// survives enum reordering, matching LiabilitySource/LiabilityStatus.</summary>
public enum InventoryMovementType
{
    /// <summary>Stock bought and received. Always increases stock.</summary>
    Purchase,

    /// <summary>Stock consumed by the society. Always decreases stock.</summary>
    Usage,

    /// <summary>Admin correction after a physical count. May go either way.</summary>
    Adjustment
}

/// <summary>A consumable the society buys and uses up — bulbs, phenyl, garbage bags.
/// Deliberately not an asset register: no serial numbers, warranty or depreciation.</summary>
public class InventoryItem : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Free text like Expense.Category rather than a lookup table — a society has a
    /// handful of categories and the UI offers the common ones.</summary>
    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Unit { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,2)")]
    public decimal MinimumStockLevel { get; set; }

    /// <summary>Running balance, always written in the same SaveChanges as the movement that
    /// changed it. Guarded by <see cref="RowVersion"/> so two concurrent stock-outs cannot both
    /// pass the "enough stock?" check; the loser gets a concurrency exception instead of a
    /// negative balance. <see cref="Movements"/> remains the source of truth and must always
    /// sum to this value.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal CurrentStock { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>Items are retired, never deleted, so their history stays readable.</summary>
    public bool IsActive { get; set; } = true;

    public List<InventoryMovement> Movements { get; set; } = [];

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>One line of the stock register. Never updated or deleted — corrections are a new
/// Adjustment row, so the history stays auditable.</summary>
public class InventoryMovement : IAuditable
{
    public int Id { get; set; }

    public int InventoryItemId { get; set; }

    [ForeignKey(nameof(InventoryItemId))]
    public InventoryItem? Item { get; set; }

    public InventoryMovementType MovementType { get; set; }

    /// <summary>Signed: positive adds stock, negative removes it. Storing the sign makes the
    /// reconciliation check a plain SUM instead of a per-type CASE.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal Quantity { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    public DateTime MovementDate { get; set; }

    /// <summary>Set when the purchase was also booked as a society expense, so the spend can be
    /// traced back. Usage and adjustments never book an expense — the cost was recorded when the
    /// stock was bought.</summary>
    public int? ExpenseId { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    public Expense? Expense { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
