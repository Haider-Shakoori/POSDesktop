namespace BusinessOS.POS.Persistence.Entities;

public sealed class BusinessDayClosureEntity
{
    public long Id { get; set; }
    public long BusinessDayId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int Version { get; set; }
    public long ClosedByUserId { get; set; }
    public int ShiftCount { get; set; }
    public int SalesCount { get; set; }
    public decimal SalesSubtotal { get; set; }
    public decimal SalesLineDiscountTotal { get; set; }
    public decimal SalesDiscountTotal { get; set; }
    public decimal SalesNetTotal { get; set; }
    public decimal SalesReturnTotal { get; set; }
    public decimal NetSalesTotal { get; set; }
    public decimal SalesCogsTotal { get; set; }
    public decimal CogsReversedTotal { get; set; }
    public decimal NetCogsTotal { get; set; }
    public decimal GrossProfitTotal { get; set; }
    public decimal CustomerCollectionsTotal { get; set; }
    public decimal PurchasesTotal { get; set; }
    public decimal PurchaseReturnsTotal { get; set; }
    public decimal SupplierPaymentsTotal { get; set; }
    public decimal OperatingExpensesTotal { get; set; }
    public decimal OtherIncomeTotal { get; set; }
    public decimal NetProfitTotal { get; set; }
    public decimal OpeningCashTotal { get; set; }
    public decimal CashInflowTotal { get; set; }
    public decimal CashOutflowTotal { get; set; }
    public decimal ExpectedCashTotal { get; set; }
    public decimal ActualCashTotal { get; set; }
    public decimal VarianceTotal { get; set; }
    public string? CashBreakdownJson { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
