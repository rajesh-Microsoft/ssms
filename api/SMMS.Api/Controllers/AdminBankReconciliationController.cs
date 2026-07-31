using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMMS.Api.Dtos;
using SMMS.Api.Services.Payments;

namespace SMMS.Api.Controllers;

/// <summary>
/// Admin-only bank-statement reconciliation: upload a CSV export of incoming credits, auto-match
/// them to pending payment proofs by UTR/amount, and confirm or ignore the remaining suggestions.
/// Confirming a match approves the linked proof and marks its charge Paid.
/// </summary>
[ApiController]
[Route("api/admin/reconciliation")]
[Authorize(Roles = "Admin")]
public class AdminBankReconciliationController(BankReconciliationService recon) : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    [HttpGet("summary")]
    public async Task<ActionResult<ReconciliationSummaryDto>> Summary(CancellationToken ct)
        => Ok(await recon.SummaryAsync(ct));

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<BankTransactionDto>>> List([FromQuery] string? status, CancellationToken ct)
        => Ok(await recon.ListAsync(status, ct));

    [HttpPost("import")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Please choose a bank statement CSV file to import." });
        if (file.Length > MaxUploadBytes)
            return BadRequest(new { message = "File is too large (max 5 MB)." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await recon.ImportCsvAsync(stream, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmMatchRequest request, CancellationToken ct)
    {
        var ok = await recon.ConfirmAsync(request.BankTransactionId, request.PaymentProofId, ct);
        return ok ? NoContent() : BadRequest(new { message = "Could not confirm the match (already matched or not found)." });
    }

    [HttpPost("transactions/{id:int}/ignore")]
    public async Task<IActionResult> Ignore(int id, CancellationToken ct)
    {
        var ok = await recon.IgnoreAsync(id, ct);
        return ok ? NoContent() : BadRequest(new { message = "Could not ignore the transaction (already matched or not found)." });
    }
}
