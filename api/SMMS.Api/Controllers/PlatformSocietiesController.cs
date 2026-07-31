using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Dtos.Control;
using SMMS.Api.Models.Control;
using SMMS.Api.Services;
using SMMS.Api.Services.Control;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/platform/societies")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformSocietiesController(
    ControlDbContext controlDb,
    DbTenantStore tenantStore,
    IServiceScopeFactory scopeFactory,
    TenantProvisioningService provisioning,
    TokenService tokenService,
    PlatformAuditService audit,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SocietyDto>>> List()
    {
        var societies = await controlDb.Societies.AsNoTracking()
            .OrderBy(s => s.DisplayName)
            .ToListAsync();

        return Ok(societies.Select(ToDto));
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<SocietyDto>> Get(string key)
    {
        var society = await controlDb.Societies.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        return society is null ? NotFound() : Ok(ToDto(society));
    }

    [HttpPost]
    public async Task<ActionResult<SocietyDto>> Onboard(OnboardSocietyRequest req)
    {
        try
        {
            var society = await provisioning.ProvisionAsync(
                req.Key, req.DisplayName, req.Plan,
                req.AdminUsername, req.AdminPassword, req.FlatCount, req.ExpiryDate);

            await audit.LogAsync("SocietyOnboard", "Society", society.Key,
                $"Onboarded '{society.DisplayName}' (db {society.DbName}).");

            return Created($"/api/platform/societies/{society.Key}", ToDto(society));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Public self-service registration. Records a Pending society (no database yet) for a
    /// super-admin to approve. Anonymous — all input is untrusted and validated in the service.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterSocietyRequest req)
    {
        try
        {
            var society = await provisioning.RegisterPendingAsync(
                req.Key, req.DisplayName, req.AdminName, req.AdminEmail,
                req.Phone, req.Address, req.Plan, req.FlatCount);

            await audit.LogAsync("SocietyRegister", "Society", society.Key,
                $"Self-registration for '{society.DisplayName}' (pending approval).");

            return Ok(new { message = "Registration received. You'll be notified once it's approved." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Pending registrations awaiting approval (contact details included via the DTO).</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<SocietyDto>>> PendingList()
    {
        var pending = await controlDb.Societies.AsNoTracking()
            .Where(s => s.Status == SocietyStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        return Ok(pending.Select(ToDto));
    }

    /// <summary>Approves a pending registration: provisions + seeds its database and returns the
    /// initial admin credentials (shown once) plus the society's portal URL.</summary>
    [HttpPost("{key}/approve")]
    public async Task<ActionResult<ApproveSocietyResponse>> Approve(string key, ApproveSocietyRequest req)
    {
        try
        {
            var (society, username, password) = await provisioning.ApproveAsync(
                key, req.AdminUsername, req.AdminPassword, req.ExpiryDate);

            await audit.LogAsync("SocietyApprove", "Society", society.Key,
                $"Approved '{society.DisplayName}' (db {society.DbName}); admin '{username}'.");

            return Ok(new ApproveSocietyResponse(ToDto(society), username, password, BuildPortalUrl(society.Key)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Rejects (deletes) a pending registration. Only allowed while still Pending.</summary>
    [HttpPost("{key}/reject")]
    public async Task<IActionResult> Reject(string key)
    {
        var society = await controlDb.Societies.FirstOrDefaultAsync(s => s.Key == key);
        if (society is null) return NotFound();

        if (!string.Equals(society.Status, SocietyStatus.Pending, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only a pending registration can be rejected." });

        controlDb.Societies.Remove(society);
        await controlDb.SaveChangesAsync();

        await audit.LogAsync("SocietyReject", "Society", key,
            $"Rejected pending registration '{society.DisplayName}'.");
        return Ok(new { message = "Registration rejected." });
    }

    private string BuildPortalUrl(string key)
    {
        var baseDomain = config["ControlPlane:TenantBaseDomain"] ?? "ssms.yuvaansoft.shop";
        return $"https://{key}.{baseDomain}";
    }

    [HttpPost("{key}/suspend")]
    public Task<IActionResult> Suspend(string key) => SetStatus(key, SocietyStatus.Suspended);

    [HttpPost("{key}/activate")]
    public Task<IActionResult> Activate(string key) => SetStatus(key, SocietyStatus.Active);

    /// <summary>Renew/extend a society's subscription: sets a new expiry date (and optional plan) and,
    /// if it was Expired or Suspended, restores it to Active. Refreshes the tenant registry so access
    /// is granted again immediately.</summary>
    [HttpPost("{key}/renew")]
    public async Task<IActionResult> Renew(string key, RenewSocietyRequest req)
    {
        if (req.ExpiryDate.Date < DateTime.UtcNow.Date)
            return BadRequest(new { message = "New expiry date must be today or in the future." });

        var society = await controlDb.Societies.FirstOrDefaultAsync(s => s.Key == key);
        if (society is null) return NotFound();

        society.ExpiryDate = req.ExpiryDate.Date;
        if (!string.IsNullOrWhiteSpace(req.Plan))
            society.Plan = req.Plan;

        // Renewing a lapsed (Expired) or paused (Suspended) society brings it back online.
        if (string.Equals(society.Status, SocietyStatus.Expired, StringComparison.OrdinalIgnoreCase)
            || string.Equals(society.Status, SocietyStatus.Suspended, StringComparison.OrdinalIgnoreCase))
            society.Status = SocietyStatus.Active;

        await controlDb.SaveChangesAsync();
        tenantStore.Reload();

        await audit.LogAsync("SocietyRenew", "Society", key,
            $"Subscription renewed to {society.ExpiryDate:yyyy-MM-dd}" +
            (string.IsNullOrWhiteSpace(req.Plan) ? "." : $" on plan '{society.Plan}'."));
        return Ok(ToDto(society));
    }

    private async Task<IActionResult> SetStatus(string key, string status)
    {
        var society = await controlDb.Societies.FirstOrDefaultAsync(s => s.Key == key);
        if (society is null) return NotFound();

        society.Status = status;
        await controlDb.SaveChangesAsync();
        tenantStore.Reload();

        await audit.LogAsync($"Society{status}", "Society", key, $"Status set to {status}.");
        return Ok(ToDto(society));
    }

    [HttpPost("{key}/impersonate")]
    public async Task<ActionResult<ImpersonateResponse>> Impersonate(string key)
    {
        var society = await controlDb.Societies.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);
        if (society is null) return NotFound();

        if (string.Equals(society.Status, SocietyStatus.Suspended, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Cannot impersonate a suspended society. Activate it first." });

        if (string.Equals(society.Status, SocietyStatus.Expired, StringComparison.OrdinalIgnoreCase)
            || (society.ExpiryDate is { } exp && exp.Date < DateTime.UtcNow.Date))
            return BadRequest(new { message = "Cannot impersonate a society with an expired subscription. Renew it first." });

        var tenant = tenantStore.GetByKey(key);
        if (tenant is null) return NotFound();

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
        var tenantDb = scope.ServiceProvider.GetRequiredService<SmmsDbContext>();

        var admin = await tenantDb.Users
            .FirstOrDefaultAsync(u => u.Role == "Admin" && u.Status == "Active");
        if (admin is null)
            return NotFound(new { message = "No active admin found in this society." });

        var superAdmin = User.Identity?.Name ?? "superadmin";
        // Short-lived, impersonation-tagged token so society audit trails can attribute the session.
        var token = tokenService.CreateToken(admin, key, impersonatedBy: superAdmin, expiryMinutesOverride: 30);

        await audit.LogAsync("Impersonate", "Society", key,
            $"Logged in as society admin '{admin.Username}'.", impersonatedTenant: key);

        return Ok(new ImpersonateResponse(token, key, admin.Username));
    }

    private SocietyDto ToDto(Society s)
    {
        var tenant = tenantStore.GetByKey(s.Key);
        var memberCount = tenant is not null ? TenantMetrics.CountMembers(scopeFactory, tenant) : 0;
        return SocietyMapping.ToDto(s, memberCount);
    }
}
