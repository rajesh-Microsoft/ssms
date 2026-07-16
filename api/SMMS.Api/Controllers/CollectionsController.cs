using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/collections")]
[Authorize]
public class CollectionsController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    private static CollectionDto ToDto(Collection c) => new(
        c.Id, c.MemberId, c.Member?.Name, c.Member?.Flat, c.Amount, c.Status, c.Month, c.Year, c.PaymentDate, c.PaymentMode, c.Remarks);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CollectionDto>>> GetAll([FromQuery] int? year, [FromQuery] int? month)
    {
        var query = db.Collections.Include(c => c.Member).AsQueryable();
        if (year.HasValue) query = query.Where(c => c.Year == year.Value);
        if (month.HasValue) query = query.Where(c => c.Month == month.Value);
        var results = await query.OrderByDescending(c => c.Year).ThenByDescending(c => c.Month).ToListAsync();
        return Ok(results.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CollectionDto>> GetById(int id)
    {
        var c = await db.Collections.Include(x => x.Member).FirstOrDefaultAsync(x => x.Id == id);
        return c is null ? NotFound() : Ok(ToDto(c));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CollectionDto>> Create(CollectionUpsertRequest request)
    {
        var memberExists = await db.Members.AnyAsync(m => m.Id == request.MemberId);
        if (!memberExists) return BadRequest(new { message = "Member not found." });

        var collection = new Collection
        {
            MemberId = request.MemberId,
            Amount = request.Amount,
            Status = request.Status,
            Month = request.Month,
            Year = request.Year,
            PaymentDate = request.PaymentDate,
            PaymentMode = request.PaymentMode,
            Remarks = request.Remarks
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "Add", $"Added collection of {collection.Amount} for member {collection.MemberId}");
        var saved = await db.Collections.Include(c => c.Member).FirstAsync(c => c.Id == collection.Id);
        return CreatedAtAction(nameof(GetById), new { id = collection.Id }, ToDto(saved));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CollectionUpsertRequest request)
    {
        var collection = await db.Collections.FindAsync(id);
        if (collection is null) return NotFound();

        collection.MemberId = request.MemberId;
        collection.Amount = request.Amount;
        collection.Status = request.Status;
        collection.Month = request.Month;
        collection.Year = request.Year;
        collection.PaymentDate = request.PaymentDate;
        collection.PaymentMode = request.PaymentMode;
        collection.Remarks = request.Remarks;
        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "Update", $"Updated collection id {id}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var collection = await db.Collections.FindAsync(id);
        if (collection is null) return NotFound();

        db.Collections.Remove(collection);
        await db.SaveChangesAsync();
        await audit.LogAsync("Collections", "Delete", $"Deleted collection id: {id}");
        return NoContent();
    }
}
