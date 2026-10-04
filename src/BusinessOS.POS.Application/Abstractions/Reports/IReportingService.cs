namespace BusinessOS.POS.Application.Abstractions.Reports;

public interface IReportingService
{
    Task<ReportLookupData> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<ReportResult> BuildAsync(ReportFilters filters, CancellationToken cancellationToken = default);
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
}

public sealed record ReportFilters(
    DateTime From, DateTime To, long? CustomerId = null, long? SupplierId = null,
    long? ProductId = null, long? CategoryId = null);

public sealed record ReportLookupOption(long Id, string Label);
public sealed record ReportLookupData(
    IReadOnlyList<ReportLookupOption> Customers,
    IReadOnlyList<ReportLookupOption> Suppliers,
    IReadOnlyList<ReportLookupOption> Products,
    IReadOnlyList<ReportLookupOption> Categories);

public sealed record ReportSummary(
    int SalesCount, decimal Subtotal, decimal Discounts, decimal SalesNet,
    decimal Returns, decimal NetSales, decimal? SalesCogs, decimal? CogsReversed,
    decimal? NetCogs, decimal? GrossProfit, decimal Expenses, decimal OtherIncome,
    decimal? NetProfit, decimal Purchases, decimal PurchaseReturns,
    decimal Collections, decimal SupplierPayments, decimal Aov,
    decimal Receivables, decimal Payables, decimal InventoryValue,
    decimal DamagedCost, decimal ExpiredCost);

public sealed record SalesTrendRow(DateTime Day, int SalesCount, decimal NetSales, decimal? GrossProfit);
public sealed record ProductPerformanceRow(
    long ProductId, string Sku, string Product, string? Category,
    decimal QuantityBase, decimal NetSales, decimal? Cogs, decimal? GrossProfit);
public sealed record CategoryProfitRow(
    long? CategoryId, string Category, decimal NetSales, decimal? Cogs, decimal? GrossProfit);
public sealed record BalanceReportRow(long Id, string Name, decimal Balance);
public sealed record InventoryReportRow(
    long ProductId, string Sku, string Product, decimal StockOnHand,
    decimal MinimumStock, bool LowStock, decimal InventoryValue);
public sealed record ExpiryReportRow(
    long BatchId, string Sku, string Product, string BatchNumber,
    DateTime? ExpiresAt, decimal StockOnHand, int DaysRemaining);
public sealed record ClosingHistoryRow(
    DateTime BusinessDate, string Number, int Version, decimal NetSales,
    decimal? NetProfit, decimal ExpectedCash, decimal ActualCash,
    decimal Variance, DateTimeOffset ClosedAt);
public sealed record PeakHourRow(int Hour, int SalesCount, decimal NetSales);
public sealed record WeekdayPerformanceRow(int DayOfWeek, string Weekday, int SalesCount, decimal NetSales);

public sealed record ReportResult(
    ReportFilters Filters, bool CanViewProfit, ReportSummary Summary,
    IReadOnlyList<SalesTrendRow> SalesTrend,
    IReadOnlyList<ProductPerformanceRow> TopProducts,
    IReadOnlyList<ProductPerformanceRow> SlowProducts,
    IReadOnlyList<CategoryProfitRow> CategoryProfit,
    IReadOnlyList<BalanceReportRow> Customers,
    IReadOnlyList<BalanceReportRow> Suppliers,
    IReadOnlyList<InventoryReportRow> Inventory,
    IReadOnlyList<ExpiryReportRow> Expiring,
    IReadOnlyList<ClosingHistoryRow> ClosingHistory,
    IReadOnlyList<PeakHourRow> PeakHours,
    IReadOnlyList<WeekdayPerformanceRow> Weekdays);

public sealed record DashboardRecentSale(
    long Id, string Number, string Customer, decimal NetTotal,
    decimal ReturnedTotal, string PaymentStatus, DateTimeOffset SoldAt);
public sealed record DashboardLowStock(
    long ProductId, string Sku, string Product, decimal StockOnHand, decimal MinimumStock);
public sealed record DashboardSnapshot(
    bool CanViewSales, bool CanViewCustomers, bool CanViewSuppliers, bool CanViewInventory,
    decimal? TodayNetSales, int? TodayTransactions, decimal? Receivables, decimal? Payables,
    int? LowStockCount, decimal? CurrentShiftExpectedCash, string? CurrentShiftTerminal,
    IReadOnlyList<DashboardRecentSale> RecentSales,
    IReadOnlyList<DashboardLowStock> LowStockProducts);
