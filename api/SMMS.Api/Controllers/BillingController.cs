using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMMS.Api.Dtos;
using SMMS.Api.Services.Billing;

namespace SMMS.Api.Controllers;

/// <summary>Admin-only maintenance billing: manually generate the monthly maintenance invoices
/// (one Unpaid charge per active flat). Idempotent — safe to re-run.</summary>
[ApiController]
[Route("api/admin/billing")]
[Authorize(Roles = "Admin")]
public class BillingController(BillingService billing) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<BillingRunResult>> Generate(GenerateBillingRequest request)
    {
        if (request.Month is < 1 or > 12) return BadRequest(new { message = "Month must be 1-12." });
        if (request.Year is < 2000 or > 2100) return BadRequest(new { message = "Year is out of range." });
        if (request.Amount is < 0) return BadRequest(new { message = "Amount cannot be negative." });

        var result = await billing.GenerateForMonthAsync(request.Month, request.Year, request.Amount);
        return Ok(result);
    }
}
