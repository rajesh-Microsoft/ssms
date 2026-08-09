using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

/// <summary>A bill, invoice or payment screenshot supporting a reimbursement claim — the evidence
/// that justifies money leaving the society.</summary>
public class ReimbursementAttachment
{
    public int Id { get; set; }

    public int RequestId { get; set; }
    public ReimbursementRequest? Request { get; set; }

    /// <summary>Storage-owned relative path. Never client-supplied.</summary>
    [Required, MaxLength(400)]
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>The name the member's device gave it. Shown back to them; never used on disk.</summary>
    [MaxLength(260)]
    public string? FileName { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public int UploadedByUserId { get; set; }
    public DateTime UploadedOn { get; set; } = DateTime.UtcNow;
}
