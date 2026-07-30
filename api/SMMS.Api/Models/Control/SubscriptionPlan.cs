using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

public class SubscriptionPlan
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public int BillingPeriodMonths { get; set; } = 12;

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "INR";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}