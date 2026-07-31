using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/support")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformSupportController(ControlDbContext db, PlatformAuditService audit) : ControllerBase
{
    private static readonly string[] Statuses =
    {
        SupportTicketStatus.Open, SupportTicketStatus.InProgress,
        SupportTicketStatus.Resolved, SupportTicketStatus.Closed
    };

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupportTicketDto>>> List(
        [FromQuery] string? societyKey, [FromQuery] string? status)
    {
        var query = db.SupportTickets.AsNoTracking()
            .Include(t => t.Society)
            .Include(t => t.Messages)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(societyKey))
            query = query.Where(t => t.Society != null && t.Society.Key == societyKey);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        var list = await query
            .OrderByDescending(t => t.UpdatedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync();

        return Ok(list.Select(ToDto));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<TicketSummaryDto>> Summary()
    {
        var statuses = await db.SupportTickets.AsNoTracking().Select(t => t.Status).ToListAsync();
        return Ok(new TicketSummaryDto(
            Total: statuses.Count,
            Open: statuses.Count(s => s == SupportTicketStatus.Open),
            InProgress: statuses.Count(s => s == SupportTicketStatus.InProgress),
            Resolved: statuses.Count(s => s == SupportTicketStatus.Resolved),
            Closed: statuses.Count(s => s == SupportTicketStatus.Closed)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupportTicketDto>> Get(int id)
    {
        var ticket = await db.SupportTickets.AsNoTracking()
            .Include(t => t.Society)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id);
        return ticket is null ? NotFound() : Ok(ToDto(ticket));
    }

    [HttpPost]
    public async Task<ActionResult<SupportTicketDto>> Create(CreateTicketRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Subject))
            return BadRequest(new { message = "Subject is required." });
        if (string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { message = "Description is required." });

        Society? society = null;
        if (!string.IsNullOrWhiteSpace(req.SocietyKey))
        {
            society = await db.Societies.FirstOrDefaultAsync(s => s.Key == req.SocietyKey);
            if (society is null) return BadRequest(new { message = "Unknown society." });
        }

        var ticket = new SupportTicket
        {
            TicketNumber = await NextTicketNumberAsync(),
            SocietyId = society?.Id,
            Subject = req.Subject.Trim(),
            Description = req.Description.Trim(),
            Category = NormalizeCategory(req.Category),
            Priority = NormalizePriority(req.Priority),
            Status = SupportTicketStatus.Open,
            ContactName = string.IsNullOrWhiteSpace(req.ContactName) ? society?.AdminName : req.ContactName.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(req.ContactEmail) ? society?.AdminEmail : req.ContactEmail.Trim()
        };
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        await audit.LogAsync("SupportTicketCreate", "SupportTicket", ticket.TicketNumber, $"'{ticket.Subject}'.");

        ticket.Society = society;
        return Created($"/api/platform/support/{ticket.Id}", ToDto(ticket));
    }

    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<SupportTicketDto>> UpdateStatus(int id, UpdateTicketStatusRequest req)
    {
        if (!Statuses.Contains(req.Status))
            return BadRequest(new { message = "Unknown status." });

        var ticket = await db.SupportTickets
            .Include(t => t.Society).Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        ticket.Status = req.Status;
        ticket.UpdatedAt = DateTime.UtcNow;
        ticket.ResolvedAt = req.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed
            ? ticket.ResolvedAt ?? DateTime.UtcNow
            : null;

        if (!string.IsNullOrWhiteSpace(req.Note))
            ticket.Messages.Add(new SupportTicketMessage { AuthorUsername = Actor(), Body = req.Note.Trim() });

        await db.SaveChangesAsync();
        await audit.LogAsync("SupportTicketStatus", "SupportTicket", ticket.TicketNumber, $"Set {ticket.Status}.");
        return Ok(ToDto(ticket));
    }

    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<SupportTicketDto>> AddMessage(int id, AddTicketMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Message body is required." });

        var ticket = await db.SupportTickets
            .Include(t => t.Society).Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        ticket.Messages.Add(new SupportTicketMessage { AuthorUsername = Actor(), Body = req.Body.Trim() });
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.LogAsync("SupportTicketReply", "SupportTicket", ticket.TicketNumber, "Reply added.");
        return Ok(ToDto(ticket));
    }

    private string Actor() => User.Identity?.Name ?? "System";

    private static string NormalizeCategory(string? c)
    {
        var v = (c ?? string.Empty).Trim();
        return v is SupportCategory.Billing or SupportCategory.Technical
            or SupportCategory.Onboarding or SupportCategory.General
            ? v : SupportCategory.General;
    }

    private static string NormalizePriority(string? p)
    {
        var v = (p ?? string.Empty).Trim();
        return v is SupportPriority.Low or SupportPriority.Normal
            or SupportPriority.High or SupportPriority.Urgent
            ? v : SupportPriority.Normal;
    }

    /// <summary>Sequential per-month ticket number, e.g. TKT-202608-0001.</summary>
    private async Task<string> NextTicketNumberAsync()
    {
        var prefix = $"TKT-{DateTime.UtcNow:yyyyMM}-";
        var last = await db.SupportTickets
            .Where(t => t.TicketNumber.StartsWith(prefix))
            .Select(t => t.TicketNumber)
            .OrderByDescending(n => n)
            .FirstOrDefaultAsync();

        var seq = 1;
        if (last is not null && int.TryParse(last[prefix.Length..], out var n)) seq = n + 1;
        return prefix + seq.ToString("D4");
    }

    private static SupportTicketDto ToDto(SupportTicket t) => new(
        t.Id, t.TicketNumber, t.Society?.Key, t.Society?.DisplayName,
        t.Subject, t.Description, t.Category, t.Priority, t.Status,
        t.ContactName, t.ContactEmail, t.CreatedAt, t.UpdatedAt, t.ResolvedAt,
        t.Messages.OrderBy(m => m.CreatedAt)
            .Select(m => new SupportTicketMessageDto(m.Id, m.AuthorUsername, m.Body, m.CreatedAt)));
}
