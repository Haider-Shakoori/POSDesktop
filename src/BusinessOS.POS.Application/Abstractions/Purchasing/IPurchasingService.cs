namespace BusinessOS.POS.Application.Abstractions.Purchasing;

public interface IPurchasingService
{
    Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync(string? search = null, bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<SupplierDetail?> GetSupplierAsync(long supplierId, CancellationToken cancellationToken = default);
    Task<SupplierSummary> SaveSupplierAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseProductOption>> GetPurchasableProductsAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrderSummary>> GetPurchaseOrdersAsync(long? supplierId = null, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDetail?> GetPurchaseOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDetail> CreatePurchaseOrderAsync(PurchaseOrderCreateRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDetail> ApprovePurchaseOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<PurchaseOrderDetail> CancelPurchaseOrderAsync(long orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GoodsReceiptSummary>> GetGoodsReceiptsAsync(long? supplierId = null, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDetail?> GetGoodsReceiptAsync(long receiptId, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDetail> PostGoodsReceiptAsync(GoodsReceiptPostRequest request, CancellationToken cancellationToken = default);

    Task<PurchaseReturnResult> PostPurchaseReturnAsync(PurchaseReturnRequest request, CancellationToken cancellationToken = default);
    Task<SupplierPaymentResult> RecordSupplierPaymentAsync(SupplierPaymentRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchasePaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);
}

public sealed record SupplierSummary(
    long Id, string Name, string? ContactPerson, string? Phone, string? AlternatePhone,
    string? Address, decimal OpeningBalance, decimal CurrentBalance, string? Notes, bool IsActive);

public sealed record SupplierLedgerRow(
    long Id, string EntryType, decimal Debit, decimal Credit, decimal BalanceAfter,
    string? ReferenceNumber, DateTimeOffset OccurredAt, string? Notes);

public sealed record SupplierPaymentRow(
    long Id, string Number, string Method, decimal Amount, string? Reference, DateTimeOffset PaidAt, string? Notes);

public sealed record SupplierOpenReceiptRow(
    long Id, string Number, DateTimeOffset ReceivedAt, decimal NetTotal, decimal PaidAmount,
    decimal ReturnedTotal, decimal BalanceDue);

public sealed record SupplierDetail(
    SupplierSummary Supplier,
    IReadOnlyList<SupplierLedgerRow> Ledger,
    IReadOnlyList<SupplierOpenReceiptRow> OpenReceipts,
    IReadOnlyList<SupplierPaymentRow> Payments);

public sealed record SupplierSaveRequest(
    long? Id, string Name, string? ContactPerson, string? Phone, string? AlternatePhone,
    string? Address, decimal OpeningBalance, string? Notes, bool IsActive = true);

public sealed record PurchaseProductOption(
    long ProductUnitId, long ProductId, string Sku, string Name, string Unit,
    int DecimalPlaces, decimal ConversionFactor, decimal PurchaseCost, bool TrackExpiry);

public sealed record PurchaseOrderLineRequest(
    long ProductUnitId, decimal Quantity, decimal UnitCost, decimal LineDiscountAmount = 0m, string? Notes = null);

public sealed record PurchaseOrderCreateRequest(
    long SupplierId, DateTime OrderDate, DateTime? ExpectedDate, string? SupplierReference,
    decimal OrderDiscountAmount, string? Notes, IReadOnlyList<PurchaseOrderLineRequest> Items);

public sealed record PurchaseOrderSummary(
    long Id, string Number, long SupplierId, string SupplierName, string Status,
    DateTime OrderDate, DateTime? ExpectedDate, decimal NetTotal, DateTimeOffset CreatedAt);

public sealed record PurchaseOrderLine(
    long Id, long ProductUnitId, string Sku, string Product, string Unit, int DecimalPlaces,
    decimal OrderedQuantity, decimal ReceivedQuantity, decimal RemainingQuantity,
    decimal UnitCost, decimal LineDiscountAmount, decimal LineNetTotal);

public sealed record PurchaseOrderDetail(
    PurchaseOrderSummary Header, string? SupplierReference, decimal Subtotal, decimal LineDiscountTotal,
    decimal OrderDiscountAmount, string? Notes, IReadOnlyList<PurchaseOrderLine> Items);

public sealed record GoodsReceiptLineRequest(
    long? PurchaseOrderItemId, long? ProductUnitId, decimal Quantity, decimal? UnitCost,
    decimal LineDiscountAmount = 0m, string? BatchNumber = null,
    DateTime? ManufacturedAt = null, DateTime? ExpiresAt = null);

public sealed record GoodsReceiptExpenseRequest(string Type, decimal Amount, string? Description = null);

public sealed record GoodsReceiptPostRequest(
    string IdempotencyKey, long SupplierId, long? PurchaseOrderId, string? SupplierInvoiceReference,
    DateTimeOffset? ReceivedAt, decimal ReceiptDiscountAmount, decimal PaidAmount,
    string? PaymentMethodCode, string? PaymentReference, string? PaymentNotes, string? Notes,
    IReadOnlyList<GoodsReceiptLineRequest> Items, IReadOnlyList<GoodsReceiptExpenseRequest> Expenses);

public sealed record GoodsReceiptSummary(
    long Id, string Number, long SupplierId, string SupplierName, long? PurchaseOrderId,
    string Status, DateTimeOffset ReceivedAt, decimal NetTotal, decimal PaidAmount,
    decimal ReturnedTotal, decimal BalanceDue);

public sealed record GoodsReceiptLine(
    long Id, long ProductUnitId, string Sku, string Product, string Unit, int DecimalPlaces,
    decimal Quantity, decimal QuantityBase, decimal SourceUnitCost, decimal LineDiscountAmount,
    decimal AllocatedReceiptDiscount, decimal AllocatedExpense, decimal LandedTotal,
    decimal SourceUnitLandedCost, decimal BaseUnitLandedCost, string? BatchNumber,
    DateTime? ManufacturedAt, DateTime? ExpiresAt, decimal ReturnedQuantity,
    decimal ReturnableQuantity, decimal ReturnableAmount);

public sealed record GoodsReceiptExpenseRow(string Type, string? Description, decimal Amount);

public sealed record GoodsReceiptDetail(
    GoodsReceiptSummary Header, string? SupplierInvoiceReference, decimal Subtotal,
    decimal LineDiscountTotal, decimal ReceiptDiscountAmount, decimal ExpenseTotal,
    string? Notes, IReadOnlyList<GoodsReceiptLine> Items, IReadOnlyList<GoodsReceiptExpenseRow> Expenses);

public sealed record PurchaseReturnLineRequest(long GoodsReceiptItemId, decimal Quantity);

public sealed record PurchaseReturnRequest(
    string IdempotencyKey, long GoodsReceiptId, string Reason, IReadOnlyList<PurchaseReturnLineRequest> Items);

public sealed record PurchaseReturnResult(
    long Id, string Number, long GoodsReceiptId, decimal ReturnTotal,
    decimal ReceiptBalanceDue, decimal SupplierBalanceAfter, DateTimeOffset PostedAt);

public sealed record PurchasePaymentMethodOption(string Code, string Name, bool IsCash);

public sealed record SupplierPaymentRequest(
    string IdempotencyKey, long SupplierId, decimal Amount, string MethodCode,
    string? Reference, DateTimeOffset? PaidAt, string? Notes);

public sealed record SupplierPaymentAllocation(long GoodsReceiptId, string GoodsReceiptNumber, decimal Amount);

public sealed record SupplierPaymentResult(
    long Id, string Number, long SupplierId, decimal Amount, string MethodCode,
    decimal SupplierBalanceAfter, DateTimeOffset PaidAt,
    IReadOnlyList<SupplierPaymentAllocation> Allocations);
