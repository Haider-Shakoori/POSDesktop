namespace BusinessOS.POS.Application.Abstractions.Closing;

public interface IBusinessDayClosingService
{
    Task<BusinessDaySummary> GetSummaryAsync(DateTime businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessDayRow>> GetBusinessDaysAsync(int take = 120, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessDayClosureRow>> GetClosuresAsync(DateTime businessDate, CancellationToken cancellationToken = default);
    Task<BusinessDayClosureRow> CloseAsync(BusinessDayCloseRequest request, CancellationToken cancellationToken = default);
    Task<BusinessDayRow> ReopenAsync(DateTime businessDate, string reason, CancellationToken cancellationToken = default);
}

public sealed record BusinessDayRow(
    long Id, DateTime BusinessDate, string Status, DateTimeOffset? ClosedAt,
    long? ClosedByUserId, DateTimeOffset? ReopenedAt, long? ReopenedByUserId,
    string? ReopenReason, int ClosureVersions);

public sealed record BusinessDayShiftRow(
    long ShiftId, string Terminal, string Cashier, string Status,
    decimal OpeningCash, decimal ExpectedCash, decimal? ActualCash,
    decimal? Variance, bool? WithinTolerance, DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt);

public sealed record CashBreakdownRow(string MovementType, string Direction, decimal Amount);

public sealed record BusinessDaySummary(
    DateTime BusinessDate,
    string Status,
    int ShiftCount,
    int OpenShiftCount,
    int SalesCount,
    decimal SalesSubtotal,
    decimal SalesLineDiscountTotal,
    decimal SalesDiscountTotal,
    decimal SalesNetTotal,
    decimal SalesReturnTotal,
    decimal NetSalesTotal,
    decimal SalesCogsTotal,
    decimal CogsReversedTotal,
    decimal NetCogsTotal,
    decimal GrossProfitTotal,
    decimal CustomerCollectionsTotal,
    decimal PurchasesTotal,
    decimal PurchaseReturnsTotal,
    decimal SupplierPaymentsTotal,
    decimal OperatingExpensesTotal,
    decimal OtherIncomeTotal,
    decimal NetProfitTotal,
    decimal OpeningCashTotal,
    decimal CashInflowTotal,
    decimal CashOutflowTotal,
    decimal ExpectedCashTotal,
    decimal ActualCashTotal,
    decimal VarianceTotal,
    decimal LedgerExpectedCashTotal,
    IReadOnlyList<CashBreakdownRow> CashBreakdown,
    IReadOnlyList<BusinessDayShiftRow> Shifts);

public sealed record BusinessDayCloseRequest(
    string IdempotencyKey, DateTime BusinessDate, string? Notes);

public sealed record BusinessDayClosureRow(
    long Id,
    long BusinessDayId,
    string Number,
    int Version,
    long ClosedByUserId,
    int ShiftCount,
    int SalesCount,
    decimal SalesSubtotal,
    decimal SalesLineDiscountTotal,
    decimal SalesDiscountTotal,
    decimal SalesNetTotal,
    decimal SalesReturnTotal,
    decimal NetSalesTotal,
    decimal SalesCogsTotal,
    decimal CogsReversedTotal,
    decimal NetCogsTotal,
    decimal GrossProfitTotal,
    decimal CustomerCollectionsTotal,
    decimal PurchasesTotal,
    decimal PurchaseReturnsTotal,
    decimal SupplierPaymentsTotal,
    decimal OperatingExpensesTotal,
    decimal OtherIncomeTotal,
    decimal NetProfitTotal,
    decimal OpeningCashTotal,
    decimal CashInflowTotal,
    decimal CashOutflowTotal,
    decimal ExpectedCashTotal,
    decimal ActualCashTotal,
    decimal VarianceTotal,
    IReadOnlyList<CashBreakdownRow> CashBreakdown,
    string? Notes,
    DateTimeOffset ClosedAt);
