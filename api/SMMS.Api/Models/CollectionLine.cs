using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>One line of an invoice's component breakdown. Sum of lines equals Collection.Amount.</summary>
public class CollectionLine
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
}
