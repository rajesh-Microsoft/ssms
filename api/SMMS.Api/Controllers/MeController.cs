using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using SMMS.Api.Services.Billing;

namespace SMMS.Api.Controllers;

/// <summary>
/// Self-service endpoints any authenticated resident can call for their OWN data.
/// This intentionally avoids the Members/Collections permission checks so a plain
/// Member can see their profile, payment history and dues without broad View grants.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(SmmsDbContext db, AuditService audit, MaintenanceCalculationService calc) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<MeProfileDto>> Get()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized();

        // Link the resident to their Member record by flat (payments are keyed to Member).
        Member? member = null;
        if (!string.IsNullOrWhiteSpace(user.Flat))
        {
            var flat = user.Flat!.ToLower();
            member = await db.Members.FirstOrDefaultAsync(m => m.Flat.ToLower() == flat);
        }

        var payments = member is null
            ? new List<MePaymentDto>()
            : await db.Collections.Where(c => c.MemberId == member.Id)
                .OrderByDescending(c => c.Year).ThenByDescending(c => c.Month)
                .Select(c => new MePaymentDto(c.Id, c.Amount, c.Status, c.Month, c.Year,
                    c.PaymentDate, c.PaymentMode, c.Remarks))
                .ToListAsync();

        var settings = await db.Settings.FirstOrDefaultAsync();
        var dueDay = settings?.DueDay ?? 5;

        // Expected monthly maintenance is this flat's own rule-engine total (sum of active components).
        var maintenanceAmt = 0m;
        if (member is not null)
        {
            var components = await calc.LoadComponentsAsync();
            maintenanceAmt = calc.CalculateForFlat(components, MaintenanceCalculationService.ToContext(member)).Total;
        }

        var paid = payments.Where(p => p.Status == "Paid").ToList();
        var pending = payments.Where(p => p.Status != "Paid").ToList();
        var lastPaymentDate = paid.Where(p => p.PaymentDate.HasValue)
            .Select(p => p.PaymentDate).DefaultIfEmpty(null).Max();

        var nextMonth = DateTime.UtcNow.Date.AddMonths(1);
        var day = Math.Min(dueDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
        DateTime? nextDueDate = new DateTime(nextMonth.Year, nextMonth.Month, day);

        var complaints = await db.Complaints
            .Where(c => c.RaisedByUserId == CurrentUserId)
            .Select(c => c.Status)
            .ToListAsync();
        var open = complaints.Count(s => s == "Open" || s == "In Progress");
        var closed = complaints.Count(s => s == "Resolved" || s == "Closed");

        var maintenance = new MeMaintenanceSummaryDto(
            paid.Sum(p => p.Amount), paid.Count, pending.Sum(p => p.Amount), pending.Count,
            lastPaymentDate, nextDueDate, maintenanceAmt, dueDay);
        var complaintSummary = new MeComplaintSummaryDto(open, closed, complaints.Count);

        return Ok(new MeProfileDto(
            user.Id, user.Username, user.Role, user.Name, user.Email, user.Mobile,
            user.Flat, user.Floor, user.OccupancyType, user.EmergencyContact, user.ProfilePhoto,
            user.FamilyJson, user.VehiclesJson, user.NotifyPrefsJson,
            settings?.SocietyName ?? "Our Society",
            maintenance, complaintSummary, payments));
    }

    /// <summary>The resident's own advance (wallet) balance and ledger history (newest first).</summary>
    [HttpGet("advance")]
    public async Task<ActionResult<MeAdvanceDto>> GetAdvance()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized();

        Member? member = null;
        if (!string.IsNullOrWhiteSpace(user.Flat))
        {
            var flat = user.Flat!.ToLower();
            member = await db.Members.FirstOrDefaultAsync(m => m.Flat.ToLower() == flat);
        }
        if (member is null) return Ok(new MeAdvanceDto(0m, "Auto", Array.Empty<MeAdvanceEntryDto>()));

        var entries = await db.AdvanceLedger
            .Where(e => e.MemberId == member.Id)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new MeAdvanceEntryDto(e.Id, e.Date, e.Type, e.Amount, e.BalanceAfter, e.Source, e.Note))
            .ToListAsync();

        return Ok(new MeAdvanceDto(member.AdvanceBalance, member.AdvanceMode, entries));
    }

    [HttpPut]
    public async Task<IActionResult> Update(MeUpdateRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized();

        // Members may only edit their own contact/profile fields.
        // Flat, Floor, Role and Status are administered by an Admin and are never touched here.
        user.Name = request.Name;
        user.Email = request.Email;
        user.Mobile = request.Mobile;
        user.OccupancyType = request.OccupancyType;
        user.EmergencyContact = request.EmergencyContact;
        user.ProfilePhoto = request.ProfilePhoto;
        user.FamilyJson = request.FamilyJson;
        user.VehiclesJson = request.VehiclesJson;
        user.NotifyPrefsJson = request.NotifyPrefsJson;

        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "UpdateProfile", $"{user.Username} updated their profile");
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized();

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Current password is incorrect." });

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "ChangePassword", $"{user.Username} changed their password");
        return NoContent();
    }
}
