using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;

namespace BusinessOS.POS.LocalServer.Api;

public static class LanOperationalEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (IDashboardService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        api.MapGet("/catalog/references", async (IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.GetReferenceDataAsync(ct)));
        api.MapPost("/catalog/products/search", async (CatalogSearchRequest request, IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.GetProductsAsync(request.Search, request.CategoryId, request.IsActive, ct)));
        api.MapGet("/catalog/products/{id:long}", async (long id, IProductCatalogService service, CancellationToken ct) =>
        {
            var result = await service.GetProductAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/catalog/products/save", async (CatalogProductSaveRequest request, IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.SaveProductAsync(request, ct)));
        api.MapPost("/catalog/categories/save", async (CatalogCategorySaveRequest request, IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.SaveCategoryAsync(request, ct)));
        api.MapPost("/catalog/brands/save", async (CatalogBrandSaveRequest request, IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.SaveBrandAsync(request, ct)));
        api.MapPost("/catalog/units/save", async (CatalogUnitSaveRequest request, IProductCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.SaveUnitAsync(request, ct)));

        api.MapGet("/inventory/references", async (IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetReferenceDataAsync(ct)));
        api.MapPost("/inventory/stock/search", async (InventoryStockSearchRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetStockAsync(request.Search, request.LowStockOnly, ct)));
        api.MapPost("/inventory/movements/search", async (InventoryMovementSearchRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetMovementsAsync(request.ProductId, request.Take, ct)));
        api.MapPost("/inventory/opening-stock", async (OpeningStockRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.RecordOpeningStockAsync(request, ct)));
        api.MapPost("/inventory/adjust", async (StockAdjustmentRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.AdjustStockAsync(request, ct)));
        api.MapPost("/inventory/counts", async (CreateStockCountRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.CreateStockCountAsync(request, ct)));
        api.MapPost("/inventory/counts/{id:long}/approve", async (long id, IdempotencyRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveStockCountAsync(id, request.IdempotencyKey, ct)));
        api.MapGet("/inventory/counts", async (IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetStockCountsAsync(ct)));
        api.MapPost("/inventory/writeoffs", async (InventoryWriteoffRequest request, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.PostWriteoffAsync(request, ct)));
        api.MapGet("/inventory/writeoffs", async (IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.GetWriteoffsAsync(ct)));

        api.MapGet("/pos/references", async (IPosService service, CancellationToken ct) =>
            Results.Ok(await service.GetReferenceDataAsync(ct)));
        api.MapPost("/pos/products/search", async (PosSearchRequest request, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.SearchProductsAsync(request.Query, request.Take, ct)));
        api.MapPost("/pos/open-shift", async (OpeningCashRequest request, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.OpenShiftAsync(request.OpeningCash, ct)));
        api.MapPost("/pos/checkout", async (PosCheckoutRequest request, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.CheckoutAsync(request, ct)));
        api.MapPost("/pos/hold", async (PosHoldRequest request, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.HoldAsync(request, ct)));
        api.MapGet("/pos/held", async (IPosService service, CancellationToken ct) =>
            Results.Ok(await service.GetHeldSalesAsync(ct)));
        api.MapGet("/pos/held/{id:long}", async (long id, IPosService service, CancellationToken ct) =>
            Results.Ok(await service.ResumeHeldSaleAsync(id, ct)));
        api.MapPost("/pos/held/{id:long}/release", async (long id, IPosService service, CancellationToken ct) =>
        {
            await service.ReleaseHeldSaleAsync(id, ct);
            return Results.NoContent();
        });

        api.MapPost("/sales/search", async (SearchTakeRequest request, ISalesService service, CancellationToken ct) =>
            Results.Ok(await service.GetSalesAsync(request.Search, request.Take, ct)));
        api.MapGet("/sales/{id:long}", async (long id, ISalesService service, CancellationToken ct) =>
        {
            var result = await service.GetSaleAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapGet("/sales/{id:long}/receipt", async (long id, ISalesService service, CancellationToken ct) =>
        {
            var result = await service.GetReceiptAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/sales/returns", async (SaleReturnRequest request, ISaleReturnService service, CancellationToken ct) =>
            Results.Ok(await service.ReturnAsync(request, ct)));
        api.MapPost("/sales/void", async (SaleVoidRequest request, ISaleReturnService service, CancellationToken ct) =>
            Results.Ok(await service.VoidAsync(request, ct)));
        api.MapPost("/sales/returns/search", async (OptionalLongRequest request, ISaleReturnService service, CancellationToken ct) =>
            Results.Ok(await service.GetReturnsAsync(request.Id, ct)));
        api.MapGet("/sales/refund-methods", async (ISaleReturnService service, CancellationToken ct) =>
            Results.Ok(await service.GetRefundMethodsAsync(ct)));

        api.MapPost("/customers/search", async (CustomerSearchRequest request, ICustomerService service, CancellationToken ct) =>
            Results.Ok(await service.GetCustomersAsync(request.Search, request.ActiveOnly, ct)));
        api.MapGet("/customers/{id:long}", async (long id, ICustomerService service, CancellationToken ct) =>
        {
            var result = await service.GetCustomerAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/customers/save", async (CustomerSaveRequest request, ICustomerService service, CancellationToken ct) =>
            Results.Ok(await service.SaveCustomerAsync(request, ct)));
        api.MapPost("/customers/collect", async (CustomerCollectionRequest request, ICustomerService service, CancellationToken ct) =>
            Results.Ok(await service.CollectAsync(request, ct)));
        api.MapGet("/customers/payment-methods", async (ICustomerService service, CancellationToken ct) =>
            Results.Ok(await service.GetPaymentMethodsAsync(ct)));

        api.MapPost("/purchasing/suppliers/search", async (SupplierSearchRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.GetSuppliersAsync(request.Search, request.ActiveOnly, ct)));
        api.MapGet("/purchasing/suppliers/{id:long}", async (long id, IPurchasingService service, CancellationToken ct) =>
        {
            var result = await service.GetSupplierAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/purchasing/suppliers/save", async (SupplierSaveRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.SaveSupplierAsync(request, ct)));
        api.MapPost("/purchasing/products/search", async (SimpleSearchRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.GetPurchasableProductsAsync(request.Search, ct)));
        api.MapPost("/purchasing/orders/search", async (OptionalLongRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.GetPurchaseOrdersAsync(request.Id, ct)));
        api.MapGet("/purchasing/orders/{id:long}", async (long id, IPurchasingService service, CancellationToken ct) =>
        {
            var result = await service.GetPurchaseOrderAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/purchasing/orders", async (PurchaseOrderCreateRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.CreatePurchaseOrderAsync(request, ct)));
        api.MapPost("/purchasing/orders/{id:long}/approve", async (long id, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.ApprovePurchaseOrderAsync(id, ct)));
        api.MapPost("/purchasing/orders/{id:long}/cancel", async (long id, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.CancelPurchaseOrderAsync(id, ct)));
        api.MapPost("/purchasing/receipts/search", async (OptionalLongRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.GetGoodsReceiptsAsync(request.Id, ct)));
        api.MapGet("/purchasing/receipts/{id:long}", async (long id, IPurchasingService service, CancellationToken ct) =>
        {
            var result = await service.GetGoodsReceiptAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/purchasing/receipts", async (GoodsReceiptPostRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.PostGoodsReceiptAsync(request, ct)));
        api.MapPost("/purchasing/returns", async (PurchaseReturnRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.PostPurchaseReturnAsync(request, ct)));
        api.MapPost("/purchasing/payments", async (SupplierPaymentRequest request, IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.RecordSupplierPaymentAsync(request, ct)));
        api.MapGet("/purchasing/payment-methods", async (IPurchasingService service, CancellationToken ct) =>
            Results.Ok(await service.GetPaymentMethodsAsync(ct)));

        api.MapGet("/cash/terminals", async (ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetTerminalsAsync(ct)));
        api.MapGet("/cash/current-shift", async (ICashManagementService service, CancellationToken ct) =>
        {
            var result = await service.GetCurrentShiftAsync(ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });
        api.MapPost("/cash/shifts/search", async (TakeRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetShiftsAsync(request.Take, ct)));
        api.MapGet("/cash/shifts/{id:long}", async (long id, ICashManagementService service, CancellationToken ct) =>
        {
            var result = await service.GetShiftAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        api.MapPost("/cash/shifts/open", async (ShiftOpenRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.OpenShiftAsync(request, ct)));
        api.MapPost("/cash/movements", async (ManualCashMovementRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.RecordManualMovementAsync(request, ct)));
        api.MapPost("/cash/shifts/close", async (ShiftCloseRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.CloseShiftAsync(request, ct)));
        api.MapPost("/cash/shifts/{id:long}/reopen", async (long id, ReasonRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.ReopenShiftAsync(id, request.Reason, ct)));
        api.MapGet("/cash/expense-categories", async (ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetExpenseCategoriesAsync(ct)));
        api.MapGet("/cash/payment-methods", async (ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetPaymentMethodsAsync(ct)));
        api.MapPost("/cash/operating/search", async (OperatingSearchRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetOperatingEntriesAsync(request.EntryType, request.Take, ct)));
        api.MapPost("/cash/operating", async (OperatingEntryRequest request, ICashManagementService service, CancellationToken ct) =>
            Results.Ok(await service.RecordOperatingEntryAsync(request, ct)));

        api.MapPost("/closing/summary", async (DateRequest request, IBusinessDayClosingService service, CancellationToken ct) =>
            Results.Ok(await service.GetSummaryAsync(request.Date, ct)));
        api.MapPost("/closing/days/search", async (TakeRequest request, IBusinessDayClosingService service, CancellationToken ct) =>
            Results.Ok(await service.GetBusinessDaysAsync(request.Take, ct)));
        api.MapPost("/closing/revisions", async (DateRequest request, IBusinessDayClosingService service, CancellationToken ct) =>
            Results.Ok(await service.GetClosuresAsync(request.Date, ct)));
        api.MapPost("/closing/close", async (BusinessDayCloseRequest request, IBusinessDayClosingService service, CancellationToken ct) =>
            Results.Ok(await service.CloseAsync(request, ct)));
        api.MapPost("/closing/reopen", async (DateReasonRequest request, IBusinessDayClosingService service, CancellationToken ct) =>
            Results.Ok(await service.ReopenAsync(request.Date, request.Reason, ct)));

        api.MapGet("/reports/lookups", async (IReportingService service, CancellationToken ct) =>
            Results.Ok(await service.GetLookupsAsync(ct)));
        api.MapPost("/reports/build", async (ReportFilters request, IReportingService service, CancellationToken ct) =>
            Results.Ok(await service.BuildAsync(request, ct)));
        api.MapPost("/reports/sales-export", async (ReportFilters request, IReportingService service, CancellationToken ct) =>
            Results.Ok(await service.GetSalesExportAsync(request, ct)));
    }
}

public sealed record CatalogSearchRequest(string? Search, long? CategoryId, bool? IsActive);
public sealed record InventoryStockSearchRequest(string? Search, bool LowStockOnly);
public sealed record InventoryMovementSearchRequest(long? ProductId, int Take);
public sealed record IdempotencyRequest(string IdempotencyKey);
public sealed record PosSearchRequest(string Query, int Take);
public sealed record OpeningCashRequest(decimal OpeningCash);
public sealed record SearchTakeRequest(string? Search, int Take);
public sealed record OptionalLongRequest(long? Id);
public sealed record CustomerSearchRequest(string? Search, bool ActiveOnly);
public sealed record SupplierSearchRequest(string? Search, bool ActiveOnly);
public sealed record SimpleSearchRequest(string? Search);
public sealed record TakeRequest(int Take);
public sealed record ReasonRequest(string Reason);
public sealed record OperatingSearchRequest(string? EntryType, int Take);
public sealed record DateRequest(DateTime Date);
public sealed record DateReasonRequest(DateTime Date, string Reason);
