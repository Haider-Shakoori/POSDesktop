namespace BusinessOS.POS.Application.Abstractions.Sales;

public interface ISaleReturnService
{
    Task<SaleReturnResult> ReturnAsync(SaleReturnRequest request, CancellationToken cancellationToken = default);
    Task<SaleReturnResult> VoidAsync(SaleVoidRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaleReturnSummary>> GetReturnsAsync(long? saleId = null, CancellationToken cancellationToken = default);
}

public sealed record SaleReturnLineRequest(long SaleItemId, decimal Quantity);

public sealed record SaleRefundRequest(
    string PaymentMethodCode,
    decimal Amount,
    string? Reference = null,
    string? Notes = null);

public sealed record SaleReturnRequest(
    string IdempotencyKey,
    long SaleId,
    string Reason,
    IReadOnlyList<SaleReturnLineRequest> Items,
    IReadOnlyList<SaleRefundRequest> Refunds);

public sealed record SaleVoidRequest(
    string IdempotencyKey,
    long SaleId,
    string Reason,
    IReadOnlyList<SaleRefundRequest> Refunds);

public sealed record SaleReturnResult(
    long Id,
    string Number,
    long SaleId,
    string Type,
    decimal ReturnTotal,
    decimal CogsReversed,
    decimal ReceivableReversed,
    decimal RefundTotal,
    string SaleStatus,
    decimal SaleBalanceDue,
    DateTimeOffset PostedAt);

public sealed record SaleReturnSummary(
    long Id,
    string Number,
    long SaleId,
    string SaleNumber,
    string Type,
    string Reason,
    decimal ReturnTotal,
    decimal ReceivableReversed,
    decimal RefundTotal,
    DateTimeOffset PostedAt);
