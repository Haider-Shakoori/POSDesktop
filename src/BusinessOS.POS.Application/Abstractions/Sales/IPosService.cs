namespace BusinessOS.POS.Application.Abstractions.Sales;

public interface IPosService
{
    Task<PosReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(string query, int take = 30, CancellationToken cancellationToken = default);
    Task<PosShiftState> OpenShiftAsync(decimal openingCash, CancellationToken cancellationToken = default);
    Task<PosCheckoutResult> CheckoutAsync(PosCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<PosHeldSaleSummary> HoldAsync(PosHoldRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PosHeldSaleSummary>> GetHeldSalesAsync(CancellationToken cancellationToken = default);
    Task<PosHeldSaleDetail> ResumeHeldSaleAsync(long heldSaleId, CancellationToken cancellationToken = default);
    Task ReleaseHeldSaleAsync(long heldSaleId, CancellationToken cancellationToken = default);
}

public sealed record PosReferenceData(
    IReadOnlyList<PosPaymentMethod> PaymentMethods,
    PosShiftState Shift);

public sealed record PosPaymentMethod(long Id, string Code, string Name, bool IsCash);

public sealed record PosShiftState(bool IsOpen, long? ShiftId, decimal OpeningCash, DateTimeOffset? OpenedAt);

public sealed record PosProductSearchItem(
    long ProductUnitId,
    long ProductId,
    string Name,
    string Sku,
    string Unit,
    int DecimalPlaces,
    decimal ConversionFactor,
    decimal Price,
    decimal? MinimumPrice,
    decimal? AvailableQuantity,
    bool TrackStock,
    string? MatchedBarcode);

public sealed record PosCheckoutLineRequest(long ProductUnitId, decimal Quantity, decimal LineDiscountAmount = 0m);
public sealed record PosPaymentRequest(
    string MethodCode,
    decimal Amount,
    decimal? TenderedAmount = null,
    string? Reference = null,
    string? Notes = null);

public sealed record PosCheckoutRequest(
    string IdempotencyKey,
    IReadOnlyList<PosCheckoutLineRequest> Lines,
    decimal SaleDiscountAmount,
    IReadOnlyList<PosPaymentRequest> Payments,
    string? Notes = null);

public sealed record PosCheckoutResult(
    long SaleId,
    string SaleNumber,
    decimal NetTotal,
    decimal PaidAmount,
    decimal ChangeAmount,
    decimal CogsTotal,
    decimal GrossProfit,
    DateTimeOffset SoldAt);

public sealed record PosHoldRequest(
    string IdempotencyKey,
    IReadOnlyList<PosCheckoutLineRequest> Lines,
    decimal SaleDiscountAmount,
    string? Notes = null);

public sealed record PosHeldSaleSummary(
    long Id,
    string Number,
    int ItemCount,
    decimal EstimatedTotal,
    DateTimeOffset HeldAt);

public sealed record PosHeldSaleLine(
    long ProductUnitId,
    string Name,
    string Sku,
    string Unit,
    decimal Quantity,
    decimal UnitPriceSnapshot,
    decimal LineDiscountAmount,
    decimal? AvailableQuantity,
    decimal? MinimumPrice,
    bool TrackStock,
    int DecimalPlaces,
    decimal ConversionFactor);

public sealed record PosHeldSaleDetail(
    long Id,
    string Number,
    decimal SaleDiscountAmount,
    string? Notes,
    IReadOnlyList<PosHeldSaleLine> Lines);
