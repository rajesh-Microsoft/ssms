using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

/// <summary>A bill, invoice or payment screenshot proving a booked expense actually happened —
/// what an auditor asks for when a line in the books is questioned.</summary>
public class ExpenseAttachment
{
    public int Id { get; set; }

    public int ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    /// <summary>Storage-owned relative path. Never client-supplied.</summary>
    [Required, MaxLength(400)]
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>The name the uploader's device gave it. Shown back to them; never used on disk.</summary>
    [MaxLength(260)]
    public string? FileName { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public int UploadedByUserId { get; set; }
    public DateTime UploadedOn { get; set; } = DateTime.UtcNow;
}
