using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

public class PlatformInvoice
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public int SocietyId { get; set; }
    public Society Society { get; set; } = null!;

    public int SubscriptionPlanId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    [Required, MaxLength(50)]
    public string PlanCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PlanName { get; set; } = string.Empty;

    public int BillingPeriodMonths { get; set; }
    public decimal Amount { get; set; }

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "INR";

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime DueDate { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = PlatformInvoiceStatus.Draft;

    public DateTime? PaidAt { get; set; }

    [MaxLength(150)]
    public string? PaymentReference { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class PlatformInvoiceStatus
{
    public const string Draft = "Draft";
    public const string Issued = "Issued";
    public const string Paid = "Paid";
    public const string Overdue = "Overdue";
    public const string Void = "Void";
}