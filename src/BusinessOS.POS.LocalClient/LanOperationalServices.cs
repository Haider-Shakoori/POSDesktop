using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;

namespace BusinessOS.POS.LocalClient;

public sealed class LanDashboardService(LanApiClient api) : IDashboardService
{
    public Task<DashboardSnapshot> GetAsync(CancellationToken ct = default) =>
        api.GetAsync<DashboardSnapshot>("dashboard", ct);
}

public sealed class LanProductCatalogService(LanApiClient api) : IProductCatalogService
{
    public Task<CatalogReferenceData> GetReferenceDataAsync(CancellationToken ct = default) =>
        api.GetAsync<CatalogReferenceData>("catalog/references", ct);

    public Task<IReadOnlyList<CatalogProductSummary>> GetProductsAsync(
        string? search = null, long? categoryId = null, bool? isActive = null,
        CancellationToken ct = default) =>
        api.PostAsync<CatalogSearchRequest, IReadOnlyList<CatalogProductSummary>>(
            "catalog/products/search", new(search, categoryId, isActive), ct);

    public Task<CatalogProductDetail?> GetProductAsync(long productId, CancellationToken ct = default) =>
        api.GetOptionalAsync<CatalogProductDetail>($"catalog/products/{productId}", ct);

    public Task<CatalogProductDetail> SaveProductAsync(CatalogProductSaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<CatalogProductSaveRequest, CatalogProductDetail>("catalog/products/save", request, ct);

    public Task<CatalogCategoryItem> SaveCategoryAsync(CatalogCategorySaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<CatalogCategorySaveRequest, CatalogCategoryItem>("catalog/categories/save", request, ct);

    public Task<CatalogBrandItem> SaveBrandAsync(CatalogBrandSaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<CatalogBrandSaveRequest, CatalogBrandItem>("catalog/brands/save", request, ct);

    public Task<CatalogUnitItem> SaveUnitAsync(CatalogUnitSaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<CatalogUnitSaveRequest, CatalogUnitItem>("catalog/units/save", request, ct);
}

public sealed class LanInventoryService(LanApiClient api) : IInventoryService
{
    public Task<InventoryReferenceData> GetReferenceDataAsync(CancellationToken ct = default) =>
        api.GetAsync<InventoryReferenceData>("inventory/references", ct);

    public Task<IReadOnlyList<InventoryStockRow>> GetStockAsync(
        string? search = null, bool lowStockOnly = false, CancellationToken ct = default) =>
        api.PostAsync<InventoryStockSearchRequest, IReadOnlyList<InventoryStockRow>>(
            "inventory/stock/search", new(search, lowStockOnly), ct);

    public Task<IReadOnlyList<InventoryMovementRow>> GetMovementsAsync(
        long? productId = null, int take = 250, CancellationToken ct = default) =>
        api.PostAsync<InventoryMovementSearchRequest, IReadOnlyList<InventoryMovementRow>>(
            "inventory/movements/search", new(productId, take), ct);

    public Task<InventoryMovementRow> RecordOpeningStockAsync(OpeningStockRequest request, CancellationToken ct = default) =>
        api.PostAsync<OpeningStockRequest, InventoryMovementRow>("inventory/opening-stock", request, ct);

    public Task<InventoryMovementRow> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct = default) =>
        api.PostAsync<StockAdjustmentRequest, InventoryMovementRow>("inventory/adjust", request, ct);

    public Task<StockCountDetail> CreateStockCountAsync(CreateStockCountRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateStockCountRequest, StockCountDetail>("inventory/counts", request, ct);

    public Task<StockCountDetail> ApproveStockCountAsync(
        long stockCountId, string idempotencyKey, CancellationToken ct = default) =>
        api.PostAsync<IdempotencyRequest, StockCountDetail>(
            $"inventory/counts/{stockCountId}/approve", new(idempotencyKey), ct);

    public Task<IReadOnlyList<StockCountSummary>> GetStockCountsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<StockCountSummary>>("inventory/counts", ct);

    public Task<InventoryWriteoffResult> PostWriteoffAsync(InventoryWriteoffRequest request, CancellationToken ct = default) =>
        api.PostAsync<InventoryWriteoffRequest, InventoryWriteoffResult>("inventory/writeoffs", request, ct);

    public Task<IReadOnlyList<InventoryWriteoffResult>> GetWriteoffsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<InventoryWriteoffResult>>("inventory/writeoffs", ct);
}

public sealed class LanPosService(LanApiClient api) : IPosService
{
    public Task<PosReferenceData> GetReferenceDataAsync(CancellationToken ct = default) =>
        api.GetAsync<PosReferenceData>("pos/references", ct);

    public Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(
        string query, int take = 30, CancellationToken ct = default) =>
        api.PostAsync<PosSearchRequest, IReadOnlyList<PosProductSearchItem>>(
            "pos/products/search", new(query, take), ct);

    public Task<PosShiftState> OpenShiftAsync(decimal openingCash, CancellationToken ct = default) =>
        api.PostAsync<OpeningCashRequest, PosShiftState>("pos/open-shift", new(openingCash), ct);

    public Task<PosCheckoutResult> CheckoutAsync(PosCheckoutRequest request, CancellationToken ct = default) =>
        api.PostAsync<PosCheckoutRequest, PosCheckoutResult>("pos/checkout", request, ct);

    public Task<PosHeldSaleSummary> HoldAsync(PosHoldRequest request, CancellationToken ct = default) =>
        api.PostAsync<PosHoldRequest, PosHeldSaleSummary>("pos/hold", request, ct);

    public Task<IReadOnlyList<PosHeldSaleSummary>> GetHeldSalesAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<PosHeldSaleSummary>>("pos/held", ct);

    public Task<PosHeldSaleDetail> ResumeHeldSaleAsync(long heldSaleId, CancellationToken ct = default) =>
        api.GetAsync<PosHeldSaleDetail>($"pos/held/{heldSaleId}", ct);

    public Task ReleaseHeldSaleAsync(long heldSaleId, CancellationToken ct = default) =>
        api.PostAsync($"pos/held/{heldSaleId}/release", ct);
}

public sealed class LanSalesService(LanApiClient api) : ISalesService
{
    public Task<IReadOnlyList<SaleHistoryRow>> GetSalesAsync(
        string? search = null, int take = 300, CancellationToken ct = default) =>
        api.PostAsync<SearchTakeRequest, IReadOnlyList<SaleHistoryRow>>(
            "sales/search", new(search, take), ct);

    public Task<SaleDetail?> GetSaleAsync(long saleId, CancellationToken ct = default) =>
        api.GetOptionalAsync<SaleDetail>($"sales/{saleId}", ct);

    public Task<SaleReceiptData?> GetReceiptAsync(long saleId, CancellationToken ct = default) =>
        api.GetOptionalAsync<SaleReceiptData>($"sales/{saleId}/receipt", ct);
}

public sealed class LanSaleReturnService(LanApiClient api) : ISaleReturnService
{
    public Task<SaleReturnResult> ReturnAsync(SaleReturnRequest request, CancellationToken ct = default) =>
        api.PostAsync<SaleReturnRequest, SaleReturnResult>("sales/returns", request, ct);

    public Task<SaleReturnResult> VoidAsync(SaleVoidRequest request, CancellationToken ct = default) =>
        api.PostAsync<SaleVoidRequest, SaleReturnResult>("sales/void", request, ct);

    public Task<IReadOnlyList<SaleReturnSummary>> GetReturnsAsync(long? saleId = null, CancellationToken ct = default) =>
        api.PostAsync<OptionalLongRequest, IReadOnlyList<SaleReturnSummary>>(
            "sales/returns/search", new(saleId), ct);

    public Task<IReadOnlyList<SaleRefundMethod>> GetRefundMethodsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<SaleRefundMethod>>("sales/refund-methods", ct);
}

public sealed class LanCustomerService(LanApiClient api) : ICustomerService
{
    public Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(
        string? search = null, bool activeOnly = false, CancellationToken ct = default) =>
        api.PostAsync<CustomerSearchRequest, IReadOnlyList<CustomerSummary>>(
            "customers/search", new(search, activeOnly), ct);

    public Task<CustomerDetail?> GetCustomerAsync(long customerId, CancellationToken ct = default) =>
        api.GetOptionalAsync<CustomerDetail>($"customers/{customerId}", ct);

    public Task<CustomerSummary> SaveCustomerAsync(CustomerSaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<CustomerSaveRequest, CustomerSummary>("customers/save", request, ct);

    public Task<CustomerCollectionResult> CollectAsync(CustomerCollectionRequest request, CancellationToken ct = default) =>
        api.PostAsync<CustomerCollectionRequest, CustomerCollectionResult>("customers/collect", request, ct);

    public Task<IReadOnlyList<CustomerPaymentMethod>> GetPaymentMethodsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CustomerPaymentMethod>>("customers/payment-methods", ct);
}

public sealed class LanPurchasingService(LanApiClient api) : IPurchasingService
{
    public Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync(
        string? search = null, bool activeOnly = false, CancellationToken ct = default) =>
        api.PostAsync<SupplierSearchRequest, IReadOnlyList<SupplierSummary>>(
            "purchasing/suppliers/search", new(search, activeOnly), ct);

    public Task<SupplierDetail?> GetSupplierAsync(long supplierId, CancellationToken ct = default) =>
        api.GetOptionalAsync<SupplierDetail>($"purchasing/suppliers/{supplierId}", ct);

    public Task<SupplierSummary> SaveSupplierAsync(SupplierSaveRequest request, CancellationToken ct = default) =>
        api.PostAsync<SupplierSaveRequest, SupplierSummary>("purchasing/suppliers/save", request, ct);

    public Task<IReadOnlyList<PurchaseProductOption>> GetPurchasableProductsAsync(
        string? search = null, CancellationToken ct = default) =>
        api.PostAsync<SimpleSearchRequest, IReadOnlyList<PurchaseProductOption>>(
            "purchasing/products/search", new(search), ct);

    public Task<IReadOnlyList<PurchaseOrderSummary>> GetPurchaseOrdersAsync(
        long? supplierId = null, CancellationToken ct = default) =>
        api.PostAsync<OptionalLongRequest, IReadOnlyList<PurchaseOrderSummary>>(
            "purchasing/orders/search", new(supplierId), ct);

    public Task<PurchaseOrderDetail?> GetPurchaseOrderAsync(long orderId, CancellationToken ct = default) =>
        api.GetOptionalAsync<PurchaseOrderDetail>($"purchasing/orders/{orderId}", ct);

    public Task<PurchaseOrderDetail> CreatePurchaseOrderAsync(PurchaseOrderCreateRequest request, CancellationToken ct = default) =>
        api.PostAsync<PurchaseOrderCreateRequest, PurchaseOrderDetail>("purchasing/orders", request, ct);

    public Task<PurchaseOrderDetail> ApprovePurchaseOrderAsync(long orderId, CancellationToken ct = default) =>
        api.PostAsync<object, PurchaseOrderDetail>($"purchasing/orders/{orderId}/approve", new { }, ct);

    public Task<PurchaseOrderDetail> CancelPurchaseOrderAsync(long orderId, CancellationToken ct = default) =>
        api.PostAsync<object, PurchaseOrderDetail>($"purchasing/orders/{orderId}/cancel", new { }, ct);

    public Task<IReadOnlyList<GoodsReceiptSummary>> GetGoodsReceiptsAsync(
        long? supplierId = null, CancellationToken ct = default) =>
        api.PostAsync<OptionalLongRequest, IReadOnlyList<GoodsReceiptSummary>>(
            "purchasing/receipts/search", new(supplierId), ct);

    public Task<GoodsReceiptDetail?> GetGoodsReceiptAsync(long receiptId, CancellationToken ct = default) =>
        api.GetOptionalAsync<GoodsReceiptDetail>($"purchasing/receipts/{receiptId}", ct);

    public Task<GoodsReceiptDetail> PostGoodsReceiptAsync(GoodsReceiptPostRequest request, CancellationToken ct = default) =>
        api.PostAsync<GoodsReceiptPostRequest, GoodsReceiptDetail>("purchasing/receipts", request, ct);

    public Task<PurchaseReturnResult> PostPurchaseReturnAsync(PurchaseReturnRequest request, CancellationToken ct = default) =>
        api.PostAsync<PurchaseReturnRequest, PurchaseReturnResult>("purchasing/returns", request, ct);

    public Task<SupplierPaymentResult> RecordSupplierPaymentAsync(SupplierPaymentRequest request, CancellationToken ct = default) =>
        api.PostAsync<SupplierPaymentRequest, SupplierPaymentResult>("purchasing/payments", request, ct);

    public Task<IReadOnlyList<PurchasePaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<PurchasePaymentMethodOption>>("purchasing/payment-methods", ct);
}

public sealed class LanCashManagementService(LanApiClient api) : ICashManagementService
{
    public Task<IReadOnlyList<CashTerminalOption>> GetTerminalsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CashTerminalOption>>("cash/terminals", ct);

    public Task<CashShiftDetail?> GetCurrentShiftAsync(CancellationToken ct = default) =>
        api.GetOptionalAsync<CashShiftDetail>("cash/current-shift", ct);

    public Task<IReadOnlyList<CashShiftSummary>> GetShiftsAsync(int take = 100, CancellationToken ct = default) =>
        api.PostAsync<TakeRequest, IReadOnlyList<CashShiftSummary>>("cash/shifts/search", new(take), ct);

    public Task<CashShiftDetail?> GetShiftAsync(long shiftId, CancellationToken ct = default) =>
        api.GetOptionalAsync<CashShiftDetail>($"cash/shifts/{shiftId}", ct);

    public Task<CashShiftDetail> OpenShiftAsync(ShiftOpenRequest request, CancellationToken ct = default) =>
        api.PostAsync<ShiftOpenRequest, CashShiftDetail>("cash/shifts/open", request, ct);

    public Task<CashMovementRow> RecordManualMovementAsync(ManualCashMovementRequest request, CancellationToken ct = default) =>
        api.PostAsync<ManualCashMovementRequest, CashMovementRow>("cash/movements", request, ct);

    public Task<ShiftClosureResult> CloseShiftAsync(ShiftCloseRequest request, CancellationToken ct = default) =>
        api.PostAsync<ShiftCloseRequest, ShiftClosureResult>("cash/shifts/close", request, ct);

    public Task<CashShiftDetail> ReopenShiftAsync(long shiftId, string reason, CancellationToken ct = default) =>
        api.PostAsync<ReasonRequest, CashShiftDetail>($"cash/shifts/{shiftId}/reopen", new(reason), ct);

    public Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<ExpenseCategoryOption>>("cash/expense-categories", ct);

    public Task<IReadOnlyList<CashPaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CashPaymentMethodOption>>("cash/payment-methods", ct);

    public Task<IReadOnlyList<OperatingEntryRow>> GetOperatingEntriesAsync(
        string? entryType = null, int take = 300, CancellationToken ct = default) =>
        api.PostAsync<OperatingSearchRequest, IReadOnlyList<OperatingEntryRow>>(
            "cash/operating/search", new(entryType, take), ct);

    public Task<OperatingEntryRow> RecordOperatingEntryAsync(OperatingEntryRequest request, CancellationToken ct = default) =>
        api.PostAsync<OperatingEntryRequest, OperatingEntryRow>("cash/operating", request, ct);
}

public sealed class LanBusinessDayClosingService(LanApiClient api) : IBusinessDayClosingService
{
    public Task<BusinessDaySummary> GetSummaryAsync(DateTime businessDate, CancellationToken ct = default) =>
        api.PostAsync<DateRequest, BusinessDaySummary>("closing/summary", new(businessDate), ct);

    public Task<IReadOnlyList<BusinessDayRow>> GetBusinessDaysAsync(int take = 120, CancellationToken ct = default) =>
        api.PostAsync<TakeRequest, IReadOnlyList<BusinessDayRow>>("closing/days/search", new(take), ct);

    public Task<IReadOnlyList<BusinessDayClosureRow>> GetClosuresAsync(DateTime businessDate, CancellationToken ct = default) =>
        api.PostAsync<DateRequest, IReadOnlyList<BusinessDayClosureRow>>("closing/revisions", new(businessDate), ct);

    public Task<BusinessDayClosureRow> CloseAsync(BusinessDayCloseRequest request, CancellationToken ct = default) =>
        api.PostAsync<BusinessDayCloseRequest, BusinessDayClosureRow>("closing/close", request, ct);

    public Task<BusinessDayRow> ReopenAsync(DateTime businessDate, string reason, CancellationToken ct = default) =>
        api.PostAsync<DateReasonRequest, BusinessDayRow>("closing/reopen", new(businessDate, reason), ct);
}

public sealed class LanReportingService(LanApiClient api) : IReportingService
{
    public Task<ReportLookups> GetLookupsAsync(CancellationToken ct = default) =>
        api.GetAsync<ReportLookups>("reports/lookups", ct);

    public Task<ReportSnapshot> BuildAsync(ReportFilters filters, CancellationToken ct = default) =>
        api.PostAsync<ReportFilters, ReportSnapshot>("reports/build", filters, ct);

    public Task<IReadOnlyList<SalesExportRow>> GetSalesExportAsync(ReportFilters filters, CancellationToken ct = default) =>
        api.PostAsync<ReportFilters, IReadOnlyList<SalesExportRow>>("reports/sales-export", filters, ct);
}

internal sealed record CatalogSearchRequest(string? Search, long? CategoryId, bool? IsActive);
internal sealed record InventoryStockSearchRequest(string? Search, bool LowStockOnly);
internal sealed record InventoryMovementSearchRequest(long? ProductId, int Take);
internal sealed record IdempotencyRequest(string IdempotencyKey);
internal sealed record PosSearchRequest(string Query, int Take);
internal sealed record OpeningCashRequest(decimal OpeningCash);
internal sealed record SearchTakeRequest(string? Search, int Take);
internal sealed record OptionalLongRequest(long? Id);
internal sealed record CustomerSearchRequest(string? Search, bool ActiveOnly);
internal sealed record SupplierSearchRequest(string? Search, bool ActiveOnly);
internal sealed record SimpleSearchRequest(string? Search);
internal sealed record TakeRequest(int Take);
internal sealed record ReasonRequest(string Reason);
internal sealed record OperatingSearchRequest(string? EntryType, int Take);
internal sealed record DateRequest(DateTime Date);
internal sealed record DateReasonRequest(DateTime Date, string Reason);
