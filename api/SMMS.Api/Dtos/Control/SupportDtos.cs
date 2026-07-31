namespace SMMS.Api.Dtos.Control;

/// <summary>One message in a support ticket's conversation thread.</summary>
public record SupportTicketMessageDto(
    int Id,
    string AuthorUsername,
    string Body,
    DateTime CreatedAt);

/// <summary>A support ticket as shown in the platform console, including its thread.</summary>
public record SupportTicketDto(
    int Id,
    string TicketNumber,
    string? SocietyKey,
    string? SocietyName,
    string Subject,
    string Description,
    string Category,
    string Priority,
    string Status,
    string? ContactName,
    string? ContactEmail,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    IEnumerable<SupportTicketMessageDto> Messages);

/// <summary>Payload to log a new support ticket. <see cref="SocietyKey"/> is optional (general tickets).</summary>
public record CreateTicketRequest(
    string? SocietyKey,
    string Subject,
    string Description,
    string? Category,
    string? Priority,
    string? ContactName,
    string? ContactEmail);

/// <summary>Change a ticket's status, optionally appending a note to the thread.</summary>
public record UpdateTicketStatusRequest(string Status, string? Note);

/// <summary>Append a message to a ticket's thread.</summary>
public record AddTicketMessageRequest(string Body);

/// <summary>Ticket counts by status for the Support view KPIs.</summary>
public record TicketSummaryDto(int Total, int Open, int InProgress, int Resolved, int Closed);
