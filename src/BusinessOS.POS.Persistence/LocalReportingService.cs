using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalReportingService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IReportingService
{
    private static readonly TimeSpan AfghanistanOffset = TimeSpan.FromMinutes(270);

    public async Task<ReportLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("reports.view");
        var locale = RequireUser().PreferredLocale;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var customers = (await context.Customers.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(x => new ReportLookupItem(x.Id, x.Phone ?? string.Empty, x.Name))
            .ToList();

        var suppliers = (await context.Suppliers.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(x => new ReportLookupItem(x.Id, x.Phone ?? string.Empty, x.Name))
            .ToList();

        var products = (await context.Products.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.NameEn).ThenBy(x => x.Id).ToListAsync(cancellationToken))
            .Select(x => new ReportLookupItem(
                x.Id, x.Sku, Localize(x.NameEn, x.NameFa, x.NamePs, locale)))
            .ToList();

        var categories = (await context.Categories.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.NameEn).ToListAsync(cancellationToken))
            .Select(x => new ReportLookupItem(
                x.Id, string.Empty, Localize(x.NameEn, x.NameFa, x.NamePs, locale)))
            .ToList();

        return new ReportLookups(customers, suppliers, products, categories);
    }

    public async Task<ReportSnapshot> BuildAsync(
        ReportFilters filters,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("reports.view");
        filters = Normalize(filters);
        var locale = RequireUser().PreferredLocale;
        var canProfit = authorizer.HasPermission("reports.profit");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var sales = await context.Sales.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        if (filters.CustomerId is not null)
            sales = sales.Where(x => x.CustomerId == filters.CustomerId.Value).ToList();

        var returns = await context.SaleReturns.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);

        var relatedSaleIds = sales.Select(x => x.Id)
            .Concat(returns.Select(x => x.SaleId))
            .Distinct()
            .ToList();
        List<SaleEntity> relatedSales = relatedSaleIds.Count == 0
            ? []
            : await context.Sales.AsNoTracking()
                .Where(x => relatedSaleIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var saleMap = relatedSales.ToDictionary(x => x.Id);

        if (filters.CustomerId is not null)
        {
            returns = returns
                .Where(x => saleMap.GetValueOrDefault(x.SaleId)?.CustomerId == filters.CustomerId.Value)
                .ToList();
        }

        var inRangeSaleIds = sales.Select(x => x.Id).Distinct().ToList();
        List<SaleItemEntity> saleItems = inRangeSaleIds.Count == 0
            ? []
            : await context.SaleItems.AsNoTracking()
                .Where(x => inRangeSaleIds.Contains(x.SaleId))
                .ToListAsync(cancellationToken);

        var returnIds = returns.Select(x => x.Id).ToList();
        List<SaleReturnItemEntity> returnItems = returnIds.Count == 0
            ? []
            : await context.SaleReturnItems.AsNoTracking()
                .Where(x => returnIds.Contains(x.SaleReturnId))
                .ToListAsync(cancellationToken);

        var returnSourceItemIds = returnItems.Select(x => x.SaleItemId).Distinct().ToList();
        List<SaleItemEntity> returnSourceItems = returnSourceItemIds.Count == 0
            ? []
            : await context.SaleItems.AsNoTracking()
                .Where(x => returnSourceItemIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

        var allReferencedItems = saleItems
            .Concat(returnSourceItems)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        var productIds = allReferencedItems.Select(x => x.ProductId).Distinct().ToList();
        List<ProductEntity> products = productIds.Count == 0
            ? []
            : await context.Products.AsNoTracking()
                .Include(x => x.Category)
                .Where(x => productIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var productMap = products.ToDictionary(x => x.Id);
        var itemMap = allReferencedItems.ToDictionary(x => x.Id);

        bool ItemMatches(SaleItemEntity item)
        {
            if (filters.ProductId is not null && item.ProductId != filters.ProductId.Value)
                return false;
            if (filters.CategoryId is not null &&
                productMap.GetValueOrDefault(item.ProductId)?.CategoryId != filters.CategoryId.Value)
                return false;
            return true;
        }

        var itemFiltered = filters.ProductId is not null || filters.CategoryId is not null;
        var scopedItems = itemFiltered ? saleItems.Where(ItemMatches).ToList() : saleItems;
        var scopedSaleIds = scopedItems.Select(x => x.SaleId).Distinct().ToHashSet();
        var scopedSales = itemFiltered
            ? sales.Where(x => scopedSaleIds.Contains(x.Id)).ToList()
            : sales;

        var scopedReturnItems = itemFiltered
            ? returnItems.Where(x =>
                itemMap.TryGetValue(x.SaleItemId, out var item) && ItemMatches(item)).ToList()
            : returnItems;
        var scopedReturnIds = scopedReturnItems.Select(x => x.SaleReturnId).Distinct().ToHashSet();
        var scopedReturns = itemFiltered
            ? returns.Where(x => scopedReturnIds.Contains(x.Id)).ToList()
            : returns;

        var salesCount = scopedSales.Count;
        var subtotal = itemFiltered ? Money(scopedItems.Sum(x => x.LineSubtotal)) : Money(scopedSales.Sum(x => x.Subtotal));
        var discounts = itemFiltered
            ? Money(scopedItems.Sum(x => x.LineDiscountAmount + x.AllocatedSaleDiscount))
            : Money(scopedSales.Sum(x => x.LineDiscountTotal + x.SaleDiscountAmount));
        var salesNet = itemFiltered ? Money(scopedItems.Sum(x => x.LineNetTotal)) : Money(scopedSales.Sum(x => x.NetTotal));
        var returnTotal = itemFiltered ? Money(scopedReturnItems.Sum(x => x.ReturnAmount)) : Money(scopedReturns.Sum(x => x.ReturnTotal));
        var netSales = Money(salesNet - returnTotal);
        var salesCogs = itemFiltered ? Money(scopedItems.Sum(x => x.CogsAmount)) : Money(scopedSales.Sum(x => x.CogsTotal));
        var cogsReversed = itemFiltered ? Money(scopedReturnItems.Sum(x => x.CogsAmount)) : Money(scopedReturns.Sum(x => x.CogsReversed));
        var netCogs = Money(salesCogs - cogsReversed);
        var grossProfit = Money(netSales - netCogs);

        var operating = await context.OperatingEntries.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        var expenses = Money(operating.Where(x => x.EntryType == "expense").Sum(x => x.Amount));
        var otherIncome = Money(operating.Where(x => x.EntryType == "income").Sum(x => x.Amount));
        var netProfit = Money(grossProfit + otherIncome - expenses);

        var receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        if (filters.SupplierId is not null)
            receipts = receipts.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();

        var purchaseReturns = await context.PurchaseReturns.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        if (filters.SupplierId is not null)
            purchaseReturns = purchaseReturns.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();

        var collections = await context.CustomerCollections.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        if (filters.CustomerId is not null)
            collections = collections.Where(x => x.CustomerId == filters.CustomerId.Value).ToList();

        var initialPayments = await context.PurchasePayments.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        var supplierPayments = await context.SupplierPayments.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        if (filters.SupplierId is not null)
        {
            initialPayments = initialPayments.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
            supplierPayments = supplierPayments.Where(x => x.SupplierId == filters.SupplierId.Value).ToList();
        }

        var customers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var suppliers = await context.Suppliers.AsNoTracking().ToListAsync(cancellationToken);
        var receivables = Money(customers
            .Where(x => filters.CustomerId is null || x.Id == filters.CustomerId.Value)
            .Sum(x => x.CurrentBalance));
        var payables = Money(suppliers
            .Where(x => filters.SupplierId is null || x.Id == filters.SupplierId.Value)
            .Sum(x => x.CurrentBalance));

        var costLayers = await context.InventoryCostLayers.AsNoTracking()
            .Where(x => x.RemainingQuantityBase > 0m)
            .ToListAsync(cancellationToken);
        var inventoryValue = Money(costLayers.Sum(x => x.RemainingQuantityBase * x.UnitCostBase));

        var writeoffs = await context.InventoryWriteoffs.AsNoTracking().ToListAsync(cancellationToken);
        var rangedWriteoffs = writeoffs.Where(x =>
        {
            var day = x.PostedAt.ToOffset(AfghanistanOffset).Date;
            return day >= filters.From && day <= filters.To;
        }).ToList();
        var damagedCost = Money(rangedWriteoffs.Where(x => x.WriteoffType == "damage").Sum(x => x.TotalCost));
        var expiredCost = Money(rangedWriteoffs.Where(x => x.WriteoffType == "expiry").Sum(x => x.TotalCost));

        var summary = new ReportSummary(
            salesCount,
            subtotal,
            discounts,
            salesNet,
            returnTotal,
            netSales,
            canProfit ? salesCogs : null,
            canProfit ? cogsReversed : null,
            canProfit ? netCogs : null,
            canProfit ? grossProfit : null,
            expenses,
            otherIncome,
            canProfit ? netProfit : null,
            Money(receipts.Sum(x => x.NetTotal)),
            Money(purchaseReturns.Sum(x => x.ReturnTotal)),
            Money(collections.Sum(x => x.Amount)),
            Money(initialPayments.Sum(x => x.Amount) + supplierPayments.Sum(x => x.Amount)),
            salesCount == 0 ? 0m : Money(netSales / salesCount),
            receivables,
            payables,
            canProfit ? inventoryValue : null,
            canProfit ? damagedCost : null,
            canProfit ? expiredCost : null);

        var salesByDay = itemFiltered
            ? scopedItems.GroupBy(x => saleMap[x.SaleId].BusinessDate)
                .ToDictionary(
                    g => g.Key,
                    g => new DayAccumulator(
                        g.Select(x => x.SaleId).Distinct().Count(),
                        Money(g.Sum(x => x.LineNetTotal)),
                        Money(g.Sum(x => x.CogsAmount))))
            : scopedSales.GroupBy(x => x.BusinessDate)
                .ToDictionary(
                    g => g.Key,
                    g => new DayAccumulator(
                        g.Count(),
                        Money(g.Sum(x => x.NetTotal)),
                        Money(g.Sum(x => x.CogsTotal))));

        var returnsByDay = itemFiltered
            ? scopedReturnItems.GroupBy(x => scopedReturns
                    .First(r => r.Id == x.SaleReturnId).BusinessDate)
                .ToDictionary(
                    g => g.Key,
                    g => new ReturnAccumulator(
                        Money(g.Sum(x => x.ReturnAmount)),
                        Money(g.Sum(x => x.CogsAmount))))
            : scopedReturns.GroupBy(x => x.BusinessDate)
                .ToDictionary(
                    g => g.Key,
                    g => new ReturnAccumulator(
                        Money(g.Sum(x => x.ReturnTotal)),
                        Money(g.Sum(x => x.CogsReversed))));

        var trendDates = salesByDay.Keys.Concat(returnsByDay.Keys).Distinct().OrderBy(x => x);
        var trend = trendDates.Select(day =>
        {
            var s = salesByDay.GetValueOrDefault(day) ?? new DayAccumulator(0, 0m, 0m);
            var r = returnsByDay.GetValueOrDefault(day) ?? new ReturnAccumulator(0m, 0m);
            var dayNet = Money(s.NetSales - r.Returns);
            var dayProfit = Money(dayNet - Money(s.Cogs - r.Cogs));
            return new SalesTrendRow(day, s.SalesCount, dayNet, canProfit ? dayProfit : null);
        }).ToList();

        var productRows = BuildProductPerformance(
            scopedItems, scopedReturnItems, itemMap, productMap, locale, canProfit);
        var topProducts = productRows
            .OrderByDescending(x => x.QuantityBase).ThenBy(x => x.Product)
            .Take(10).ToList();
        var slowProducts = productRows
            .OrderBy(x => x.QuantityBase).ThenBy(x => x.Product)
            .Take(10).ToList();

        var categoryProfit = BuildCategoryProfit(
            scopedItems, scopedReturnItems, itemMap, productMap, locale, canProfit);

        var customerRows = customers
            .Where(x => filters.CustomerId is null || x.Id == filters.CustomerId.Value)
            .Select(x =>
            {
                var customerSales = sales.Where(s => s.CustomerId == x.Id).ToList();
                return new CustomerActivityRow(
                    x.Id, x.Name, x.Phone, customerSales.Count,
                    Money(customerSales.Sum(s => s.NetTotal)), x.CurrentBalance);
            })
            .OrderByDescending(x => x.SalesTotal)
            .ThenBy(x => x.Customer)
            .Take(20).ToList();

        var supplierRows = suppliers
            .Where(x => filters.SupplierId is null || x.Id == filters.SupplierId.Value)
            .Select(x =>
            {
                var supplierReceipts = receipts.Where(r => r.SupplierId == x.Id).ToList();
                return new SupplierActivityRow(
                    x.Id, x.Name, x.Phone, supplierReceipts.Count,
                    Money(supplierReceipts.Sum(r => r.NetTotal)), x.CurrentBalance);
            })
            .OrderByDescending(x => x.PurchasesTotal)
            .ThenBy(x => x.Supplier)
            .Take(20).ToList();

        var tracked = await context.Products.AsNoTracking().Where(x => x.TrackStock).ToListAsync(cancellationToken);
        var batches = await context.ProductBatches.AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.StockOnHand > 0m)
            .ToListAsync(cancellationToken);
        var today = DateTime.Today;
        var inventory = new InventoryHealth(
            tracked.Count,
            tracked.Count(x => x.StockOnHand <= x.MinimumStock),
            tracked.Count(x => x.StockOnHand <= 0m),
            batches.Count(x => x.ExpiresAt is not null && x.ExpiresAt.Value.Date < today),
            batches.Count(x => x.ExpiresAt is not null &&
                               x.ExpiresAt.Value.Date >= today &&
                               x.ExpiresAt.Value.Date <= today.AddDays(30)));

        var expiring = batches
            .Where(x => x.ExpiresAt is not null && x.ExpiresAt.Value.Date <= today.AddDays(30))
            .OrderBy(x => x.ExpiresAt)
            .Take(20)
            .Select(x => new ExpiryWatchRow(
                x.Id, x.ProductId,
                Localize(x.Product.NameEn, x.Product.NameFa, x.Product.NamePs, locale),
                x.Product.Sku, x.BatchNumber, x.StockOnHand, x.ExpiresAt))
            .ToList();

        var days = await context.BusinessDays.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .ToListAsync(cancellationToken);
        var dayMap = days.ToDictionary(x => x.Id);
        var dayIds = days.Select(x => x.Id).ToList();
        List<BusinessDayClosureEntity> closures = dayIds.Count == 0
            ? []
            : await context.BusinessDayClosures.AsNoTracking()
                .Where(x => dayIds.Contains(x.BusinessDayId))
                .OrderByDescending(x => x.ClosedAt)
                .Take(30)
                .ToListAsync(cancellationToken);
        var closingHistory = closures.Select(x => new ClosingHistoryRow(
            x.Id,
            dayMap[x.BusinessDayId].BusinessDate,
            x.Number,
            x.Version,
            x.NetSalesTotal,
            x.VarianceTotal,
            canProfit ? x.NetProfitTotal : null,
            x.ClosedAt)).ToList();

        var peakHours = BuildPeakHours(scopedSales, scopedItems, itemFiltered);
        var weekdays = BuildWeekdays(scopedSales, scopedItems, itemFiltered);

        return new ReportSnapshot(
            filters, canProfit, summary, trend, topProducts, slowProducts,
            categoryProfit, customerRows, supplierRows, inventory, expiring,
            closingHistory, peakHours, weekdays);
    }

    public async Task<IReadOnlyList<SalesExportRow>> GetSalesExportAsync(
        ReportFilters filters,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("reports.view");
        filters = Normalize(filters);
        var canProfit = authorizer.HasPermission("reports.profit");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sales = await context.Sales.AsNoTracking()
            .Where(x => x.BusinessDate >= filters.From && x.BusinessDate <= filters.To)
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (filters.CustomerId is not null)
            sales = sales.Where(x => x.CustomerId == filters.CustomerId.Value).ToList();

        if (filters.ProductId is not null || filters.CategoryId is not null)
        {
            var saleIds = sales.Select(x => x.Id).ToList();
            List<SaleItemEntity> items = saleIds.Count == 0 ? [] : await context.SaleItems.AsNoTracking()
                .Where(x => saleIds.Contains(x.SaleId)).ToListAsync(cancellationToken);
            var productIds = items.Select(x => x.ProductId).Distinct().ToList();
            List<ProductEntity> products = productIds.Count == 0 ? [] : await context.Products.AsNoTracking()
                .Where(x => productIds.Contains(x.Id)).ToListAsync(cancellationToken);
            var productMap = products.ToDictionary(x => x.Id);
            var matchingSaleIds = items.Where(x =>
                (filters.ProductId is null || x.ProductId == filters.ProductId.Value) &&
                (filters.CategoryId is null ||
                 productMap.GetValueOrDefault(x.ProductId)?.CategoryId == filters.CategoryId.Value))
                .Select(x => x.SaleId).Distinct().ToHashSet();
            sales = sales.Where(x => matchingSaleIds.Contains(x.Id)).ToList();
        }

        return sales.Select(x => new SalesExportRow(
            x.Number,
            x.SoldAt,
            x.CustomerNameSnapshot,
            x.Subtotal,
            x.LineDiscountTotal,
            x.SaleDiscountAmount,
            x.NetTotal,
            x.ReturnedTotal,
            canProfit ? x.CogsTotal : null,
            canProfit ? x.GrossProfit : null,
            x.PaidAmount,
            x.BalanceDue)).ToList();
    }

    private static IReadOnlyList<ProductPerformanceRow> BuildProductPerformance(
        IReadOnlyList<SaleItemEntity> saleItems,
        IReadOnlyList<SaleReturnItemEntity> returnItems,
        IReadOnlyDictionary<long, SaleItemEntity> itemMap,
        IReadOnlyDictionary<long, ProductEntity> productMap,
        string locale,
        bool canProfit)
    {
        var saleGroups = saleItems.GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => new ProductAccumulator(
                g.Sum(x => x.QuantityBase),
                Money(g.Sum(x => x.LineNetTotal)),
                Money(g.Sum(x => x.CogsAmount))));

        var returnGroups = returnItems
            .Where(x => itemMap.ContainsKey(x.SaleItemId))
            .GroupBy(x => itemMap[x.SaleItemId].ProductId)
            .ToDictionary(g => g.Key, g => new ProductAccumulator(
                g.Sum(x => x.QuantityBase),
                Money(g.Sum(x => x.ReturnAmount)),
                Money(g.Sum(x => x.CogsAmount))));

        return saleGroups.Keys.Concat(returnGroups.Keys).Distinct()
            .Select(id =>
            {
                var sale = saleGroups.GetValueOrDefault(id) ?? new ProductAccumulator(0m, 0m, 0m);
                var ret = returnGroups.GetValueOrDefault(id) ?? new ProductAccumulator(0m, 0m, 0m);
                var product = productMap.GetValueOrDefault(id);
                var netSales = Money(sale.Value - ret.Value);
                var cogs = Money(sale.Cogs - ret.Cogs);
                return new ProductPerformanceRow(
                    id,
                    product?.Sku ?? string.Empty,
                    product is null ? "Product" : Localize(product.NameEn, product.NameFa, product.NamePs, locale),
                    product?.Category is null
                        ? null
                        : Localize(product.Category.NameEn, product.Category.NameFa, product.Category.NamePs, locale),
                    Quantity(sale.Quantity - ret.Quantity),
                    netSales,
                    canProfit ? cogs : null,
                    canProfit ? Money(netSales - cogs) : null);
            }).ToList();
    }

    private static IReadOnlyList<CategoryProfitRow> BuildCategoryProfit(
        IReadOnlyList<SaleItemEntity> saleItems,
        IReadOnlyList<SaleReturnItemEntity> returnItems,
        IReadOnlyDictionary<long, SaleItemEntity> itemMap,
        IReadOnlyDictionary<long, ProductEntity> productMap,
        string locale,
        bool canProfit)
    {
        string Key(long productId) =>
            productMap.GetValueOrDefault(productId)?.CategoryId?.ToString() ?? "uncategorized";

        var saleGroups = saleItems.GroupBy(x => Key(x.ProductId))
            .ToDictionary(g => g.Key, g => new ValueCogs(
                Money(g.Sum(x => x.LineNetTotal)), Money(g.Sum(x => x.CogsAmount))));
        var returnGroups = returnItems
            .Where(x => itemMap.ContainsKey(x.SaleItemId))
            .GroupBy(x => Key(itemMap[x.SaleItemId].ProductId))
            .ToDictionary(g => g.Key, g => new ValueCogs(
                Money(g.Sum(x => x.ReturnAmount)), Money(g.Sum(x => x.CogsAmount))));

        return saleGroups.Keys.Concat(returnGroups.Keys).Distinct().Select(key =>
        {
            var sale = saleGroups.GetValueOrDefault(key) ?? new ValueCogs(0m, 0m);
            var ret = returnGroups.GetValueOrDefault(key) ?? new ValueCogs(0m, 0m);
            var category = key == "uncategorized"
                ? null
                : productMap.Values.Select(x => x.Category)
                    .FirstOrDefault(x => x?.Id.ToString() == key);
            var netSales = Money(sale.Value - ret.Value);
            var cogs = Money(sale.Cogs - ret.Cogs);
            return new CategoryProfitRow(
                category?.Id,
                category is null ? "Uncategorized" :
                    Localize(category.NameEn, category.NameFa, category.NamePs, locale),
                netSales,
                canProfit ? cogs : null,
                canProfit ? Money(netSales - cogs) : null);
        })
        .OrderByDescending(x => x.GrossProfit ?? x.NetSales)
        .Take(20)
        .ToList();
    }

    private static IReadOnlyList<TimePerformanceRow> BuildPeakHours(
        IReadOnlyList<SaleEntity> sales,
        IReadOnlyList<SaleItemEntity> items,
        bool itemFiltered)
    {
        if (itemFiltered)
        {
            var saleMap = sales.ToDictionary(x => x.Id);
            return items.Where(x => saleMap.ContainsKey(x.SaleId))
                .GroupBy(x => saleMap[x.SaleId].SoldAt.ToOffset(AfghanistanOffset).Hour)
                .Select(g => new TimePerformanceRow(
                    g.Key,
                    g.Key.ToString("D2") + ":00",
                    g.Select(x => x.SaleId).Distinct().Count(),
                    Money(g.Sum(x => x.LineNetTotal))))
                .OrderByDescending(x => x.SalesCount)
                .ThenBy(x => x.Bucket)
                .Take(24).ToList();
        }

        return sales.GroupBy(x => x.SoldAt.ToOffset(AfghanistanOffset).Hour)
            .Select(g => new TimePerformanceRow(
                g.Key,
                g.Key.ToString("D2") + ":00",
                g.Count(),
                Money(g.Sum(x => x.NetTotal))))
            .OrderByDescending(x => x.SalesCount)
            .ThenBy(x => x.Bucket)
            .Take(24).ToList();
    }

    private static IReadOnlyList<TimePerformanceRow> BuildWeekdays(
        IReadOnlyList<SaleEntity> sales,
        IReadOnlyList<SaleItemEntity> items,
        bool itemFiltered)
    {
        static int MondayIndex(DateTimeOffset value)
        {
            var d = value.ToOffset(AfghanistanOffset).DayOfWeek;
            return ((int)d + 6) % 7;
        }

        if (itemFiltered)
        {
            var saleMap = sales.ToDictionary(x => x.Id);
            return items.Where(x => saleMap.ContainsKey(x.SaleId))
                .GroupBy(x => MondayIndex(saleMap[x.SaleId].SoldAt))
                .Select(g => new TimePerformanceRow(
                    g.Key,
                    WeekdayLabel(g.Key),
                    g.Select(x => x.SaleId).Distinct().Count(),
                    Money(g.Sum(x => x.LineNetTotal))))
                .OrderBy(x => x.Bucket).ToList();
        }

        return sales.GroupBy(x => MondayIndex(x.SoldAt))
            .Select(g => new TimePerformanceRow(
                g.Key,
                WeekdayLabel(g.Key),
                g.Count(),
                Money(g.Sum(x => x.NetTotal))))
            .OrderBy(x => x.Bucket).ToList();
    }

    private static string WeekdayLabel(int index) => index switch
    {
        0 => "Monday",
        1 => "Tuesday",
        2 => "Wednesday",
        3 => "Thursday",
        4 => "Friday",
        5 => "Saturday",
        6 => "Sunday",
        _ => index.ToString(),
    };

    private static ReportFilters Normalize(ReportFilters filters)
    {
        var from = filters.From.Date;
        var to = filters.To.Date;
        if (to < from)
            throw new InvalidOperationException("Report end date cannot be before the start date.");
        return filters with { From = from, To = to };
    }

    private UserSessionSnapshot RequireUser() =>
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

    private sealed record DayAccumulator(int SalesCount, decimal NetSales, decimal Cogs);
    private sealed record ReturnAccumulator(decimal Returns, decimal Cogs);
    private sealed record ProductAccumulator(decimal Quantity, decimal Value, decimal Cogs);
    private sealed record ValueCogs(decimal Value, decimal Cogs);
}
