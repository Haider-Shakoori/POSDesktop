using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalSalesService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : ISalesService
{
    public async Task<IReadOnlyList<SaleHistoryRow>> GetSalesAsync(
        string? search = null,
        int take = 300,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.view");
        search = search?.Trim();
        take = Math.Clamp(take, 1, 1000);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Sales.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Number, like) ||
                EF.Functions.Like(x.CustomerNameSnapshot, like));
        }

        var sales = await query
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var userIds = sales.Select(x => x.CashierUserId).Distinct().ToList();
        var users = await context.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return sales.Select(x => new SaleHistoryRow(
            x.Id,
            x.Number,
            x.CustomerNameSnapshot,
            users.GetValueOrDefault(x.CashierUserId)?.Name ?? "Unknown",
            x.NetTotal,
            x.PaidAmount,
            x.ChangeAmount,
            x.PaymentStatus,
            x.SoldAt)).ToList();
    }

    public async Task<SaleDetail?> GetSaleAsync(
        long saleId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.view");
        return await LoadDetailAsync(saleId, cancellationToken);
    }

    public async Task<SaleReceiptData?> GetReceiptAsync(
        long saleId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.view");
        var sale = await LoadDetailAsync(saleId, cancellationToken);
        return sale is null
            ? null
            : new SaleReceiptData(
                "BusinessOS POS",
                "WeSoft Technologies · Afghanistan",
                "AFN",
                sale);
    }

    private async Task<SaleDetail?> LoadDetailAsync(long saleId, CancellationToken cancellationToken)
    {
        var locale = sessions.Current?.PreferredLocale
            ?? throw new InvalidOperationException("No user is signed in.");
        var canViewProfit = authorizer.HasPermission("reports.profit");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sale = await context.Sales.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken);

        if (sale is null) return null;

        var cashier = await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sale.CashierUserId, cancellationToken);
        var paymentMethods = await context.PaymentMethods.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var saleItemIds = sale.Items.Select(x => x.Id).ToList();
        var returned = await context.SaleReturnItems.AsNoTracking()
            .Where(x => saleItemIds.Contains(x.SaleItemId))
            .GroupBy(x => x.SaleItemId)
            .Select(x => new { SaleItemId = x.Key, Quantity = x.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.SaleItemId, x => x.Quantity, cancellationToken);

        var items = sale.Items.OrderBy(x => x.Id).Select(x =>
        {
            var returnedQuantity = returned.GetValueOrDefault(x.Id, 0m);
            return new SaleDetailLine(
                x.Id,
                x.SkuSnapshot,
                x.ProductNameSnapshot,
                x.UnitNameSnapshot,
                x.Quantity,
                x.UnitPrice,
                x.LineSubtotal,
                x.LineDiscountAmount,
                x.AllocatedSaleDiscount,
                x.LineNetTotal,
                canViewProfit ? x.CogsAmount : 0m,
                canViewProfit ? x.GrossProfit : 0m,
                returnedQuantity,
                Math.Max(0m, x.Quantity - returnedQuantity));
        }).ToList();

        var payments = sale.Payments.OrderBy(x => x.Id).Select(x =>
        {
            paymentMethods.TryGetValue(x.MethodCode, out var method);
            var name = method is null
                ? x.MethodCode
                : Localize(method.NameEn, method.NameFa, method.NamePs, locale);

            return new SaleDetailPayment(
                x.MethodCode,
                name,
                x.Amount,
                x.TenderedAmount,
                x.ChangeAmount,
                x.Reference,
                x.Notes,
                x.PaidAt);
        }).ToList();

        return new SaleDetail(
            sale.Id,
            sale.Number,
            sale.CustomerNameSnapshot,
            cashier?.Name ?? "Unknown",
            sale.Status,
            sale.PaymentStatus,
            sale.CustomerId,
            sale.Subtotal,
            sale.LineDiscountTotal,
            sale.SaleDiscountAmount,
            sale.NetTotal,
            canViewProfit ? sale.CogsTotal : 0m,
            canViewProfit ? sale.GrossProfit : 0m,
            sale.PaidAmount,
            sale.ChangeAmount,
            sale.BalanceDue,
            sale.ReturnedTotal,
            sale.RefundedTotal,
            sale.SoldAt,
            sale.Notes,
            items,
            payments);
    }

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps,
            _ => en,
        };
}
