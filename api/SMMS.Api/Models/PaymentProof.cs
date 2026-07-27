using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMMS.Api.Models;

/// <summary>
/// A resident-submitted proof of a UPI payment against a maintenance charge (Collection).
/// Flows Pending -&gt; Approved/Rejected. On approval the linked Collection is marked Paid.
/// Designed to be gateway-agnostic: future automated gateways (Razorpay, Cashfree, PhonePe,
/// ICICI Smart Collect) can populate <see cref="GatewayName"/>/<see cref="GatewayReference"/>
/// on the same table without a schema change.
/// </summary>
public class PaymentProof
{
    public int Id { get; set; }

    /// <summary>The maintenance charge (invoice/ledger row) this payment settles.</summary>
    [Required]
    public int CollectionId { get; set; }

    [ForeignKey(nameof(CollectionId))]
    public Collection? Collection { get; set; }

    /// <summary>The member (flat) the charge belongs to. Denormalized for fast admin filtering.</summary>
    [Required]
    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public Member? Member { get; set; }

    /// <summary>User account that submitted the proof (resident).</summary>
    public int SubmittedByUserId { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>UPI transaction/UTR reference the resident optionally typed in.</summary>
    [MaxLength(60)]
    public string? UpiReference { get; set; }

    /// <summary>Original uploaded file name.</summary>
    [MaxLength(260)]
    public string? FileName { get; set; }

    /// <summary>Server-relative stored path of the screenshot (never client-controlled).</summary>
    [MaxLength(400)]
    public string? StoredPath { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    /// <summary>"Pending", "Approved", or "Rejected".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public int? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(500)]
    public string? ReviewRemarks { get; set; }

    // ── Future automated-gateway hooks (unused by the manual UPI flow) ──

    [MaxLength(40)]
    public string? GatewayName { get; set; }

    [MaxLength(120)]
    public string? GatewayReference { get; set; }
}
