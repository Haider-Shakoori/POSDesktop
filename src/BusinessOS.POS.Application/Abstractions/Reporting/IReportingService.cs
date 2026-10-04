namespace BusinessOS.POS.Application.Abstractions.Reporting;

public interface IReportingService
{
    Task<ReportLookups> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<ReportSnapshot> BuildAsync(ReportFilters filters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesExportRow>> GetSalesExportAsync(ReportFilters filters, CancellationToken cancellationToken = default);
}

public sealed record ReportFilters(
    DateTime From,
    DateTime To,
    long? CustomerId = null,
    long? SupplierId = null,
    long? ProductId = null,
    long? CategoryId = null);

public sealed record ReportLookupItem(long Id, string Code, string Name);
public sealed record ReportLookups(
    IReadOnlyList<ReportLookupItem> Customers,
    IReadOnlyList<ReportLookupItem> Suppliers,
    IReadOnlyList<ReportLookupItem> Products,
    IReadOnlyList<ReportLookupItem> Categories);

public sealed record ReportSummary(
    int SalesCount,
    decimal Subtotal,
    decimal Discounts,
    decimal SalesNet,
    decimal Returns,
    decimal NetSales,
    decimal? SalesCogs,
    decimal? CogsReversed,
    decimal? NetCogs,
    decimal? GrossProfit,
    decimal Expenses,
    decimal OtherIncome,
    decimal? NetProfit,
    decimal Purchases,
    decimal PurchaseReturns,
    decimal Collections,
    decimal SupplierPayments,
    decimal AverageOrderValue,
    decimal Receivables,
    decimal Payables,
    decimal? InventoryValue,
    decimal? DamagedCost,
    decimal? ExpiredCost);

public sealed record SalesTrendRow(
    DateTime BusinessDate, int SalesCount, decimal NetSales, decimal? GrossProfit);

public sealed record ProductPerformanceRow(
    long ProductId, string Sku, string Product, string? Category,
    decimal QuantityBase, decimal NetSales, decimal? Cogs, decimal? GrossProfit);

public sealed record CategoryProfitRow(
    long? CategoryId, string Category, decimal NetSales, decimal? Cogs, decimal? GrossProfit);

public sealed record CustomerActivityRow(
    long CustomerId, string Customer, string? Phone, int SalesCount,
    decimal SalesTotal, decimal CurrentBalance);

public sealed record SupplierActivityRow(
    long SupplierId, string Supplier, string? Phone, int ReceiptCount,
    decimal PurchasesTotal, decimal CurrentBalance);

public sealed record InventoryHealth(
    int TrackedProducts, int LowStock, int OutOfStock,
    int ExpiredBatches, int ExpiringBatches);

public sealed record ExpiryWatchRow(
    long BatchId, long ProductId, string Product, string Sku,
    string BatchNumber, decimal StockOnHand, DateTime? ExpiresAt);

public sealed record ClosingHistoryRow(
    long ClosureId, DateTime BusinessDate, string Number, int Version,
    decimal NetSales, decimal Variance, decimal? NetProfit, DateTimeOffset ClosedAt);

public sealed record TimePerformanceRow(
    int Bucket, string Label, int SalesCount, decimal NetSales);

public sealed record ReportSnapshot(
    ReportFilters Filters,
    bool CanViewProfit,
    ReportSummary Summary,
    IReadOnlyList<SalesTrendRow> SalesTrend,
    IReadOnlyList<ProductPerformanceRow> TopProducts,
    IReadOnlyList<ProductPerformanceRow> SlowProducts,
    IReadOnlyList<CategoryProfitRow> CategoryProfit,
    IReadOnlyList<CustomerActivityRow> Customers,
    IReadOnlyList<SupplierActivityRow> Suppliers,
    InventoryHealth Inventory,
    IReadOnlyList<ExpiryWatchRow> Expiring,
    IReadOnlyList<ClosingHistoryRow> ClosingHistory,
    IReadOnlyList<TimePerformanceRow> PeakHours,
    IReadOnlyList<TimePerformanceRow> Weekdays);

public sealed record SalesExportRow(
    string Number,
    DateTimeOffset SoldAt,
    string Customer,
    decimal Subtotal,
    decimal LineDiscountTotal,
    decimal SaleDiscountAmount,
    decimal NetTotal,
    decimal ReturnedTotal,
    decimal? CogsTotal,
    decimal? GrossProfit,
    decimal PaidAmount,
    decimal BalanceDue);
