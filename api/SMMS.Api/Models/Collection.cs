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

    /// <summary>"Paid", "Unpaid", or "Partial".</summary>
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
}
