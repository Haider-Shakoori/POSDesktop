namespace BusinessOS.POS.Application.Abstractions.Sales;

public interface ISalesService
{
    Task<IReadOnlyList<SaleHistoryRow>> GetSalesAsync(
        string? search = null,
        int take = 300,
        CancellationToken cancellationToken = default);
    Task<SaleDetail?> GetSaleAsync(long saleId, CancellationToken cancellationToken = default);
    Task<SaleReceiptData?> GetReceiptAsync(long saleId, CancellationToken cancellationToken = default);
}

public sealed record SaleHistoryRow(
    long Id,
    string Number,
    string CustomerName,
    string CashierName,
    decimal NetTotal,
    decimal PaidAmount,
    decimal ChangeAmount,
    string PaymentStatus,
    DateTimeOffset SoldAt);

public sealed record SaleDetailLine(
    long SaleItemId,
    string Sku,
    string Product,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal LineDiscount,
    decimal SaleDiscount,
    decimal NetTotal,
    decimal Cogs,
    decimal GrossProfit,
    decimal ReturnedQuantity,
    decimal ReturnableQuantity,
    decimal ReturnedAmount,
    decimal ReturnableAmount);

public sealed record SaleDetailPayment(
    string MethodCode,
    string MethodName,
    decimal AppliedAmount,
    decimal TenderedAmount,
    decimal ChangeAmount,
    string? Reference,
    string? Notes,
    DateTimeOffset PaidAt);

public sealed record SaleDetail(
    long Id,
    string Number,
    string CustomerName,
    string CashierName,
    string Status,
    string PaymentStatus,
    long? CustomerId,
    decimal Subtotal,
    decimal LineDiscountTotal,
    decimal SaleDiscountAmount,
    decimal NetTotal,
    decimal CogsTotal,
    decimal GrossProfit,
    decimal PaidAmount,
    decimal ChangeAmount,
    decimal BalanceDue,
    decimal ReturnedTotal,
    decimal RefundedTotal,
    DateTimeOffset SoldAt,
    string? Notes,
    IReadOnlyList<SaleDetailLine> Items,
    IReadOnlyList<SaleDetailPayment> Payments);

public sealed record SaleReceiptData(
    string BusinessName,
    string BusinessSubtitle,
    string CurrencyCode,
    SaleDetail Sale);
