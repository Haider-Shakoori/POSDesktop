namespace BusinessOS.POS.Application.Abstractions.Dashboard;

public interface IDashboardService
{
    Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record DashboardShift(
    long Id, string Terminal, decimal OpeningCash, decimal ExpectedCash, DateTimeOffset OpenedAt);

public sealed record DashboardLowStockRow(
    long ProductId, string Sku, string Product, decimal StockOnHand, decimal MinimumStock, string Unit);

public sealed record DashboardRecentSaleRow(
    long SaleId, string Number, string Customer, decimal NetTotal,
    decimal ReturnedTotal, string PaymentStatus, DateTimeOffset SoldAt);

public sealed record DashboardSnapshot(
    DateTime BusinessDate,
    decimal? TodayNetSales,
    int? TodayTransactions,
    decimal? Receivables,
    decimal? Payables,
    int? LowStockCount,
    DashboardShift? OpenShift,
    IReadOnlyList<DashboardLowStockRow> LowStockProducts,
    IReadOnlyList<DashboardRecentSaleRow> RecentSales);
