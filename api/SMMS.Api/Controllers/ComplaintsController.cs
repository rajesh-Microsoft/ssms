using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/complaints")]
[Authorize]
public class ComplaintsController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static ComplaintDto ToDto(Complaint c) => new(
        c.Id, c.Subject, c.Description, c.Category, c.Priority, c.Status,
        c.RaisedByUserId, c.RaisedByUser?.Username ?? string.Empty, c.Flat, c.Floor,
        c.CreatedAt, c.ResolvedAt, c.ResolutionNotes, c.AssignedTo);

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    // Admins, or anyone explicitly granted "Edit" on the Complaints module, can see/manage all complaints.
    private bool CanManageAll => User.CanEdit(PermissionModules.Complaints);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ComplaintDto>>> GetAll([FromQuery] string? status)
    {
        var query = db.Complaints.Include(c => c.RaisedByUser).AsQueryable();
        if (!CanManageAll)
            query = query.Where(c => c.RaisedByUserId == CurrentUserId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        var results = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComplaintDto>> GetById(int id)
    {
        var complaint = await db.Complaints.Include(c => c.RaisedByUser).FirstOrDefaultAsync(c => c.Id == id);
        if (complaint is null) return NotFound();
        if (!CanManageAll && complaint.RaisedByUserId != CurrentUserId) return Forbid();

        return Ok(ToDto(complaint));
    }

    [HttpPost]
    public async Task<ActionResult<ComplaintDto>> Create(ComplaintCreateRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized();

        var complaint = new Complaint
        {
            Subject = request.Subject,
            Description = request.Description,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "Other" : request.Category,
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Status = "Open",
            RaisedByUserId = user.Id,
            Flat = user.Flat,
            Floor = user.Floor
        };

        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();
        await audit.LogAsync("Complaints", "Add", $"Raised complaint: {complaint.Subject}");

        complaint.RaisedByUser = user;
        return CreatedAtAction(nameof(GetById), new { id = complaint.Id }, ToDto(complaint));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ComplaintUpdateRequest request)
    {
        if (!CanManageAll) return Forbid();
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();

        complaint.Subject = request.Subject;
        complaint.Description = request.Description;
        complaint.Category = request.Category;
        complaint.Priority = request.Priority;
        complaint.ResolutionNotes = request.ResolutionNotes;
        complaint.AssignedTo = request.AssignedTo;

        var wasResolved = complaint.Status is "Resolved" or "Closed";
        complaint.Status = request.Status;
        var isNowResolved = request.Status is "Resolved" or "Closed";
        if (isNowResolved && !wasResolved) complaint.ResolvedAt = DateTime.UtcNow;
        else if (!isNowResolved) complaint.ResolvedAt = null;

        await db.SaveChangesAsync();
        await audit.LogAsync("Complaints", "Update", $"Updated complaint #{id} -> {complaint.Status}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var complaint = await db.Complaints.FindAsync(id);
        if (complaint is null) return NotFound();

        if (!CanManageAll)
        {
            if (complaint.RaisedByUserId != CurrentUserId) return Forbid();
            if (complaint.Status != "Open") return BadRequest(new { message = "Only open complaints can be withdrawn." });
        }

        db.Complaints.Remove(complaint);
        await db.SaveChangesAsync();
        await audit.LogAsync("Complaints", "Delete", $"Deleted complaint #{id}");
        return NoContent();
    }
}
