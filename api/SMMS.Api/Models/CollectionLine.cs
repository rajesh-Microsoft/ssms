using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>One line of an invoice's component breakdown. Sum of lines equals Collection.Amount.</summary>
public class CollectionLine : IAuditable
{
    public int Id { get; set; }

    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }

    public int? ComponentId { get; set; }

    [Required, MaxLength(100)]
    public string ComponentName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Method { get; set; } = string.Empty;

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
