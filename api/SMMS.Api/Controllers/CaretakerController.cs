using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Storage;

namespace SMMS.Api.Controllers;

/// <summary>
/// The caretaker's day: who came in, what was delivered, what is broken and whether the
/// morning round was done. Admins share the routes so the committee can see the same log
/// without a second implementation.
/// </summary>
[ApiController]
[Route("api/caretaker")]
[Authorize(Roles = $"{Roles.Caretaker},{Roles.Admin}")]
public class CaretakerController(SmmsDbContext db, AuditService audit, IFileStorage storage) : ControllerBase
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    /// <summary>The morning round. Labels are stored on each submission, so changing this list
    /// never rewrites what was signed off on an earlier day.</summary>
    private static readonly string[] ChecklistTemplate =
    [
        "Main gate locked",
        "Street lights off",
        "Water pump started",
        "Tank level checked",
        "Generator checked",
        "Garden watered",
        "Garbage collected",
        "Lift working",
        "CCTV working",
        "Common lights working"
    ];

    /// <summary>What the caretaker can report, mapped onto the categories the committee already
    /// filters complaints by. The specific wording survives in the subject line.</summary>
    private static readonly Dictionary<string, (string Category, string Priority)> Issues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Water leakage"] = ("Plumbing", "High"),
        ["Broken pipe"] = ("Plumbing", "High"),
        ["Drainage blocked"] = ("Plumbing", "Medium"),
        ["Street light"] = ("Electrical", "Medium"),
        ["Power failure"] = ("Electrical", "High"),
        ["Common light"] = ("Electrical", "Low"),
        ["Lift stopped"] = ("Electrical", "High"),
        ["Garbage not collected"] = ("Housekeeping", "Medium"),
        ["Suspicious person"] = ("Security", "High"),
        ["Stray animal"] = ("Security", "Low"),
        ["Other"] = ("Other", "Medium")
    };

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Returns the flat exactly as the member record spells it, or null if no such flat.
    /// Storing the canonical spelling is what lets a resident's My Gate match on it later.</summary>
    private async Task<string?> ResolveFlatAsync(string? flat)
    {
        if (string.IsNullOrWhiteSpace(flat)) return null;
        var wanted = flat.Trim().ToLower();
        return await db.Members
            .Where(m => m.Status == "Active" && m.Flat.ToLower() == wanted)
            .Select(m => m.Flat)
            .FirstOrDefaultAsync();
    }

    private static VisitorDto ToDto(Visitor v) => new(
        v.Id, v.Name, v.Mobile, v.Flat, v.Purpose, v.VehicleNumber, v.InAt, v.OutAt, v.Notes);

    private static DeliveryDto ToDto(Delivery d) => new(
        d.Id, d.Courier, d.Flat, d.Status, d.ReceivedAt, d.CollectedAt, d.CollectedBy, d.Notes);

    // ── Dashboard ──

    [HttpGet("summary")]
    public async Task<ActionResult<CaretakerSummaryDto>> Summary()
    {
        var dayStart = DateTime.UtcNow.Date;
        var me = await db.Users.FindAsync(CurrentUserId);

        return Ok(new CaretakerSummaryDto(
            Today,
            me?.Name ?? me?.Username ?? "Caretaker",
            await db.Visitors.CountAsync(v => v.InAt >= dayStart),
            await db.Visitors.CountAsync(v => v.OutAt == null),
            await db.Deliveries.CountAsync(d => d.Status == "Waiting"),
            await db.Complaints.CountAsync(c => c.Status == "Open" || c.Status == "In Progress"),
            await db.DailyChecklists.AnyAsync(c => c.Date == Today)));
    }

    /// <summary>Flat numbers only. The caretaker needs them to route a visitor; resident names and
    /// contact details are deliberately not exposed here.</summary>
    [HttpGet("flats")]
    public async Task<ActionResult<IEnumerable<string>>> Flats()
    {
        var flats = await db.Members
            .Where(m => m.Status == "Active" && m.Flat != "")
            .Select(m => m.Flat)
            .Distinct()
            .ToListAsync();

        return Ok(flats
            .OrderBy(f => int.TryParse(f, out var n) ? n : int.MaxValue)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase));
    }

    // ── Visitors ──

    [HttpGet("visitors")]
    public async Task<ActionResult<IEnumerable<VisitorDto>>> Visitors(
        [FromQuery] bool insideOnly = false,
        [FromQuery] string? search = null)
    {
        var dayStart = DateTime.UtcNow.Date;
        var query = db.Visitors.AsQueryable();

        // Someone who came in yesterday and never left still needs to be signed out.
        query = insideOnly
            ? query.Where(v => v.OutAt == null)
            : query.Where(v => v.InAt >= dayStart || v.OutAt == null);

        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(v =>
                v.Name.Contains(search) ||
                v.Flat.Contains(search) ||
                (v.Mobile != null && v.Mobile.Contains(search)) ||
                (v.VehicleNumber != null && v.VehicleNumber.Contains(search)));
        }

        var rows = await query.OrderByDescending(v => v.InAt).Take(200).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    [HttpPost("visitors")]
    public async Task<ActionResult<VisitorDto>> AddVisitor(VisitorCreateRequest request)
    {
        var flat = await ResolveFlatAsync(request.Flat);
        if (flat is null)
            return BadRequest(new { message = $"'{request.Flat?.Trim()}' is not a registered flat. Pick one from the list." });

        var visitor = new Visitor
        {
            Name = request.Name.Trim(),
            Mobile = request.Mobile?.Trim(),
            Flat = flat,
            Purpose = request.Purpose.Trim(),
            VehicleNumber = request.VehicleNumber?.Trim().ToUpperInvariant(),
            Notes = request.Notes?.Trim(),
            RecordedByUserId = CurrentUserId
        };

        db.Visitors.Add(visitor);
        await db.SaveChangesAsync();
        await audit.LogAsync("Caretaker", "VisitorIn", $"Visitor {visitor.Name} in for flat {visitor.Flat}");

        return CreatedAtAction(nameof(Visitors), new { }, ToDto(visitor));
    }

    [HttpPost("visitors/{id:int}/exit")]
    public async Task<ActionResult<VisitorDto>> ExitVisitor(int id)
    {
        var visitor = await db.Visitors.FindAsync(id);
        if (visitor is null) return NotFound();
        if (visitor.OutAt is not null) return BadRequest(new { message = "That visitor is already marked out." });

        visitor.OutAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Caretaker", "VisitorOut", $"Visitor {visitor.Name} out from flat {visitor.Flat}");

        return Ok(ToDto(visitor));
    }

    // ── Deliveries ──

    [HttpGet("deliveries")]
    public async Task<ActionResult<IEnumerable<DeliveryDto>>> Deliveries(
        [FromQuery] bool waitingOnly = false,
        [FromQuery] string? search = null)
    {
        var dayStart = DateTime.UtcNow.Date;
        var query = db.Deliveries.AsQueryable();

        // A parcel nobody collected stays on the list until it is handed over.
        query = waitingOnly
            ? query.Where(d => d.Status == "Waiting")
            : query.Where(d => d.ReceivedAt >= dayStart || d.Status == "Waiting");

        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(d =>
                d.Courier.Contains(search) ||
                d.Flat.Contains(search) ||
                (d.CollectedBy != null && d.CollectedBy.Contains(search)));
        }

        var rows = await query.OrderByDescending(d => d.ReceivedAt).Take(200).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    [HttpPost("deliveries")]
    public async Task<ActionResult<DeliveryDto>> AddDelivery(DeliveryCreateRequest request)
    {
        var flat = await ResolveFlatAsync(request.Flat);
        if (flat is null)
            return BadRequest(new { message = $"'{request.Flat?.Trim()}' is not a registered flat. Pick one from the list." });

        var delivery = new Delivery
        {
            Courier = request.Courier.Trim(),
            Flat = flat,
            Notes = request.Notes?.Trim(),
            Status = "Waiting",
            RecordedByUserId = CurrentUserId
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        await audit.LogAsync("Caretaker", "DeliveryIn", $"{delivery.Courier} parcel held for flat {delivery.Flat}");

        return CreatedAtAction(nameof(Deliveries), new { }, ToDto(delivery));
    }

    [HttpPost("deliveries/{id:int}/collected")]
    public async Task<ActionResult<DeliveryDto>> CollectDelivery(int id, DeliveryCollectRequest request)
    {
        var delivery = await db.Deliveries.FindAsync(id);
        if (delivery is null) return NotFound();
        if (delivery.Status == "Collected") return BadRequest(new { message = "That parcel is already collected." });

        delivery.Status = "Collected";
        delivery.CollectedAt = DateTime.UtcNow;
        delivery.CollectedBy = string.IsNullOrWhiteSpace(request.CollectedBy) ? null : request.CollectedBy.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync("Caretaker", "DeliveryOut", $"Parcel #{delivery.Id} collected for flat {delivery.Flat}");

        return Ok(ToDto(delivery));
    }

    // ── Daily checklist ──

    [HttpGet("checklist")]
    public async Task<ActionResult<ChecklistDto>> Checklist([FromQuery] DateOnly? date)
    {
        var day = date ?? Today;
        var existing = await db.DailyChecklists
            .Include(c => c.Items)
            .Include(c => c.SubmittedByUser)
            .FirstOrDefaultAsync(c => c.Date == day);

        if (existing is null)
        {
            return Ok(new ChecklistDto(day, false, null, null, null,
                ChecklistTemplate.Select(l => new ChecklistItemDto(l, false, null))));
        }

        return Ok(new ChecklistDto(
            existing.Date, true, existing.SubmittedAt,
            existing.SubmittedByUser?.Name ?? existing.SubmittedByUser?.Username,
            existing.Notes,
            existing.Items.Select(i => new ChecklistItemDto(i.Label, i.Done, i.Remark))));
    }

    /// <summary>Submitting again on the same day replaces that day's ticks rather than creating a
    /// second record, so a caretaker can correct a mistake without help.</summary>
    [HttpPost("checklist")]
    public async Task<ActionResult<ChecklistDto>> SubmitChecklist(ChecklistSubmitRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "Tick at least one item before submitting." });

        var day = Today;
        var checklist = await db.DailyChecklists
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Date == day);

        if (checklist is null)
        {
            checklist = new DailyChecklist { Date = day, SubmittedByUserId = CurrentUserId };
            db.DailyChecklists.Add(checklist);
        }
        else
        {
            db.DailyChecklistItems.RemoveRange(checklist.Items);
            checklist.SubmittedByUserId = CurrentUserId;
        }

        checklist.SubmittedAt = DateTime.UtcNow;
        checklist.Notes = request.Notes?.Trim();
        checklist.Items = request.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Label))
            .Select(i => new DailyChecklistItem
            {
                Label = i.Label.Trim(),
                Done = i.Done,
                Remark = string.IsNullOrWhiteSpace(i.Remark) ? null : i.Remark.Trim()
            })
            .ToList();

        await db.SaveChangesAsync();
        var done = checklist.Items.Count(i => i.Done);
        await audit.LogAsync("Caretaker", "Checklist", $"Daily round submitted: {done}/{checklist.Items.Count} done");

        return await Checklist(day);
    }

    // ── Issues ──

    [HttpGet("issue-types")]
    public ActionResult<IEnumerable<string>> IssueTypes() => Ok(Issues.Keys);

    /// <summary>Raises a normal complaint, so it lands in the committee's existing queue instead of
    /// a parallel list only the caretaker can see.</summary>
    [HttpPost("complaints")]
    [RequestSizeLimit(MaxPhotoBytes + 1024 * 1024)]
    public async Task<ActionResult<CaretakerComplaintDto>> RaiseIssue(
        [FromForm] string issue,
        [FromForm] string? location,
        [FromForm] string? description,
        IFormFile? photo)
    {
        if (string.IsNullOrWhiteSpace(issue) || !Issues.TryGetValue(issue.Trim(), out var mapped))
            return BadRequest(new { message = "Pick one of the listed issues." });

        string? storedPath = null, contentType = null;
        if (photo is not null && photo.Length > 0)
        {
            if (photo.Length > MaxPhotoBytes) return BadRequest(new { message = "Photo must be under 5 MB." });
            if (!photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only image files are allowed." });

            await using var stream = photo.OpenReadStream();
            storedPath = await storage.SaveAsync(stream, "caretaker", photo.FileName);
            contentType = photo.ContentType;
        }

        var where = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        var complaint = new Complaint
        {
            Subject = where is null ? issue.Trim() : $"{issue.Trim()} — {where}",
            Description = string.IsNullOrWhiteSpace(description)
                ? $"Reported from the gate by the caretaker.{(where is null ? "" : $" Location: {where}.")}"
                : description.Trim(),
            Category = mapped.Category,
            Priority = mapped.Priority,
            Status = "Open",
            RaisedByUserId = CurrentUserId,
            PhotoPath = storedPath,
            PhotoContentType = contentType
        };

        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();
        await audit.LogAsync("Caretaker", "Issue", $"Raised complaint #{complaint.Id}: {complaint.Subject}");

        return Ok(new CaretakerComplaintDto(
            complaint.Id, complaint.Subject, complaint.Category, complaint.Priority,
            complaint.Status, complaint.CreatedAt, complaint.PhotoPath is not null));
    }

    [HttpGet("complaints")]
    public async Task<ActionResult<IEnumerable<CaretakerComplaintDto>>> MyIssues()
    {
        var userId = CurrentUserId;
        var rows = await db.Complaints
            .Where(c => c.RaisedByUserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(rows.Select(c => new CaretakerComplaintDto(
            c.Id, c.Subject, c.Category, c.Priority, c.Status, c.CreatedAt, c.PhotoPath is not null)));
    }
}
