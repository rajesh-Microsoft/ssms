using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

/// <summary>One-time admin data-migration import (member login accounts). Idempotent — safe to
/// re-run: usernames that already exist, or that repeat within the payload, are skipped.
/// Uses the mobile number as the login username (mirrors the old phone-number login).</summary>
[ApiController]
[Route("api/admin/import")]
[Authorize(Roles = "Admin")]
public class ImportController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    public record ImportMemberDto(
        string FullName, string MobileNumber, string EmailAddress,
        string FlatNumber, string Password);

    [HttpPost("members")]
    public async Task<IActionResult> ImportMembers(List<ImportMemberDto> members)
    {
        var hasher = new PasswordHasher<User>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, skipped = 0;
        var report = new List<object>();

        foreach (var m in members)
        {
            var username = m.MobileNumber?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(m.Password))
            { skipped++; report.Add(new { m.FlatNumber, result = "skipped: missing mobile/password" }); continue; }

            if (!seen.Add(username))
            { skipped++; report.Add(new { m.FlatNumber, username, result = "skipped: duplicate in batch" }); continue; }

            if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            { skipped++; report.Add(new { m.FlatNumber, username, result = "skipped: username already exists" }); continue; }

            var flat = m.FlatNumber?.Trim();
            var user = new User
            {
                Username = username,
                Role = "Member",
                Status = "Active",
                Name = m.FullName?.Trim(),
                Email = m.EmailAddress?.Trim(),
                Mobile = username,
                Flat = flat,
                Floor = !string.IsNullOrEmpty(flat) ? flat[..1] : null
            };
            user.PasswordHash = hasher.HashPassword(user, m.Password);
            db.Users.Add(user);
            created++;
            report.Add(new { m.FlatNumber, username, result = "created" });
        }

        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Import", $"Bulk-imported {created} member login(s); skipped {skipped}.");
        return Ok(new { created, skipped, report });
    }
}
