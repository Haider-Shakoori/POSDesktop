using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Reports;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalReportingService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IReportingService
{
    public async Task<ReportLookupData> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("reports.view");
        var locale = RequireUser().PreferredLocale;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var customers = await context.Customers.AsNoTracking()
            .OrderBy(x => x.Name).Select(x => new ReportLookupOption(x.Id, x.Name))
            .ToListAsync(cancellationToken);
        var suppliers = await context.Suppliers.AsNoTracking()
            .OrderBy(x => x.Name).Select(x => new ReportLookupOption(x.Id, x.Name))
            .ToListAsync(cancellationToken);
        var productsRaw = await context.Products.AsNoTracking()
            .OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var categoriesRaw = await context.Categories.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NameEn).ToListAsync(cancellationToken);

        return new ReportLookupData(
            customers,
            suppliers,
            productsRaw.Select(x => new ReportLookupOption(
                x.Id, x.Sku + " · " + Localize(x.NameEn, x.NameFa, x.NamePs, locale))).ToList(),
            categoriesRaw.Select(x => new ReportLookupOption(
                x.Id, Localize(x.NameEn, x.NameFa, x.NamePs, locale))).ToList());
    }

    public async Task<ReportResult> BuildAsync(
        ReportFilters filters,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("reports.view");
        var user = RequireUser();
        var canViewProfit = authorizer.HasPermission("reports.profit");
        var from = filters.From.Date;
        var to = filters.To.Date;
        if (to < from) throw new InvalidOperationException("Report end date cannot be before the start date.");
        if ((to - from).TotalDays > 3660)
            throw new InvalidOperationException("Report range cannot exceed ten years.");

        filters = filters with { From = from, To = to };

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var sales = await context.Sales.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        if (filters.CustomerId is not null)
            sales = sales.Where(x => x.CustomerId == filters.CustomerId).ToList();

        var saleIds = sales.Select(x => x.Id).ToList();
        var saleItems = saleIds.Count == 0
            ? []
            : await context.SaleItems.AsNoTracking().Where(x => saleIds.Contains(x.SaleId)).ToListAsync(cancellationToken);

        var products = await context.Products.AsNoTracking().ToListAsync(cancellationToken);
        var productMap = products.ToDictionary(x => x.Id);
        var categories = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var categoryMap = categories.ToDictionary(x => x.Id);

        bool ItemAllowed(long productId)
        {
            if (!productMap.TryGetValue(productId, out var product)) return false;
            if (filters.ProductId is not null && productId != filters.ProductId.Value) return false;
            if (filters.CategoryId is not null && product.CategoryId != filters.CategoryId.Value) return false;
            return true;
        }

        var itemFiltered = filters.ProductId is not null || filters.CategoryId is not null;
        var filteredSaleItems = saleItems.Where(x => ItemAllowed(x.ProductId)).ToList();
        if (itemFiltered)
        {
            var allowedSaleIds = filteredSaleItems.Select(x => x.SaleId).Distinct().ToHashSet();
            sales = sales.Where(x => allowedSaleIds.Contains(x.Id)).ToList();
        }

        var returns = await context.SaleReturns.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var saleById = sales.ToDictionary(x => x.Id);
        if (filters.CustomerId is not null || itemFiltered)
            returns = returns.Where(x => saleById.ContainsKey(x.SaleId)).ToList();

        var returnIds = returns.Select(x => x.Id).ToList();
        var returnItems = returnIds.Count == 0
            ? []
            : await context.SaleReturnItems.AsNoTracking()
                .Where(x => returnIds.Contains(x.SaleReturnId)).ToListAsync(cancellationToken);
        var saleItemById = saleItems.ToDictionary(x => x.Id);

        var filteredReturnItems = returnItems.Where(x =>
            saleItemById.TryGetValue(x.SaleItemId, out var item) && ItemAllowed(item.ProductId)).ToList();
        if (itemFiltered)
        {
            var allowedReturnIds = filteredReturnItems.Select(x => x.SaleReturnId).Distinct().ToHashSet();
            returns = returns.Where(x => allowedReturnIds.Contains(x.Id)).ToList();
        }

        decimal salesSubtotal;
        decimal lineDiscount;
        decimal saleDiscount;
        decimal salesNet;
        decimal salesCogs;
        int salesCount;

        if (itemFiltered)
        {
            salesCount = filteredSaleItems.Select(x => x.SaleId).Distinct().Count();
            salesSubtotal = Money(filteredSaleItems.Sum(x => x.LineSubtotal));
            lineDiscount = Money(filteredSaleItems.Sum(x => x.LineDiscountAmount));
            saleDiscount = Money(filteredSaleItems.Sum(x => x.AllocatedSaleDiscount));
            salesNet = Money(filteredSaleItems.Sum(x => x.LineNetTotal));
            salesCogs = Money(filteredSaleItems.Sum(x => x.CogsAmount));
        }
        else
        {
            salesCount = sales.Count;
            salesSubtotal = Money(sales.Sum(x => x.Subtotal));
            lineDiscount = Money(sales.Sum(x => x.LineDiscountTotal));
            saleDiscount = Money(sales.Sum(x => x.SaleDiscountAmount));
            salesNet = Money(sales.Sum(x => x.NetTotal));
            salesCogs = Money(sales.Sum(x => x.CogsTotal));
        }

        var returnTotal = itemFiltered
            ? Money(filteredReturnItems.Sum(x => x.ReturnAmount))
            : Money(returns.Sum(x => x.ReturnTotal));
        var cogsReversed = itemFiltered
            ? Money(filteredReturnItems.Sum(x => x.CogsAmount))
            : Money(returns.Sum(x => x.CogsReversed));
        var netSales = Money(salesNet - returnTotal);
        var netCogs = Money(salesCogs - cogsReversed);
        var grossProfit = Money(netSales - netCogs);

        var operating = await context.OperatingEntries.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var expenses = Money(operating.Where(x => x.EntryType == "expense").Sum(x => x.Amount));
        var otherIncome = Money(operating.Where(x => x.EntryType == "income").Sum(x => x.Amount));
        var netProfit = Money(grossProfit + otherIncome - expenses);

        var receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var purchaseReturns = await context.PurchaseReturns.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var purchasePayments = await context.PurchasePayments.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var supplierPayments = await context.SupplierPayments.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        if (filters.SupplierId is not null)
        {
            receipts = receipts.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
            purchaseReturns = purchaseReturns.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
            purchasePayments = purchasePayments.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
            supplierPayments = supplierPayments.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
        }

        var collections = await context.CustomerCollections.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        if (filters.CustomerId is not null)
            collections = collections.Where(x => x.CustomerId == filters.CustomerId.Value).ToList();

        var customers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var suppliers = await context.Suppliers.AsNoTracking().ToListAsync(cancellationToken);
        var selectedCustomers = filters.CustomerId is null
            ? customers
            : customers.Where(x => x.Id == filters.CustomerId.Value).ToList();
        var selectedSuppliers = filters.SupplierId is null
            ? suppliers
            : suppliers.Where(x => x.Id == filters.SupplierId.Value).ToList();

        var costLayers = await context.InventoryCostLayers.AsNoTracking()
            .Where(x => x.RemainingQuantityBase > 0m).ToListAsync(cancellationToken);
        var inventoryValue = Money(costLayers.Sum(x => x.RemainingQuantityBase * x.UnitCostBase));

        var writeoffs = await context.InventoryWriteoffs.AsNoTracking().ToListAsync(cancellationToken);
        writeoffs = writeoffs.Where(x =>
        {
            var day = BusinessDayGuard.LocalBusinessDate(x.PostedAt);
            return day >= from && day <= to;
        }).ToList();

        var summary = new ReportSummary(
            salesCount,
            salesSubtotal,
            Money(lineDiscount + saleDiscount),
            salesNet,
            returnTotal,
            netSales,
            canViewProfit ? salesCogs : null,
            canViewProfit ? cogsReversed : null,
            canViewProfit ? netCogs : null,
            canViewProfit ? grossProfit : null,
            expenses,
            otherIncome,
            canViewProfit ? netProfit : null,
            Money(receipts.Sum(x => x.NetTotal)),
            Money(purchaseReturns.Sum(x => x.ReturnTotal)),
            Money(collections.Sum(x => x.Amount)),
            Money(purchasePayments.Sum(x => x.Amount) + supplierPayments.Sum(x => x.Amount)),
            salesCount == 0 ? 0m : Money(netSales / salesCount),
            Money(selectedCustomers.Sum(x => Math.Max(0m, x.CurrentBalance))),
            Money(selectedSuppliers.Sum(x => Math.Max(0m, x.CurrentBalance))),
            inventoryValue,
            Money(writeoffs.Where(x => x.WriteoffType == "damage").Sum(x => x.TotalCost)),
            Money(writeoffs.Where(x => x.WriteoffType == "expiry").Sum(x => x.TotalCost)));

        var returnsBySaleItem = filteredReturnItems
            .GroupBy(x => x.SaleItemId)
            .ToDictionary(x => x.Key, x => new
            {
                Qty = x.Sum(v => v.QuantityBase),
                Value = x.Sum(v => v.ReturnAmount),
                Cogs = x.Sum(v => v.CogsAmount),
            });

        var performance = filteredSaleItems
            .GroupBy(x => x.ProductId)
            .Select(g =>
            {
                var returned = g.Sum(item => returnsBySaleItem.GetValueOrDefault(item.Id)?.Qty ?? 0m);
                var returnValue = g.Sum(item => returnsBySaleItem.GetValueOrDefault(item.Id)?.Value ?? 0m);
                var returnedCogs = g.Sum(item => returnsBySaleItem.GetValueOrDefault(item.Id)?.Cogs ?? 0m);
                var product = productMap[g.Key];
                var category = product.CategoryId is not null && categoryMap.TryGetValue(product.CategoryId.Value, out var cat)
                    ? Localize(cat.NameEn, cat.NameFa, cat.NamePs, user.PreferredLocale)
                    : null;
                var netProductSales = Money(g.Sum(x => x.LineNetTotal) - returnValue);
                var productCogs = Money(g.Sum(x => x.CogsAmount) - returnedCogs);
                return new ProductPerformanceRow(
                    product.Id, product.Sku,
                    Localize(product.NameEn, product.NameFa, product.NamePs, user.PreferredLocale),
                    category,
                    Quantity(g.Sum(x => x.QuantityBase) - returned),
                    netProductSales,
                    canViewProfit ? productCogs : null,
                    canViewProfit ? Money(netProductSales - productCogs) : null);
            }).ToList();

        var categoryProfit = performance
            .GroupBy(x => productMap[x.ProductId].CategoryId)
            .Select(g =>
            {
                var name = g.Key is not null && categoryMap.TryGetValue(g.Key.Value, out var cat)
                    ? Localize(cat.NameEn, cat.NameFa, cat.NamePs, user.PreferredLocale)
                    : "Uncategorized";
                var categorySales = Money(g.Sum(x => x.NetSales));
                var categoryCogs = Money(g.Sum(x => x.Cogs ?? 0m));
                return new CategoryProfitRow(
                    g.Key, name, categorySales,
                    canViewProfit ? categoryCogs : null,
                    canViewProfit ? Money(categorySales - categoryCogs) : null);
            })
            .OrderByDescending(x => x.NetSales).ToList();

        var returnByDay = returns
            .GroupBy(x => x.BusinessDate)
            .ToDictionary(x => x.Key, x => new { Value = x.Sum(v => v.ReturnTotal), Cogs = x.Sum(v => v.CogsReversed) });
        var trend = sales.GroupBy(x => x.BusinessDate)
            .Select(g =>
            {
                var r = returnByDay.GetValueOrDefault(g.Key);
                var daySales = Money(g.Sum(x => x.NetTotal) - (r?.Value ?? 0m));
                var dayCogs = Money(g.Sum(x => x.CogsTotal) - (r?.Cogs ?? 0m));
                return new SalesTrendRow(g.Key, g.Count(), daySales,
                    canViewProfit ? Money(daySales - dayCogs) : null);
            })
            .OrderBy(x => x.Day).ToList();

        var inventoryByProduct = costLayers.GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => Money(x.Sum(v => v.RemainingQuantityBase * v.UnitCostBase)));
        var inventory = products.Where(x => x.TrackStock)
            .Select(x => new InventoryReportRow(
                x.Id, x.Sku, Localize(x.NameEn, x.NameFa, x.NamePs, user.PreferredLocale),
                x.StockOnHand, x.MinimumStock, x.StockOnHand <= x.MinimumStock,
                inventoryByProduct.GetValueOrDefault(x.Id)))
            .OrderByDescending(x => x.LowStock).ThenBy(x => x.Product).ToList();

        var batches = await context.ProductBatches.AsNoTracking()
            .Where(x => x.StockOnHand > 0m && !x.IsBlocked && x.ExpiresAt != null)
            .ToListAsync(cancellationToken);
        var today = DateTime.Today;
        var expiring = batches.Select(x =>
        {
            var product = productMap.GetValueOrDefault(x.ProductId);
            return new ExpiryReportRow(
                x.Id, product?.Sku ?? "", product is null ? "Product" :
                    Localize(product.NameEn, product.NameFa, product.NamePs, user.PreferredLocale),
                x.BatchNumber, x.ExpiresAt, x.StockOnHand,
                x.ExpiresAt is null ? int.MaxValue : (x.ExpiresAt.Value.Date - today).Days);
        }).Where(x => x.DaysRemaining <= 90)
          .OrderBy(x => x.DaysRemaining).ThenBy(x => x.Product).ToList();

        var days = await context.BusinessDays.AsNoTracking()
            .Where(x => x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(cancellationToken);
        var dayMap = days.ToDictionary(x => x.Id);
        var closures = dayMap.Count == 0
            ? []
            : await context.BusinessDayClosures.AsNoTracking()
                .Where(x => dayMap.Keys.Contains(x.BusinessDayId))
                .ToListAsync(cancellationToken);
        var closingHistory = closures
            .OrderByDescending(x => dayMap[x.BusinessDayId].BusinessDate)
            .ThenByDescending(x => x.Version)
            .Select(x => new ClosingHistoryRow(
                dayMap[x.BusinessDayId].BusinessDate, x.Number, x.Version,
                x.NetSalesTotal, canViewProfit ? x.NetProfitTotal : null,
                x.ExpectedCashTotal, x.ActualCashTotal, x.VarianceTotal, x.ClosedAt))
            .ToList();

        var peakHours = sales.GroupBy(x => x.SoldAt.ToLocalTime().Hour)
            .Select(g => new PeakHourRow(g.Key, g.Count(), Money(g.Sum(x => x.NetTotal))))
            .OrderByDescending(x => x.NetSales).ThenBy(x => x.Hour).ToList();

        var weekdays = sales.GroupBy(x => (int)x.SoldAt.ToLocalTime().DayOfWeek)
            .Select(g => new WeekdayPerformanceRow(
                g.Key, ((DayOfWeek)g.Key).ToString(), g.Count(), Money(g.Sum(x => x.NetTotal))))
            .OrderBy(x => x.DayOfWeek).ToList();

        return new ReportResult(
            filters, canViewProfit, summary, trend,
            performance.OrderByDescending(x => x.QuantityBase).Take(10).ToList(),
            performance.OrderBy(x => x.QuantityBase).Take(10).ToList(),
            categoryProfit,
            selectedCustomers.Where(x => x.CurrentBalance > 0m)
                .OrderByDescending(x => x.CurrentBalance)
                .Select(x => new BalanceReportRow(x.Id, x.Name, x.CurrentBalance)).ToList(),
            selectedSuppliers.Where(x => x.CurrentBalance > 0m)
                .OrderByDescending(x => x.CurrentBalance)
                .Select(x => new BalanceReportRow(x.Id, x.Name, x.CurrentBalance)).ToList(),
            inventory, expiring, closingHistory, peakHours, weekdays);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var user = RequireUser();
        var canViewSales = authorizer.HasPermission("sales.view");
        var canViewCustomers = authorizer.HasPermission("customers.view");
        var canViewSuppliers = authorizer.HasPermission("suppliers.view");
        var canViewInventory = authorizer.HasPermission("inventory.view");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var today = DateTime.Today;

        decimal? todayNetSales = null;
        int? todayTransactions = null;
        List<DashboardRecentSale> recentSales = [];

        if (canViewSales)
        {
            var sales = await context.Sales.AsNoTracking()
                .Where(x => x.BusinessDate == today)
                .ToListAsync(cancellationToken);
            var returns = await context.SaleReturns.AsNoTracking()
                .Where(x => x.BusinessDate == today)
                .ToListAsync(cancellationToken);
            todayNetSales = Money(sales.Sum(x => x.NetTotal) - returns.Sum(x => x.ReturnTotal));
            todayTransactions = sales.Count;

            recentSales = (await context.Sales.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(6).ToListAsync(cancellationToken))
                .Select(x => new DashboardRecentSale(
                    x.Id, x.Number, x.CustomerNameSnapshot, x.NetTotal,
                    x.ReturnedTotal, x.PaymentStatus, x.SoldAt)).ToList();
        }

        decimal? receivables = canViewCustomers
            ? Money((await context.Customers.AsNoTracking().ToListAsync(cancellationToken))
                .Where(x => x.CurrentBalance > 0m).Sum(x => x.CurrentBalance))
            : null;
        decimal? payables = canViewSuppliers
            ? Money((await context.Suppliers.AsNoTracking().ToListAsync(cancellationToken))
                .Where(x => x.CurrentBalance > 0m).Sum(x => x.CurrentBalance))
            : null;

        int? lowStockCount = null;
        List<DashboardLowStock> lowStockProducts = [];
        if (canViewInventory)
        {
            var products = await context.Products.AsNoTracking()
                .Where(x => x.IsActive && x.TrackStock)
                .ToListAsync(cancellationToken);
            var low = products.Where(x => x.StockOnHand <= x.MinimumStock)
                .OrderBy(x => x.StockOnHand).ThenBy(x => x.Sku).ToList();
            lowStockCount = low.Count;
            lowStockProducts = low.Take(6).Select(x => new DashboardLowStock(
                x.Id, x.Sku, Localize(x.NameEn, x.NameFa, x.NamePs, user.PreferredLocale),
                x.StockOnHand, x.MinimumStock)).ToList();
        }

        var shift = await context.CashierShifts.AsNoTracking()
            .Where(x => x.UserId == user.UserId && x.Status == "open")
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        string? terminalName = null;
        if (shift is not null)
            terminalName = await context.Terminals.AsNoTracking()
                .Where(x => x.Id == shift.TerminalId).Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken);

        return new DashboardSnapshot(
            canViewSales, canViewCustomers, canViewSuppliers, canViewInventory,
            todayNetSales, todayTransactions, receivables, payables,
            lowStockCount, shift?.ExpectedCash, terminalName,
            recentSales, lowStockProducts);
    }

    private BusinessOS.POS.Domain.Authentication.UserSessionSnapshot RequireUser() =>
        sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa!,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps!,
            _ => en,
        };
}
