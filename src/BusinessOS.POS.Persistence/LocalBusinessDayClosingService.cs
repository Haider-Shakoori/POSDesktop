using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalBusinessDayClosingService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IBusinessDayClosingService
{
    public async Task<BusinessDaySummary> GetSummaryAsync(
        DateTime businessDate,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("business_days.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await SummarizeAsync(context, businessDate.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessDayRow>> GetBusinessDaysAsync(
        int take = 120,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("business_days.view");
        take = Math.Clamp(take, 1, 1000);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var days = await context.BusinessDays.AsNoTracking()
            .OrderByDescending(x => x.BusinessDate)
            .Take(take)
            .ToListAsync(cancellationToken);

        var ids = days.Select(x => x.Id).ToList();
        var closures = await context.BusinessDayClosures.AsNoTracking()
            .Where(x => ids.Contains(x.BusinessDayId))
            .ToListAsync(cancellationToken);
        var versions = closures.GroupBy(x => x.BusinessDayId)
            .ToDictionary(x => x.Key, x => x.Count());

        return days.Select(x => MapDay(x, versions.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<IReadOnlyList<BusinessDayClosureRow>> GetClosuresAsync(
        DateTime businessDate,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("business_days.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var day = await context.BusinessDays.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessDate == businessDate.Date, cancellationToken);
        if (day is null) return [];

        var closures = await context.BusinessDayClosures.AsNoTracking()
            .Where(x => x.BusinessDayId == day.Id)
            .OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
        return closures.Select(MapClosure).ToList();
    }

    public async Task<BusinessDayClosureRow> CloseAsync(
        BusinessDayCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("business_days.close");
        ValidateUuid(request.IdempotencyKey);
        var date = request.BusinessDate.Date;
        var notes = Clean(request.Notes);
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.BusinessDayClosures
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var existingDay = await context.BusinessDays.AsNoTracking()
                .SingleAsync(x => x.Id == existing.BusinessDayId, cancellationToken);
            if (existingDay.BusinessDate != date)
                throw new InvalidOperationException("The daily-close idempotency key belongs to another business day.");
            if (Clean(existing.Notes) != notes)
                throw new InvalidOperationException("The daily-close idempotency key is already bound to another closing payload.");

            await transaction.CommitAsync(cancellationToken);
            return MapClosure(existing);
        }

        var day = await BusinessDayGuard.GetOrCreateAsync(context, date, cancellationToken);
        if (day.Status != "open")
            throw new InvalidOperationException("The business day is already closed.");

        var shifts = await context.CashierShifts
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);

        if (shifts.Any(x => x.Status == "open"))
            throw new InvalidOperationException("All cashier shifts for this business day must be closed first.");

        if (shifts.Any(x => x.ActualCash is null || x.Variance is null))
            throw new InvalidOperationException("Every closed cashier shift must contain an actual cash count and variance.");

        var summary = await SummarizeAsync(context, date, cancellationToken);

        if (summary.ExpectedCashTotal != summary.LedgerExpectedCashTotal)
            throw new InvalidOperationException(
                "Cash reconciliation failed: shift expected cash does not match the cash movement ledger.");

        var derivedVariance = Money(summary.ActualCashTotal - summary.ExpectedCashTotal);
        if (summary.VarianceTotal != derivedVariance)
            throw new InvalidOperationException(
                "Cash reconciliation failed: shift variance does not match actual minus expected cash.");

        var version = (await context.BusinessDayClosures
            .Where(x => x.BusinessDayId == day.Id)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = DateTimeOffset.UtcNow;
        var closure = new BusinessDayClosureEntity
        {
            BusinessDayId = day.Id,
            IdempotencyKey = request.IdempotencyKey,
            Number = "DAY-" + date.ToString("yyyyMMdd") + "-R" + version.ToString("D2"),
            Version = version,
            ClosedByUserId = user.UserId,
            ShiftCount = summary.ShiftCount,
            SalesCount = summary.SalesCount,
            SalesSubtotal = summary.SalesSubtotal,
            SalesLineDiscountTotal = summary.SalesLineDiscountTotal,
            SalesDiscountTotal = summary.SalesDiscountTotal,
            SalesNetTotal = summary.SalesNetTotal,
            SalesReturnTotal = summary.SalesReturnTotal,
            NetSalesTotal = summary.NetSalesTotal,
            SalesCogsTotal = summary.SalesCogsTotal,
            CogsReversedTotal = summary.CogsReversedTotal,
            NetCogsTotal = summary.NetCogsTotal,
            GrossProfitTotal = summary.GrossProfitTotal,
            CustomerCollectionsTotal = summary.CustomerCollectionsTotal,
            PurchasesTotal = summary.PurchasesTotal,
            PurchaseReturnsTotal = summary.PurchaseReturnsTotal,
            SupplierPaymentsTotal = summary.SupplierPaymentsTotal,
            OperatingExpensesTotal = summary.OperatingExpensesTotal,
            OtherIncomeTotal = summary.OtherIncomeTotal,
            NetProfitTotal = summary.NetProfitTotal,
            OpeningCashTotal = summary.OpeningCashTotal,
            CashInflowTotal = summary.CashInflowTotal,
            CashOutflowTotal = summary.CashOutflowTotal,
            ExpectedCashTotal = summary.ExpectedCashTotal,
            ActualCashTotal = summary.ActualCashTotal,
            VarianceTotal = summary.VarianceTotal,
            CashBreakdownJson = JsonSerializer.Serialize(summary.CashBreakdown),
            Notes = notes,
            ClosedAt = now,
            CreatedAt = now,
        };
        context.BusinessDayClosures.Add(closure);

        day.Status = "closed";
        day.ClosedAt = now;
        day.ClosedByUserId = user.UserId;

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "closing.business_day.closed",
            CreatedAt = now,
            DetailsJson = "{\"number\":\"" + closure.Number +
                          "\",\"version\":" + version +
                          ",\"expected_cash\":\"" + summary.ExpectedCashTotal.ToString("0.00") +
                          "\",\"actual_cash\":\"" + summary.ActualCashTotal.ToString("0.00") +
                          "\",\"net_profit\":\"" + summary.NetProfitTotal.ToString("0.00") + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapClosure(closure);
    }

    public async Task<BusinessDayRow> ReopenAsync(
        DateTime businessDate,
        string reason,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("business_days.reopen");
        reason = reason.Trim();
        if (reason.Length == 0)
            throw new InvalidOperationException("A reason is required to reopen a business day.");

        var user = RequireUser();
        var date = businessDate.Date;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var day = await BusinessDayGuard.GetOrCreateAsync(context, date, cancellationToken);
        if (day.Status == "open")
        {
            var count = await context.BusinessDayClosures.CountAsync(
                x => x.BusinessDayId == day.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MapDay(day, count);
        }

        day.Status = "open";
        day.ReopenedAt = DateTimeOffset.UtcNow;
        day.ReopenedByUserId = user.UserId;
        day.ReopenReason = reason;

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "closing.business_day.reopened",
            CreatedAt = day.ReopenedAt.Value,
            DetailsJson = "{\"business_date\":\"" + date.ToString("yyyy-MM-dd") +
                          "\",\"reason\":\"" + Escape(reason) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        var versions = await context.BusinessDayClosures.CountAsync(
            x => x.BusinessDayId == day.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapDay(day, versions);
    }

    private async Task<BusinessDaySummary> SummarizeAsync(
        PosDbContext context,
        DateTime date,
        CancellationToken cancellationToken)
    {
        date = date.Date;
        var day = await BusinessDayGuard.GetOrCreateAsync(context, date, cancellationToken);
        var shifts = await context.CashierShifts.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var shiftIds = shifts.Select(x => x.Id).ToList();

        var sales = await context.Sales.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var returns = await context.SaleReturns.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var collections = await context.CustomerCollections.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var purchaseReturns = await context.PurchaseReturns.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var purchasePayments = await context.PurchasePayments.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var supplierPayments = await context.SupplierPayments.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        var operating = await context.OperatingEntries.AsNoTracking()
            .Where(x => x.BusinessDate == date)
            .ToListAsync(cancellationToken);
        List<CashMovementEntity> cashMovements = shiftIds.Count == 0
            ? []
            : await context.CashMovements.AsNoTracking()
                .Where(x => shiftIds.Contains(x.CashierShiftId))
                .ToListAsync(cancellationToken);

        var terminalIds = shifts.Select(x => x.TerminalId).Distinct().ToList();
        var userIds = shifts.Select(x => x.UserId).Distinct().ToList();
        var terminals = await context.Terminals.AsNoTracking()
            .Where(x => terminalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var users = await context.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var salesSubtotal = Money(sales.Sum(x => x.Subtotal));
        var lineDiscount = Money(sales.Sum(x => x.LineDiscountTotal));
        var saleDiscount = Money(sales.Sum(x => x.SaleDiscountAmount));
        var salesNet = Money(sales.Sum(x => x.NetTotal));
        var salesReturn = Money(returns.Sum(x => x.ReturnTotal));
        var netSales = Money(salesNet - salesReturn);

        var salesCogs = Money(sales.Sum(x => x.CogsTotal));
        var cogsReversed = Money(returns.Sum(x => x.CogsReversed));
        var netCogs = Money(salesCogs - cogsReversed);
        var grossProfit = Money(netSales - netCogs);

        var expenses = Money(operating.Where(x => x.EntryType == "expense").Sum(x => x.Amount));
        var otherIncome = Money(operating.Where(x => x.EntryType == "income").Sum(x => x.Amount));
        var netProfit = Money(grossProfit + otherIncome - expenses);

        var breakdown = cashMovements
            .GroupBy(x => new { x.MovementType, x.Direction })
            .Select(x => new CashBreakdownRow(
                x.Key.MovementType, x.Key.Direction, Money(x.Sum(v => v.Amount))))
            .OrderBy(x => x.Direction)
            .ThenBy(x => x.MovementType)
            .ToList();

        var openingCash = Money(breakdown
            .Where(x => x.MovementType == "opening_float")
            .Sum(x => x.Amount));
        var cashInflow = Money(breakdown
            .Where(x => x.MovementType != "opening_float" && x.Direction == "inflow")
            .Sum(x => x.Amount));
        var cashOutflow = Money(breakdown
            .Where(x => x.Direction == "outflow")
            .Sum(x => x.Amount));

        var expectedCash = Money(shifts.Sum(x => x.ExpectedCash));
        var actualCash = Money(shifts.Where(x => x.ActualCash is not null).Sum(x => x.ActualCash ?? 0m));
        var variance = Money(shifts.Where(x => x.Variance is not null).Sum(x => x.Variance ?? 0m));
        var ledgerExpected = Money(openingCash + cashInflow - cashOutflow);

        var shiftRows = shifts.OrderBy(x => x.Id).Select(x => new BusinessDayShiftRow(
            x.Id,
            terminals.GetValueOrDefault(x.TerminalId)?.Name ?? "Terminal",
            users.GetValueOrDefault(x.UserId)?.Name ?? "User",
            x.Status,
            x.OpeningCash,
            x.ExpectedCash,
            x.ActualCash,
            x.Variance,
            x.VarianceWithinTolerance,
            x.OpenedAt,
            x.ClosedAt)).ToList();

        return new BusinessDaySummary(
            date,
            day.Status,
            shifts.Count,
            shifts.Count(x => x.Status == "open"),
            sales.Count,
            salesSubtotal,
            lineDiscount,
            saleDiscount,
            salesNet,
            salesReturn,
            netSales,
            salesCogs,
            cogsReversed,
            netCogs,
            grossProfit,
            Money(collections.Sum(x => x.Amount)),
            Money(receipts.Sum(x => x.NetTotal)),
            Money(purchaseReturns.Sum(x => x.ReturnTotal)),
            Money(purchasePayments.Sum(x => x.Amount) + supplierPayments.Sum(x => x.Amount)),
            expenses,
            otherIncome,
            netProfit,
            openingCash,
            cashInflow,
            cashOutflow,
            expectedCash,
            actualCash,
            variance,
            ledgerExpected,
            breakdown,
            shiftRows);
    }

    private static BusinessDayRow MapDay(BusinessDayEntity x, int versions) =>
        new(x.Id, x.BusinessDate, x.Status, x.ClosedAt, x.ClosedByUserId,
            x.ReopenedAt, x.ReopenedByUserId, x.ReopenReason, versions);

    private static BusinessDayClosureRow MapClosure(BusinessDayClosureEntity x)
    {
        IReadOnlyList<CashBreakdownRow> breakdown = [];
        if (!string.IsNullOrWhiteSpace(x.CashBreakdownJson))
        {
            breakdown = JsonSerializer.Deserialize<List<CashBreakdownRow>>(x.CashBreakdownJson!) ?? [];
        }

        return new BusinessDayClosureRow(
            x.Id, x.BusinessDayId, x.Number, x.Version, x.ClosedByUserId,
            x.ShiftCount, x.SalesCount, x.SalesSubtotal, x.SalesLineDiscountTotal,
            x.SalesDiscountTotal, x.SalesNetTotal, x.SalesReturnTotal, x.NetSalesTotal,
            x.SalesCogsTotal, x.CogsReversedTotal, x.NetCogsTotal, x.GrossProfitTotal,
            x.CustomerCollectionsTotal, x.PurchasesTotal, x.PurchaseReturnsTotal,
            x.SupplierPaymentsTotal, x.OperatingExpensesTotal, x.OtherIncomeTotal,
            x.NetProfitTotal, x.OpeningCashTotal, x.CashInflowTotal, x.CashOutflowTotal,
            x.ExpectedCashTotal, x.ActualCashTotal, x.VarianceTotal, breakdown,
            x.Notes, x.ClosedAt);
    }

    private UserSessionSnapshot RequireUser() =>
        sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void ValidateUuid(string value)
    {
        if (!Guid.TryParse(value, out _))
            throw new InvalidOperationException("A valid idempotency key is required.");
    }
}
