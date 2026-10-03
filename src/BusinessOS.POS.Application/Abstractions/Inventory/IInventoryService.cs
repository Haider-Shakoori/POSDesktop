namespace BusinessOS.POS.Application.Abstractions.Inventory;

public interface IInventoryService
{
    Task<InventoryReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryStockRow>> GetStockAsync(string? search = null, bool lowStockOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryMovementRow>> GetMovementsAsync(long? productId = null, int take = 250, CancellationToken cancellationToken = default);
    Task<InventoryMovementRow> RecordOpeningStockAsync(OpeningStockRequest request, CancellationToken cancellationToken = default);
    Task<InventoryMovementRow> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<StockCountDetail> CreateStockCountAsync(CreateStockCountRequest request, CancellationToken cancellationToken = default);
    Task<StockCountDetail> ApproveStockCountAsync(long stockCountId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockCountSummary>> GetStockCountsAsync(CancellationToken cancellationToken = default);
    Task<InventoryWriteoffResult> PostWriteoffAsync(InventoryWriteoffRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryWriteoffResult>> GetWriteoffsAsync(CancellationToken cancellationToken = default);
}

public sealed record InventoryReferenceData(
    IReadOnlyList<InventoryProductOption> Products,
    IReadOnlyList<InventoryTargetOption> Targets);

public sealed record InventoryProductOption(
    long ProductId,
    string Sku,
    string Name,
    decimal StockOnHand,
    decimal MinimumStock,
    decimal ReorderQuantity,
    bool TrackExpiry,
    IReadOnlyList<InventoryUnitOption> Units,
    IReadOnlyList<InventoryBatchRow> Batches);

public sealed record InventoryUnitOption(
    long ProductUnitId,
    long UnitId,
    string Name,
    string Code,
    int DecimalPlaces,
    decimal ConversionFactor,
    bool CanPurchase);

public sealed record InventoryBatchRow(
    long Id,
    string BatchNumber,
    DateTime? ManufacturedAt,
    DateTime? ExpiresAt,
    decimal StockOnHand,
    bool IsBlocked,
    string? Notes,
    bool IsExpired);

public sealed record InventoryTargetOption(
    string Key,
    long ProductId,
    long? BatchId,
    string Display,
    decimal ExpectedQuantityBase,
    bool IsExpired,
    bool IsBlocked);

public sealed record InventoryStockRow(
    long ProductId,
    string Sku,
    string Name,
    string BaseUnit,
    decimal StockOnHand,
    decimal MinimumStock,
    decimal ReorderQuantity,
    bool LowStock,
    bool TrackExpiry,
    int BatchCount,
    int ExpiredBatchCount,
    decimal ExpiredQuantity);

public sealed record InventoryMovementRow(
    long Id,
    long ProductId,
    string Product,
    string Sku,
    string MovementType,
    decimal QuantityBase,
    decimal BalanceAfter,
    string? BatchNumber,
    decimal? BatchBalanceAfter,
    string? SourceUnit,
    decimal? SourceQuantity,
    decimal? SourceUnitCost,
    decimal? UnitCostBase,
    string? ReferenceType,
    long? ReferenceId,
    string? Notes,
    DateTimeOffset OccurredAt);

public sealed record OpeningStockRequest(
    string IdempotencyKey,
    long ProductId,
    long UnitId,
    decimal Quantity,
    decimal? UnitCost,
    string? BatchNumber,
    DateTime? ManufacturedAt,
    DateTime? ExpiresAt,
    string? Notes);

public sealed record StockAdjustmentRequest(
    string IdempotencyKey,
    string TargetKey,
    decimal QuantityBase,
    bool IsIncrease,
    string Reason,
    string? Notes);

public sealed record StockCountLineRequest(string TargetKey, decimal PhysicalQuantityBase);

public sealed record CreateStockCountRequest(
    string IdempotencyKey,
    IReadOnlyList<StockCountLineRequest> Items,
    string? Notes);

public sealed record StockCountItemRow(
    long Id,
    string Target,
    string Product,
    string? BatchNumber,
    decimal ExpectedQuantityBase,
    decimal PhysicalQuantityBase,
    decimal VarianceQuantityBase,
    decimal CostAmount);

public sealed record StockCountDetail(
    long Id,
    string Number,
    string Status,
    DateTimeOffset CountedAt,
    DateTimeOffset? ApprovedAt,
    string? Notes,
    IReadOnlyList<StockCountItemRow> Items);

public sealed record StockCountSummary(
    long Id,
    string Number,
    string Status,
    int ItemCount,
    decimal AbsoluteVariance,
    DateTimeOffset CountedAt,
    DateTimeOffset? ApprovedAt);

public sealed record InventoryWriteoffLineRequest(string TargetKey, decimal QuantityBase);

public sealed record InventoryWriteoffRequest(
    string IdempotencyKey,
    string WriteoffType,
    string Reason,
    IReadOnlyList<InventoryWriteoffLineRequest> Items,
    string? Notes);

public sealed record InventoryWriteoffResult(
    long Id,
    string Number,
    string WriteoffType,
    string Reason,
    int ItemCount,
    decimal TotalQuantity,
    decimal TotalCost,
    DateTimeOffset PostedAt,
    string? Notes);
