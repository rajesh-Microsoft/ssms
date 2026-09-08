using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record InventoryItemDto(
    int Id,
    string Name,
    string Category,
    string Unit,
    decimal MinimumStockLevel,
    decimal CurrentStock,
    string Status,
    bool IsActive);

/// <summary>One line of the stock register. <paramref name="Quantity"/> is signed:
/// +20 received, -2 consumed.</summary>
public record InventoryMovementDto(
    int Id,
    DateTime MovementDate,
    string MovementType,
    decimal Quantity,
    string? Reason,
    int? ExpenseId,
    string? CreatedBy,
    DateTime CreatedOn);

public record InventoryItemDetailDto(
    InventoryItemDto Item,
    decimal TotalPurchased,
    decimal TotalUsed,
    IEnumerable<InventoryMovementDto> History);

public record InventoryDashboardDto(
    int TotalItems,
    int LowStockItems,
    int OutOfStockItems,
    int TotalMovements,
    IEnumerable<InventoryItemDto> LowStock,
    IEnumerable<InventoryRecentMovementDto> RecentMovements);

public record InventoryRecentMovementDto(
    int Id,
    int ItemId,
    string ItemName,
    DateTime MovementDate,
    string MovementType,
    decimal Quantity,
    string? Reason);

/// <summary>What a resident sees. Deliberately excludes vendor, amounts and the expense link.</summary>
public record MemberInventoryDto(
    string Name,
    string Category,
    string Unit,
    decimal CurrentStock,
    string Status);

public record InventoryItemUpsertRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(60)] string Category,
    [Required, MaxLength(20)] string Unit,
    [Range(0, 9999999)] decimal MinimumStockLevel,
    bool IsActive);

/// <summary>Records a purchase. When <paramref name="CreateExpense"/> is set the society also
/// books the spend, so <paramref name="PurchaseAmount"/> must then be greater than zero.</summary>
public record StockInRequest(
    [Range(1, int.MaxValue)] int ItemId,
    [Range(0.01, 9999999)] decimal Quantity,
    DateTime? PurchaseDate,
    [Range(0, 9999999)] decimal? PurchaseAmount,
    [MaxLength(300)] string? Reason,
    bool CreateExpense,
    [MaxLength(150)] string? Vendor,
    [MaxLength(30)] string? PaymentMode,
    [MaxLength(60)] string? ExpenseCategory);

public record StockOutRequest(
    [Range(1, int.MaxValue)] int ItemId,
    [Range(0.01, 9999999)] decimal Quantity,
    DateTime? MovementDate,
    [Required, MaxLength(300)] string Reason);

/// <summary>Corrects the book balance after a physical count. <paramref name="Quantity"/> is the
/// signed difference (-2 when two bulbs were found broken).</summary>
public record StockAdjustRequest(
    [Range(1, int.MaxValue)] int ItemId,
    decimal Quantity,
    DateTime? MovementDate,
    [Required, MaxLength(300)] string Reason);
