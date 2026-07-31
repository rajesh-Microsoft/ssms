using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models.Control;

/// <summary>A support request tracked in the platform console. May be tied to a society
/// (<see cref="SocietyId"/>) or raised as a general/platform-level ticket.</summary>
public class SupportTicket
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string TicketNumber { get; set; } = string.Empty;

    public int? SocietyId { get; set; }
    public Society? Society { get; set; }

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Category { get; set; } = SupportCategory.General;

    [Required, MaxLength(20)]
    public string Priority { get; set; } = SupportPriority.Normal;

    [Required, MaxLength(20)]
    public string Status { get; set; } = SupportTicketStatus.Open;

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [MaxLength(200)]
    public string? ContactEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public List<SupportTicketMessage> Messages { get; set; } = new();
}

/// <summary>One entry in a ticket's conversation thread (super-admin notes / correspondence).</summary>
public class SupportTicketMessage
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;

    [Required, MaxLength(50)]
    public string AuthorUsername { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class SupportTicketStatus
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";
}

public static class SupportPriority
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";
}

public static class SupportCategory
{
    public const string General = "General";
    public const string Billing = "Billing";
    public const string Technical = "Technical";
    public const string Onboarding = "Onboarding";
}
