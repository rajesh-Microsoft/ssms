using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

public class UtilityProvider
{
    public int Id { get; set; }

    [MaxLength(40)]
    public required string Code { get; set; }

    [MaxLength(100)]
    public required string ProviderName { get; set; }

    [MaxLength(40)]
    public required string Category { get; set; }

    public bool SupportsAutoFetch { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public ICollection<UtilityConnection> Connections { get; set; } = [];
}

public class UtilityConnection
{
    public int Id { get; set; }
    public int ProviderId { get; set; }

    [MaxLength(80)]
    public required string ConsumerNumber { get; set; }

    [MaxLength(80)]
    public string? ServiceNumber { get; set; }

    public bool AutoFetchEnabled { get; set; } = true;

    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? LastFetchedOn { get; set; }

    [MaxLength(500)]
    public string? LastFetchError { get; set; }

    public UtilityProvider? Provider { get; set; }
    public ICollection<UtilityBill> Bills { get; set; } = [];
}

public class UtilityBill
{
    public int Id { get; set; }
    public int UtilityConnectionId { get; set; }
    public DateTime BillingMonth { get; set; }

    [MaxLength(100)]
    public string? BillNumber { get; set; }

    public DateTime? BillDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal BillAmount { get; set; }
    public decimal? UnitsConsumed { get; set; }
    public decimal Arrears { get; set; }

    [MaxLength(200)]
    public string? ConsumerName { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Outstanding";

    public DateTime? PaidOn { get; set; }

    /// <summary>The bank UTR from the biller's own checkout - our gateway never sees this payment.</summary>
    [StringLength(80)]
    public string? PaymentReference { get; set; }

    /// <summary>The expense booked when the bill was marked paid. Its presence is what stops the
    /// same bill being booked twice.</summary>
    public int? ExpenseId { get; set; }

    public Expense? Expense { get; set; }

    public string RawHtml { get; set; } = string.Empty;
    public DateTime FetchedOn { get; set; } = DateTime.UtcNow;
    public UtilityConnection? UtilityConnection { get; set; }
}

public class UtilityNotification
{
    public int Id { get; set; }
    public int UtilityBillId { get; set; }

    [MaxLength(120)]
    public required string Title { get; set; }

    [MaxLength(500)]
    public required string Message { get; set; }

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public UtilityBill? UtilityBill { get; set; }
}