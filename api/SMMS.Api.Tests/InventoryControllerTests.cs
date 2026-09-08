using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Controllers;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;
using Xunit;

namespace SMMS.Api.Tests;

/// <summary>
/// Covers the stock register's rules. Each test gets its own SQLite database, which stands in for
/// one society: SMMS gives every society a separate database, so "another society" is literally a
/// different connection rather than a different row filter.
/// </summary>
public class InventoryControllerTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    private SmmsDbContext NewSociety()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);

        var options = new DbContextOptionsBuilder<SmmsDbContext>().UseSqlite(connection).Options;
        var db = new SmmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static InventoryController ControllerFor(SmmsDbContext db, ClaimsPrincipal user)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        return new InventoryController(db, new AuditService(db, accessor))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    /// <summary>A plain resident. PermissionHelper.Parse defaults every module to View, which the
    /// token mirrors as a perm claim, so this is what a normal member actually carries.</summary>
    private static ClaimsPrincipal Member() =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "resident"),
            new Claim(ClaimTypes.Role, Roles.Member),
            new Claim($"perm:{PermissionModules.Inventory}", "View")], "test"));

    /// <summary>A committee member granted Edit but not Admin — the treasurer case.</summary>
    private static ClaimsPrincipal Editor() =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "treasurer"),
            new Claim(ClaimTypes.Role, Roles.Member),
            new Claim($"perm:{PermissionModules.Inventory}", "Edit")], "test"));

    private static InventoryItemUpsertRequest NewItem(string name = "LED Bulb", decimal min = 5) =>
        new(name, "Electrical", "Nos", min, true);

    private static async Task<int> SeedItemAsync(InventoryController c, string name = "LED Bulb", decimal min = 5)
    {
        var created = await c.CreateItem(NewItem(name, min));
        return ((InventoryItemDto)((CreatedAtActionResult)created.Result!).Value!).Id;
    }

    private static async Task<decimal> StockOf(SmmsDbContext db, int itemId) =>
        (await db.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == itemId)).CurrentStock;

    public void Dispose()
    {
        foreach (var c in _connections) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Item master ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateItem_StoresItemWithZeroStock()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());

        var result = await controller.CreateItem(NewItem());

        var dto = Assert.IsType<InventoryItemDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("LED Bulb", dto.Name);
        Assert.Equal(0m, dto.CurrentStock);
        Assert.Equal(InventoryController.StatusOut, dto.Status);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task CreateItem_RejectsDuplicateActiveName()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        await SeedItemAsync(controller);

        var duplicate = await controller.CreateItem(NewItem());

        Assert.IsType<ConflictObjectResult>(duplicate.Result);
        Assert.Equal(1, await db.InventoryItems.CountAsync());
    }

    // ── Stock in ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StockIn_IncreasesStockAndRecordsMovement()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        await controller.StockIn(new StockInRequest(itemId, 20m, null, null, "Blocks A and B", false, null, null, null));

        Assert.Equal(20m, await StockOf(db, itemId));
        var movement = await db.InventoryMovements.SingleAsync();
        Assert.Equal(InventoryMovementType.Purchase, movement.MovementType);
        Assert.Equal(20m, movement.Quantity);
        Assert.Null(movement.ExpenseId);
    }

    [Fact]
    public async Task StockIn_WithCreateExpense_BooksExpenseAndLinksIt()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        await controller.StockIn(new StockInRequest(
            itemId, 20m, new DateTime(2026, 9, 8), 4000m, "ABC Electrical", true, "ABC Electrical", "Cash", null));

        var movement = await db.InventoryMovements.SingleAsync();
        Assert.NotNull(movement.ExpenseId);

        var expense = await db.Expenses.SingleAsync();
        Assert.Equal(4000m, expense.Amount);
        Assert.Equal("Electrical", expense.Category);
        Assert.Equal(9, expense.Month);
        Assert.Equal(2026, expense.Year);
        Assert.Equal(movement.ExpenseId, expense.Id);
    }

    [Fact]
    public async Task StockIn_WithCreateExpense_RequiresPositiveAmount()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        var result = await controller.StockIn(new StockInRequest(itemId, 20m, null, 0m, null, true, null, null, null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0m, await StockOf(db, itemId));
        Assert.False(await db.Expenses.AnyAsync());
    }

    [Fact]
    public async Task StockIn_WithoutExpense_AllowsMissingPurchaseAmount()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        await controller.StockIn(new StockInRequest(itemId, 12m, null, null, null, false, null, null, null));

        Assert.Equal(12m, await StockOf(db, itemId));
        Assert.False(await db.Expenses.AnyAsync());
    }

    // ── Stock out ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StockOut_DecreasesStockAndStoresNegativeQuantity()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);
        await controller.StockIn(new StockInRequest(itemId, 20m, null, null, null, false, null, null, null));

        await controller.StockOut(new StockOutRequest(itemId, 2m, null, "Block A corridor"));

        Assert.Equal(18m, await StockOf(db, itemId));
        var usage = await db.InventoryMovements.SingleAsync(m => m.MovementType == InventoryMovementType.Usage);
        Assert.Equal(-2m, usage.Quantity);
    }

    [Fact]
    public async Task StockOut_NeverBooksASecondExpense()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);
        await controller.StockIn(new StockInRequest(itemId, 20m, null, 4000m, null, true, null, "Cash", null));

        await controller.StockOut(new StockOutRequest(itemId, 2m, null, "Block A corridor"));

        Assert.Equal(1, await db.Expenses.CountAsync());
        var usage = await db.InventoryMovements.SingleAsync(m => m.MovementType == InventoryMovementType.Usage);
        Assert.Null(usage.ExpenseId);
    }

    [Fact]
    public async Task StockOut_RejectsMoreThanAvailableAndLeavesStockUntouched()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);
        await controller.StockIn(new StockInRequest(itemId, 2m, null, null, null, false, null, null, null));

        var result = await controller.StockOut(new StockOutRequest(itemId, 5m, null, "Too many"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Insufficient stock. Available quantity: 2.", bad.Value);
        Assert.Equal(2m, await StockOf(db, itemId));
        Assert.False(await db.InventoryMovements.AnyAsync(m => m.MovementType == InventoryMovementType.Usage));
    }

    [Fact]
    public async Task StockOut_FromEmptyStockIsRejected()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        var result = await controller.StockOut(new StockOutRequest(itemId, 1m, null, "Nothing left"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Insufficient stock. Available quantity: 0.", bad.Value);
    }

    // ── Adjustment ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Adjust_AppliesSignedCorrection()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);
        await controller.StockIn(new StockInRequest(itemId, 20m, null, null, null, false, null, null, null));

        await controller.Adjust(new StockAdjustRequest(itemId, -2m, null, "2 damaged bulbs found"));

        Assert.Equal(18m, await StockOf(db, itemId));
        var adjustment = await db.InventoryMovements.SingleAsync(m => m.MovementType == InventoryMovementType.Adjustment);
        Assert.Equal(-2m, adjustment.Quantity);
        Assert.Equal("2 damaged bulbs found", adjustment.Reason);
    }

    [Fact]
    public async Task Adjust_CannotDriveStockNegative()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        var result = await controller.Adjust(new StockAdjustRequest(itemId, -1m, null, "Correction"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0m, await StockOf(db, itemId));
    }

    [Fact]
    public async Task Adjust_RejectsZeroQuantity()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);

        var result = await controller.Adjust(new StockAdjustRequest(itemId, 0m, null, "Nothing to correct"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>A stock change with no explanation leaves nothing to audit, so both Stock Out and
    /// Adjust must refuse one. Asserted through the action rather than the attribute: on positional
    /// records [Required] binds to the constructor parameter, so the guard has to be real code.</summary>
    [Fact]
    public async Task StockOutAndAdjust_RequireAReason()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller);
        await controller.StockIn(new StockInRequest(itemId, 20m, null, null, null, false, null, null, null));

        foreach (var blank in new[] { "", "   " })
        {
            Assert.IsType<BadRequestObjectResult>(
                (await controller.StockOut(new StockOutRequest(itemId, 1m, null, blank))).Result);
            Assert.IsType<BadRequestObjectResult>(
                (await controller.Adjust(new StockAdjustRequest(itemId, 1m, null, blank))).Result);
        }

        Assert.Equal(20m, await StockOf(db, itemId));
        Assert.Equal(1, await db.InventoryMovements.CountAsync());
    }

    // ── Status ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(18, 5, InventoryController.StatusGood)]
    [InlineData(6, 5, InventoryController.StatusGood)]
    [InlineData(5, 5, InventoryController.StatusLow)]   // at the minimum counts as low
    [InlineData(4, 5, InventoryController.StatusLow)]
    [InlineData(0, 5, InventoryController.StatusOut)]   // empty beats low
    [InlineData(0, 0, InventoryController.StatusOut)]
    public void StatusOf_ClassifiesStockLevels(decimal current, decimal minimum, string expected) =>
        Assert.Equal(expected, InventoryController.StatusOf(current, minimum));

    // ── Authorization ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Member_CanViewButCannotModify()
    {
        using var db = NewSociety();
        var itemId = await SeedItemAsync(ControllerFor(db, Admin()));
        var member = ControllerFor(db, Member());

        Assert.IsType<OkObjectResult>((await member.GetItems()).Result);

        Assert.IsType<ForbidResult>((await member.CreateItem(NewItem("Phenyl"))).Result);
        Assert.IsType<ForbidResult>(await member.UpdateItem(itemId, NewItem()));
        Assert.IsType<ForbidResult>((await member.StockIn(new StockInRequest(itemId, 5m, null, null, null, false, null, null, null))).Result);
        Assert.IsType<ForbidResult>((await member.StockOut(new StockOutRequest(itemId, 1m, null, "no"))).Result);
        Assert.IsType<ForbidResult>((await member.Adjust(new StockAdjustRequest(itemId, 1m, null, "no"))).Result);

        Assert.Equal(0m, await StockOf(db, itemId));
        Assert.Equal(1, await db.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task Admin_CanPerformEveryOperation()
    {
        using var db = NewSociety();
        var admin = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(admin);

        Assert.IsType<OkObjectResult>((await admin.StockIn(new StockInRequest(itemId, 20m, null, null, null, false, null, null, null))).Result);
        Assert.IsType<OkObjectResult>((await admin.StockOut(new StockOutRequest(itemId, 2m, null, "Corridor"))).Result);
        Assert.IsType<OkObjectResult>((await admin.Adjust(new StockAdjustRequest(itemId, 1m, null, "Recount"))).Result);
        Assert.IsType<NoContentResult>(await admin.UpdateItem(itemId, NewItem(min: 10)));

        Assert.Equal(19m, await StockOf(db, itemId));
    }

    /// <summary>Adjustment is Admin-only: it can create or destroy stock with no money moving, so
    /// it deliberately sits outside the Edit grant that Stock In/Out use.</summary>
    [Fact]
    public async Task EditorWithoutAdmin_CanMoveStockButCannotAdjust()
    {
        using var db = NewSociety();
        var itemId = await SeedItemAsync(ControllerFor(db, Admin()));
        var editor = ControllerFor(db, Editor());

        Assert.IsType<OkObjectResult>((await editor.StockIn(new StockInRequest(itemId, 10m, null, null, null, false, null, null, null))).Result);
        Assert.IsType<OkObjectResult>((await editor.StockOut(new StockOutRequest(itemId, 3m, null, "Used"))).Result);
        Assert.IsType<ForbidResult>((await editor.Adjust(new StockAdjustRequest(itemId, 5m, null, "Correction"))).Result);

        Assert.Equal(7m, await StockOf(db, itemId));
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────────────────

    /// <summary>Societies do not share a table with a discriminator column — each one has its own
    /// database. This proves the boundary: the same admin, querying a second society, sees nothing
    /// from the first, and no request field could change that.</summary>
    [Fact]
    public async Task OneSocietyNeverSeesAnotherSocietysStock()
    {
        using var societyA = NewSociety();
        using var societyB = NewSociety();

        var adminA = ControllerFor(societyA, Admin());
        var itemId = await SeedItemAsync(adminA, "LED Bulb");
        await adminA.StockIn(new StockInRequest(itemId, 20m, null, null, null, false, null, null, null));

        var itemsInB = Assert.IsType<OkObjectResult>((await ControllerFor(societyB, Admin()).GetItems()).Result);
        Assert.Empty((IEnumerable<InventoryItemDto>)itemsInB.Value!);

        // The id from society A does not resolve inside society B.
        Assert.IsType<NotFoundResult>((await ControllerFor(societyB, Admin()).GetItem(itemId)).Result);
        Assert.Equal(0, await societyB.InventoryMovements.CountAsync());
        Assert.Equal(1, await societyA.InventoryMovements.CountAsync());
    }

    // ── Acceptance scenario from the specification ────────────────────────────────────────

    [Fact]
    public async Task AcceptanceScenario_LedBulbLifecycle()
    {
        using var db = NewSociety();
        var controller = ControllerFor(db, Admin());
        var itemId = await SeedItemAsync(controller, "LED Bulb", min: 5);

        await controller.StockIn(new StockInRequest(itemId, 20m, new DateTime(2026, 9, 8), 4000m, null, true, null, "Cash", null));
        Assert.Equal(20m, await StockOf(db, itemId));
        Assert.Equal(4000m, (await db.Expenses.SingleAsync()).Amount);

        await controller.StockOut(new StockOutRequest(itemId, 2m, null, "Block A corridor replacement"));
        Assert.Equal(18m, await StockOf(db, itemId));

        await controller.StockOut(new StockOutRequest(itemId, 14m, null, "Block B replacement"));
        Assert.Equal(4m, await StockOf(db, itemId));
        Assert.Equal(InventoryController.StatusLow, InventoryController.StatusOf(4m, 5m));

        await controller.StockOut(new StockOutRequest(itemId, 4m, null, "Remaining corridors"));
        Assert.Equal(0m, await StockOf(db, itemId));
        Assert.Equal(InventoryController.StatusOut, InventoryController.StatusOf(0m, 5m));

        var rejected = await controller.StockOut(new StockOutRequest(itemId, 1m, null, "One more"));
        Assert.Equal("Insufficient stock. Available quantity: 0.",
            Assert.IsType<BadRequestObjectResult>(rejected.Result).Value);

        await controller.Adjust(new StockAdjustRequest(itemId, 10m, null, "Opening stock correction"));
        Assert.Equal(10m, await StockOf(db, itemId));

        // History stays complete: nothing is ever edited or deleted, only appended.
        var history = await db.InventoryMovements.Where(m => m.InventoryItemId == itemId).ToListAsync();
        Assert.Equal(5, history.Count);
        Assert.Equal(1, history.Count(m => m.MovementType == InventoryMovementType.Purchase));
        Assert.Equal(3, history.Count(m => m.MovementType == InventoryMovementType.Usage));
        Assert.Equal(1, history.Count(m => m.MovementType == InventoryMovementType.Adjustment));
        Assert.Equal(1, await db.Expenses.CountAsync());
    }
}
