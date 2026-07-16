using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
public class MembersController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static MemberDto ToDto(Member m) => new(m.Id, m.Name, m.Flat, m.Floor, m.Mobile, m.Email, m.Status);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MemberDto>>> GetAll()
    {
        var members = await db.Members.OrderBy(m => m.Name).ToListAsync();
        return Ok(members.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MemberDto>> GetById(int id)
    {
        var member = await db.Members.FindAsync(id);
        return member is null ? NotFound() : Ok(ToDto(member));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<MemberDto>> Create(MemberUpsertRequest request)
    {
        var member = new Member
        {
            Name = request.Name,
            Flat = request.Flat,
            Floor = request.Floor,
            Mobile = request.Mobile,
            Email = request.Email,
            Status = request.Status
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Add", $"Added: {member.Name} ({member.Flat})");
        return CreatedAtAction(nameof(GetById), new { id = member.Id }, ToDto(member));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, MemberUpsertRequest request)
    {
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        member.Name = request.Name;
        member.Flat = request.Flat;
        member.Floor = request.Floor;
        member.Mobile = request.Mobile;
        member.Email = request.Email;
        member.Status = request.Status;
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Update", $"Updated: {member.Name} ({member.Flat})");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var member = await db.Members.FindAsync(id);
        if (member is null) return NotFound();

        db.Members.Remove(member);
        await db.SaveChangesAsync();
        await audit.LogAsync("Members", "Delete", $"Deleted member id: {id}");
        return NoContent();
    }
}
