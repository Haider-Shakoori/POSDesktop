using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalDashboardService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IDashboardService
{
    public async Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = sessions.Current ?? throw new InvalidOperationException("No user is signed in.");
        var date = DateTime.Today;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        DashboardShift? openShift = null;
        var shift = await context.CashierShifts.AsNoTracking()
            .Where(x => x.UserId == current.UserId && x.Status == "open")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (shift is not null)
        {
            var terminal = await context.Terminals.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == shift.TerminalId, cancellationToken);
            openShift = new DashboardShift(
                shift.Id,
                terminal?.Name ?? "Terminal",
                shift.OpeningCash,
                shift.ExpectedCash,
                shift.OpenedAt);
        }

        decimal? netSales = null;
        int? transactions = null;
        IReadOnlyList<DashboardRecentSaleRow> recentSales = [];
        if (authorizer.HasPermission("sales.view"))
        {
            var salesToday = await context.Sales.AsNoTracking()
                .Where(x => x.BusinessDate == date)
                .ToListAsync(cancellationToken);
            var returnsToday = await context.SaleReturns.AsNoTracking()
                .Where(x => x.BusinessDate == date)
                .ToListAsync(cancellationToken);
            netSales = Money(salesToday.Sum(x => x.NetTotal) - returnsToday.Sum(x => x.ReturnTotal));
            transactions = salesToday.Count;

            recentSales = (await context.Sales.AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Take(6)
                .ToListAsync(cancellationToken))
                .Select(x => new DashboardRecentSaleRow(
                    x.Id, x.Number, x.CustomerNameSnapshot,
                    x.NetTotal, x.ReturnedTotal, x.PaymentStatus, x.SoldAt))
                .ToList();
        }

        decimal? receivables = null;
        if (authorizer.HasPermission("customers.view"))
        {
            receivables = Money(await context.Customers.AsNoTracking()
                .Where(x => x.CurrentBalance > 0m)
                .SumAsync(x => x.CurrentBalance, cancellationToken));
        }

        decimal? payables = null;
        if (authorizer.HasPermission("suppliers.view"))
        {
            payables = Money(await context.Suppliers.AsNoTracking()
                .Where(x => x.CurrentBalance > 0m)
                .SumAsync(x => x.CurrentBalance, cancellationToken));
        }

        int? lowStockCount = null;
        IReadOnlyList<DashboardLowStockRow> lowStockProducts = [];
        if (authorizer.HasPermission("inventory.view"))
        {
            var rows = await context.Products.AsNoTracking()
                .Include(x => x.BaseUnit)
                .Where(x => x.IsActive && x.TrackStock && x.StockOnHand <= x.MinimumStock)
                .OrderBy(x => x.StockOnHand)
                .ThenBy(x => x.Id)
                .Take(6)
                .ToListAsync(cancellationToken);

            lowStockCount = await context.Products.AsNoTracking()
                .CountAsync(x => x.IsActive && x.TrackStock && x.StockOnHand <= x.MinimumStock, cancellationToken);

            lowStockProducts = rows.Select(x => new DashboardLowStockRow(
                x.Id,
                x.Sku,
                Localize(x.NameEn, x.NameFa, x.NamePs, current.PreferredLocale),
                x.StockOnHand,
                x.MinimumStock,
                x.BaseUnit.Symbol ?? x.BaseUnit.Code)).ToList();
        }

        return new DashboardSnapshot(
            date,
            netSales,
            transactions,
            receivables,
            payables,
            lowStockCount,
            openShift,
            lowStockProducts,
            recentSales);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa!,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps!,
            _ => en,
        };
}
