using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

/// <summary>
/// The society's stock register: what was bought, what is left, what was consumed.
/// Deliberately not an asset register — no serial numbers, warranty, depreciation or vendors
/// beyond the expense the purchase books.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(SmmsDbContext db, AuditService audit) : ControllerBase
{
    public const string StatusGood = "Good";
    public const string StatusLow = "LowStock";
    public const string StatusOut = "OutOfStock";

    /// <summary>Out of stock wins over low stock, so an empty shelf is never reported as merely low.</summary>
    public static string StatusOf(decimal currentStock, decimal minimumStockLevel) =>
        currentStock <= 0 ? StatusOut
        : currentStock <= minimumStockLevel ? StatusLow
        : StatusGood;

    private static InventoryItemDto ToDto(InventoryItem i) => new(
        i.Id, i.Name, i.Category, i.Unit, i.MinimumStockLevel, i.CurrentStock,
        StatusOf(i.CurrentStock, i.MinimumStockLevel), i.IsActive);

    private static InventoryMovementDto ToDto(InventoryMovement m) => new(
        m.Id, m.MovementDate, m.MovementType.ToString(), m.Quantity, m.Reason,
        m.ExpenseId, m.CreatedBy, m.CreatedOn);

    // ── Items ─────────────────────────────────────────────────────────────────────────────

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<InventoryItemDto>>> GetItems([FromQuery] bool includeInactive = false)
    {
        if (!User.CanView(PermissionModules.Inventory)) return Forbid();
        var query = db.InventoryItems.AsNoTracking();
        if (!includeInactive) query = query.Where(i => i.IsActive);
        var items = await query.OrderBy(i => i.Name).ToListAsync();
        return Ok(items.Select(ToDto));
    }

    [HttpGet("items/{id:int}")]
    public async Task<ActionResult<InventoryItemDetailDto>> GetItem(int id)
    {
        if (!User.CanView(PermissionModules.Inventory)) return Forbid();
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound();

        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.InventoryItemId == id)
            .OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id)
            .ToListAsync();

        // Adjustments are corrections, not trade, so they are excluded from both totals; they
        // still move CurrentStock, which is why the two totals need not reconcile to it.
        var purchased = movements.Where(m => m.MovementType == InventoryMovementType.Purchase).Sum(m => m.Quantity);
        var used = movements.Where(m => m.MovementType == InventoryMovementType.Usage).Sum(m => -m.Quantity);

        return Ok(new InventoryItemDetailDto(ToDto(item), purchased, used, movements.Select(ToDto)));
    }

    [HttpGet("items/{id:int}/history")]
    public async Task<ActionResult<IEnumerable<InventoryMovementDto>>> GetHistory(int id)
    {
        if (!User.CanView(PermissionModules.Inventory)) return Forbid();
        if (!await db.InventoryItems.AnyAsync(i => i.Id == id)) return NotFound();

        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.InventoryItemId == id)
            .OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id)
            .ToListAsync();
        return Ok(movements.Select(ToDto));
    }

    /// <summary>Categories already in use, so the UI can offer them without a lookup table.</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        if (!User.CanView(PermissionModules.Inventory)) return Forbid();
        return Ok(await db.InventoryItems.AsNoTracking()
            .Select(i => i.Category).Distinct().OrderBy(c => c).ToListAsync());
    }

    [HttpPost("items")]
    public async Task<ActionResult<InventoryItemDto>> CreateItem(InventoryItemUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Inventory)) return Forbid();

        var name = request.Name.Trim();
        if (await db.InventoryItems.AnyAsync(i => i.Name == name && i.IsActive))
            return Conflict($"An item named '{name}' already exists.");

        var item = new InventoryItem
        {
            Name = name,
            Category = request.Category.Trim(),
            Unit = request.Unit.Trim(),
            MinimumStockLevel = request.MinimumStockLevel,
            CurrentStock = 0m,
            IsActive = request.IsActive
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        await audit.LogAsync("Inventory", "Add", $"Added inventory item: {item.Name} ({item.Category})");
        return CreatedAtAction(nameof(GetItem), new { id = item.Id }, ToDto(item));
    }

    /// <summary>Edits the item's description only. Stock is never changed here — that would be a
    /// silent correction with no audit trail; use Adjust instead.</summary>
    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, InventoryItemUpsertRequest request)
    {
        if (!User.CanEdit(PermissionModules.Inventory)) return Forbid();
        var item = await db.InventoryItems.FindAsync(id);
        if (item is null) return NotFound();

        var name = request.Name.Trim();
        if (await db.InventoryItems.AnyAsync(i => i.Name == name && i.IsActive && i.Id != id))
            return Conflict($"An item named '{name}' already exists.");

        item.Name = name;
        item.Category = request.Category.Trim();
        item.Unit = request.Unit.Trim();
        item.MinimumStockLevel = request.MinimumStockLevel;
        item.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        await audit.LogAsync("Inventory", "Update", $"Updated inventory item id {id} ({item.Name})");
        return NoContent();
    }

    // ── Stock movements ───────────────────────────────────────────────────────────────────

    [HttpPost("stock-in")]
    public async Task<ActionResult<InventoryMovementDto>> StockIn(StockInRequest request)
    {
        if (!User.CanEdit(PermissionModules.Inventory)) return Forbid();

        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == request.ItemId);
        if (item is null) return NotFound("Item not found.");
        if (!item.IsActive) return BadRequest("This item is inactive. Reactivate it before adding stock.");

        if (request.CreateExpense && (request.PurchaseAmount is null || request.PurchaseAmount <= 0))
            return BadRequest("Purchase amount must be greater than zero to book an expense.");

        var date = request.PurchaseDate ?? DateTime.UtcNow.Date;
        var movement = new InventoryMovement
        {
            InventoryItemId = item.Id,
            MovementType = InventoryMovementType.Purchase,
            Quantity = request.Quantity,
            Reason = request.Reason,
            MovementDate = date
        };

        if (request.CreateExpense)
        {
            // Navigation property, not an id: EF stamps ExpenseId when both rows commit together.
            movement.Expense = new Expense
            {
                ExpenseDate = date,
                Category = string.IsNullOrWhiteSpace(request.ExpenseCategory) ? item.Category : request.ExpenseCategory.Trim(),
                Description = $"{item.Name} \u2014 {request.Quantity:0.##} {item.Unit}",
                Vendor = request.Vendor,
                Amount = request.PurchaseAmount!.Value,
                PaymentMode = request.PaymentMode,
                Month = date.Month,
                Year = date.Year,
                Remarks = string.IsNullOrWhiteSpace(request.Reason) ? "Inventory purchase" : $"Inventory purchase \u2014 {request.Reason}"
            };
        }

        item.CurrentStock += request.Quantity;
        db.InventoryMovements.Add(movement);

        // One SaveChanges: the movement, the running balance and any expense commit together or
        // not at all. RowVersion makes a concurrent write fail rather than silently overwrite.
        if (!await TrySaveAsync()) return Conflict(ConcurrencyMessage);

        await audit.LogAsync("Inventory", "StockIn",
            $"{item.Name}: +{request.Quantity:0.##} {item.Unit}"
            + (movement.ExpenseId is int eid ? $", expense #{eid} for {request.PurchaseAmount:0.##}" : ", no expense"));
        return Ok(ToDto(movement));
    }

    [HttpPost("stock-out")]
    public async Task<ActionResult<InventoryMovementDto>> StockOut(StockOutRequest request)
    {
        if (!User.CanEdit(PermissionModules.Inventory)) return Forbid();

        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == request.ItemId);
        if (item is null) return NotFound("Item not found.");

        // Checked here, not just via [Required]: on positional records the attribute binds to the
        // constructor parameter, so a blank string must be refused explicitly.
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("A reason is required so the stock change is traceable.");

        if (request.Quantity > item.CurrentStock)
            return BadRequest($"Insufficient stock. Available quantity: {item.CurrentStock:0.##}.");

        var movement = new InventoryMovement
        {
            InventoryItemId = item.Id,
            MovementType = InventoryMovementType.Usage,
            Quantity = -request.Quantity,
            Reason = request.Reason,
            MovementDate = request.MovementDate ?? DateTime.UtcNow.Date
        };
        item.CurrentStock -= request.Quantity;
        db.InventoryMovements.Add(movement);

        if (!await TrySaveAsync()) return Conflict(ConcurrencyMessage);

        await audit.LogAsync("Inventory", "StockOut",
            $"{item.Name}: -{request.Quantity:0.##} {item.Unit} ({request.Reason})");
        return Ok(ToDto(movement));
    }

    /// <summary>Corrects the book balance after a physical count. Admin only: a correction can
    /// create or destroy stock without any money moving, so it is not part of the Edit grant.</summary>
    [HttpPost("adjust")]
    public async Task<ActionResult<InventoryMovementDto>> Adjust(StockAdjustRequest request)
    {
        if (!User.IsInRole(Roles.Admin)) return Forbid();

        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == request.ItemId);
        if (item is null) return NotFound("Item not found.");
        if (request.Quantity == 0) return BadRequest("Adjustment quantity cannot be zero.");

        // Every adjustment must explain itself: it moves stock with no money and no document
        // behind it, so the reason is the only audit evidence there will ever be.
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("A reason is required for every stock adjustment.");

        var newStock = item.CurrentStock + request.Quantity;
        if (newStock < 0)
            return BadRequest($"Adjustment would make stock negative. Available quantity: {item.CurrentStock:0.##}.");

        var movement = new InventoryMovement
        {
            InventoryItemId = item.Id,
            MovementType = InventoryMovementType.Adjustment,
            Quantity = request.Quantity,
            Reason = request.Reason,
            MovementDate = request.MovementDate ?? DateTime.UtcNow.Date
        };
        item.CurrentStock = newStock;
        db.InventoryMovements.Add(movement);

        if (!await TrySaveAsync()) return Conflict(ConcurrencyMessage);

        await audit.LogAsync("Inventory", "Adjust",
            $"{item.Name}: {request.Quantity:+0.##;-0.##} {item.Unit} ({request.Reason})");
        return Ok(ToDto(movement));
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────────────────

    [HttpGet("dashboard")]
    public async Task<ActionResult<InventoryDashboardDto>> Dashboard()
    {
        if (!User.CanView(PermissionModules.Inventory)) return Forbid();

        var items = await db.InventoryItems.AsNoTracking().Where(i => i.IsActive).ToListAsync();
        var lowStock = items
            .Where(i => StatusOf(i.CurrentStock, i.MinimumStockLevel) != StatusGood)
            .OrderBy(i => i.CurrentStock).ThenBy(i => i.Name)
            .Select(ToDto).ToList();

        var recent = await db.InventoryMovements.AsNoTracking()
            .Include(m => m.Item)
            .OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id)
            .Take(10)
            .ToListAsync();

        return Ok(new InventoryDashboardDto(
            items.Count,
            items.Count(i => StatusOf(i.CurrentStock, i.MinimumStockLevel) == StatusLow),
            items.Count(i => StatusOf(i.CurrentStock, i.MinimumStockLevel) == StatusOut),
            await db.InventoryMovements.CountAsync(),
            lowStock,
            recent.Select(m => new InventoryRecentMovementDto(
                m.Id, m.InventoryItemId, m.Item?.Name ?? "(item)", m.MovementDate,
                m.MovementType.ToString(), m.Quantity, m.Reason))));
    }

    private const string ConcurrencyMessage =
        "Someone else changed this item's stock at the same time. Reload and try again.";

    /// <summary>Commits the movement and its running balance together. Returns false when another
    /// writer got there first, which is what stops two simultaneous stock-outs from both passing
    /// the availability check and driving the balance negative.</summary>
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
