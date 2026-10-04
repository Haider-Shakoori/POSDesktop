using System.Globalization;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalCashManagementService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer,
    IWorkstationContext workstation)
    : ICashManagementService
{
    public async Task<IReadOnlyList<CashTerminalOption>> GetTerminalsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("cash.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Terminals.AsNoTracking().AsQueryable();
        if (workstation.CashTerminalId is not null && !authorizer.HasPermission("cash.manage"))
            query = query.Where(x => x.Id == workstation.CashTerminalId.Value);

        return await query.OrderBy(x => x.Id)
            .Select(x => new CashTerminalOption(x.Id, x.Code, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CashShiftDetail?> GetCurrentShiftAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("cash.view");
        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.CashierShifts.AsNoTracking()
            .Where(x => x.UserId == user.UserId && x.Status == "open");
        if (workstation.CashTerminalId is not null)
            query = query.Where(x => x.TerminalId == workstation.CashTerminalId.Value);

        var shift = await query.OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return shift is null ? null : await LoadShiftDetailAsync(context, shift.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CashShiftSummary>> GetShiftsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("cash.view");
        take = Math.Clamp(take, 1, 500);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var shifts = await context.CashierShifts.AsNoTracking()
            .OrderByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
        return await MapShiftSummariesAsync(context, shifts, cancellationToken);
    }

    public async Task<CashShiftDetail?> GetShiftAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("cash.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadShiftDetailAsync(context, shiftId, cancellationToken);
    }

    public async Task<CashShiftDetail> OpenShiftAsync(ShiftOpenRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("shifts.open");
        ValidateUuid(request.IdempotencyKey);
        var opening = Money(request.OpeningCash);
        if (opening < 0m) throw new InvalidOperationException("Opening cash cannot be negative.");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var shiftBusinessDate = BusinessDayGuard.LocalBusinessDate(DateTimeOffset.UtcNow);
        await BusinessDayGuard.EnsureOpenAsync(context, shiftBusinessDate, cancellationToken);

        var existing = await context.CashierShifts.SingleOrDefaultAsync(
            x => x.OpenIdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != user.UserId ||
                existing.TerminalId != request.TerminalId ||
                existing.OpeningCash != opening)
                throw new InvalidOperationException("The shift opening idempotency key is already bound to another opening payload.");
            await transaction.CommitAsync(cancellationToken);
            return (await LoadShiftDetailAsync(context, existing.Id, cancellationToken))!;
        }

        if (workstation.CashTerminalId is not null &&
            request.TerminalId != workstation.CashTerminalId.Value &&
            !authorizer.HasPermission("cash.manage"))
            throw new InvalidOperationException("This workstation can open only its assigned cashier terminal.");

        var terminal = await context.Terminals.SingleOrDefaultAsync(
            x => x.Id == request.TerminalId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected terminal is unavailable.");

        if (await context.CashierShifts.AnyAsync(
            x => x.UserId == user.UserId && x.Status == "open", cancellationToken))
            throw new InvalidOperationException("This user already has an open cashier shift.");

        if (await context.CashierShifts.AnyAsync(
            x => x.TerminalId == terminal.Id && x.Status == "open", cancellationToken))
            throw new InvalidOperationException("The selected terminal already has an open cashier shift.");

        var now = DateTimeOffset.UtcNow;
        var shift = new CashierShiftEntity
        {
            TerminalId = terminal.Id,
            UserId = user.UserId,
            BusinessDate = shiftBusinessDate,
            OpenIdempotencyKey = request.IdempotencyKey,
            Status = "open",
            OpeningCash = opening,
            ExpectedCash = opening,
            OpenedAt = now,
        };
        context.CashierShifts.Add(shift);
        await context.SaveChangesAsync(cancellationToken);
        await CashLedgerEngine.EnsureOpeningMovementAsync(context, shift, user.UserId, cancellationToken);

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "cash.shift.opened",
            CreatedAt = now,
            DetailsJson = "{\"shift_id\":" + shift.Id +
                          ",\"terminal\":\"" + terminal.Code +
                          "\",\"opening_cash\":\"" + opening.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await LoadShiftDetailAsync(context, shift.Id, cancellationToken))!;
    }

    public async Task<CashMovementRow> RecordManualMovementAsync(
        ManualCashMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("cash.manage");
        ValidateUuid(request.IdempotencyKey);
        var reason = request.Reason.Trim();
        if (reason.Length == 0) throw new InvalidOperationException("A reason is required for manual cash movements.");

        var direction = request.MovementType switch
        {
            "cash_deposit" => "inflow",
            "cash_withdrawal" => "outflow",
            "drawer_to_safe" => "outflow",
            _ => throw new InvalidOperationException("Unsupported manual cash movement type."),
        };

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var movement = await CashLedgerEngine.RecordAsync(
            context, user.UserId, request.Amount, direction, request.MovementType,
            null, null, null, reason, DateTimeOffset.UtcNow,
            request.IdempotencyKey, request.ShiftId, cancellationToken, allowOtherUser: true);

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "cash.manual_movement.recorded",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"movement_type\":\"" + movement.MovementType +
                          "\",\"amount\":\"" + movement.Amount.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapMovement(movement);
    }

    public async Task<ShiftClosureResult> CloseShiftAsync(ShiftCloseRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("shifts.close");
        ValidateUuid(request.IdempotencyKey);
        var actual = Money(request.ActualCash);
        if (actual < 0m) throw new InvalidOperationException("Actual cash cannot be negative.");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.CashierShiftClosures.SingleOrDefaultAsync(
            x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CashierShiftId != request.ShiftId ||
                existing.ActualCash != actual ||
                Clean(existing.VarianceReason) != Clean(request.VarianceReason) ||
                Clean(existing.ClosingNotes) != Clean(request.ClosingNotes))
                throw new InvalidOperationException("The shift-close idempotency key is already bound to another closing payload.");
            await transaction.CommitAsync(cancellationToken);
            return MapClosure(existing);
        }

        var shift = await context.CashierShifts.SingleOrDefaultAsync(
            x => x.Id == request.ShiftId, cancellationToken)
            ?? throw new InvalidOperationException("Cashier shift was not found.");
        await BusinessDayGuard.EnsureOpenAsync(context, shift.BusinessDate, cancellationToken);

        if (shift.UserId != user.UserId && !authorizer.HasPermission("cash.manage"))
            throw new InvalidOperationException("The user is not allowed to close another cashier's shift.");
        if (shift.Status != "open")
            throw new InvalidOperationException("Only an open cashier shift can be closed.");

        var expected = await CashLedgerEngine.RecalculateExpectedCashAsync(context, shift, cancellationToken);
        var variance = Money(actual - expected);
        const decimal tolerance = 0m;
        var within = Math.Abs(variance) <= tolerance;
        var reason = Clean(request.VarianceReason);
        if (!within && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A variance reason is required when the cash difference exceeds the configured tolerance.");

        var version = (await context.CashierShiftClosures
            .Where(x => x.CashierShiftId == shift.Id)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var closure = new CashierShiftClosureEntity
        {
            IdempotencyKey = request.IdempotencyKey,
            CashierShiftId = shift.Id,
            Version = version,
            ClosedByUserId = user.UserId,
            ExpectedCash = expected,
            ActualCash = actual,
            Variance = variance,
            Tolerance = tolerance,
            WithinTolerance = within,
            VarianceReason = reason,
            ClosingNotes = Clean(request.ClosingNotes),
            ClosedAt = now,
            CreatedAt = now,
        };
        context.CashierShiftClosures.Add(closure);

        shift.Status = "closed";
        shift.ClosedAt = now;
        shift.ClosedByUserId = user.UserId;
        shift.ExpectedCash = expected;
        shift.ActualCash = actual;
        shift.Variance = variance;
        shift.VarianceWithinTolerance = within;
        shift.VarianceReason = reason;
        shift.ClosingNotes = closure.ClosingNotes;

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "closing.shift.closed",
            CreatedAt = now,
            DetailsJson = "{\"shift_id\":" + shift.Id +
                          ",\"expected\":\"" + expected.ToString("0.00", CultureInfo.InvariantCulture) +
                          "\",\"actual\":\"" + actual.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapClosure(closure);
    }

    public async Task<CashShiftDetail> ReopenShiftAsync(long shiftId, string reason, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("shifts.reopen");
        reason = reason.Trim();
        if (reason.Length == 0) throw new InvalidOperationException("A reason is required to reopen a cashier shift.");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var shift = await context.CashierShifts.SingleOrDefaultAsync(x => x.Id == shiftId, cancellationToken)
            ?? throw new InvalidOperationException("Cashier shift was not found.");
        await BusinessDayGuard.EnsureOpenAsync(context, shift.BusinessDate, cancellationToken);

        if (shift.Status == "open")
        {
            await transaction.CommitAsync(cancellationToken);
            return (await LoadShiftDetailAsync(context, shift.Id, cancellationToken))!;
        }

        if (await context.CashierShifts.AnyAsync(
            x => x.Id != shift.Id && x.UserId == shift.UserId && x.Status == "open", cancellationToken) ||
            await context.CashierShifts.AnyAsync(
            x => x.Id != shift.Id && x.TerminalId == shift.TerminalId && x.Status == "open", cancellationToken))
            throw new InvalidOperationException("The shift cannot be reopened because the cashier or terminal already has another open shift.");

        shift.Status = "open";
        shift.ClosedAt = null;
        shift.ClosedByUserId = null;
        shift.ActualCash = null;
        shift.Variance = null;
        shift.VarianceWithinTolerance = null;
        shift.VarianceReason = null;
        shift.ClosingNotes = null;
        shift.ReopenedAt = DateTimeOffset.UtcNow;
        shift.ReopenedByUserId = user.UserId;

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "closing.shift.reopened",
            CreatedAt = shift.ReopenedAt.Value,
            DetailsJson = "{\"shift_id\":" + shift.Id + ",\"reason\":\"" + Escape(reason) + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await LoadShiftDetailAsync(context, shift.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (!authorizer.HasPermission("expenses.view") && !authorizer.HasPermission("expenses.create"))
            throw new InvalidOperationException("The user is not allowed to view expense categories.");
        var locale = RequireUser().PreferredLocale;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await context.ExpenseCategories.AsNoTracking()
            .Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken))
            .Select(x => new ExpenseCategoryOption(
                x.Id, x.Code, x.EntryType, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.IsActive, x.SortOrder))
            .ToList();
    }

    public async Task<IReadOnlyList<CashPaymentMethodOption>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default)
    {
        if (!authorizer.HasPermission("expenses.view") && !authorizer.HasPermission("expenses.create") &&
            !authorizer.HasPermission("cash.view"))
            throw new InvalidOperationException("The user is not allowed to view payment methods.");
        var locale = RequireUser().PreferredLocale;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await context.PaymentMethods.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ToListAsync(cancellationToken))
            .Select(x => new CashPaymentMethodOption(
                x.Id, x.Code, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.IsCash))
            .ToList();
    }

    public async Task<IReadOnlyList<OperatingEntryRow>> GetOperatingEntriesAsync(
        string? entryType = null, int take = 300, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("expenses.view");
        take = Math.Clamp(take, 1, 1000);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.OperatingEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entryType)) query = query.Where(x => x.EntryType == entryType);
        var rows = await query.OrderByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
        return await MapOperatingEntriesAsync(context, rows, cancellationToken);
    }

    public async Task<OperatingEntryRow> RecordOperatingEntryAsync(
        OperatingEntryRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("expenses.create");
        ValidateUuid(request.IdempotencyKey);
        var amount = Money(request.Amount);
        if (amount <= 0m) throw new InvalidOperationException("Operating entry amount must be greater than zero.");
        if (request.EntryType is not ("expense" or "income"))
            throw new InvalidOperationException("Operating entry type is invalid.");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.OperatingEntries.SingleOrDefaultAsync(
            x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.EntryType != request.EntryType ||
                existing.ExpenseCategoryId != request.ExpenseCategoryId ||
                existing.PaymentMethodId != request.PaymentMethodId ||
                existing.Amount != amount)
                throw new InvalidOperationException("The operating entry idempotency key is already bound to another payload.");
            await transaction.CommitAsync(cancellationToken);
            return (await MapOperatingEntriesAsync(context, [existing], cancellationToken))[0];
        }

        var category = await context.ExpenseCategories.SingleOrDefaultAsync(
            x => x.Id == request.ExpenseCategoryId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected operating category is unavailable.");
        if (category.EntryType != request.EntryType)
            throw new InvalidOperationException("The category does not match the operating entry type.");

        var method = await context.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == request.PaymentMethodId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected payment method is unavailable.");

        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        await BusinessDayGuard.EnsureOpenAsync(context, occurredAt, cancellationToken);
        var entry = new OperatingEntryEntity
        {
            Number = await NextNumberAsync(context, "operating_entry", request.EntryType == "expense" ? "EXP" : "INC", occurredAt, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            ExpenseCategoryId = category.Id,
            PaymentMethodId = method.Id,
            RecordedByUserId = user.UserId,
            BusinessDate = BusinessDayGuard.LocalBusinessDate(occurredAt),
            EntryType = request.EntryType,
            Amount = amount,
            Reference = Clean(request.Reference),
            Description = Clean(request.Description),
            OccurredAt = occurredAt,
        };
        context.OperatingEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);

        if (method.IsCash)
        {
            await CashLedgerEngine.RecordAsync(
                context, user.UserId, amount,
                request.EntryType == "expense" ? "outflow" : "inflow",
                request.EntryType == "expense" ? "expense" : "other_income",
                "operating_entry", entry.Id, entry.Number, entry.Description,
                occurredAt, "operating-entry:" + entry.Id, null, cancellationToken);
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "cash.operating_entry.recorded",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"number\":\"" + entry.Number + "\",\"entry_type\":\"" + entry.EntryType + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await MapOperatingEntriesAsync(context, [entry], cancellationToken))[0];
    }

    private async Task<CashShiftDetail?> LoadShiftDetailAsync(PosDbContext context, long shiftId, CancellationToken cancellationToken)
    {
        var shift = await context.CashierShifts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == shiftId, cancellationToken);
        if (shift is null) return null;
        var summary = (await MapShiftSummariesAsync(context, [shift], cancellationToken))[0];
        var movements = await context.CashMovements.AsNoTracking()
            .Where(x => x.CashierShiftId == shiftId).OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var closures = await context.CashierShiftClosures.AsNoTracking()
            .Where(x => x.CashierShiftId == shiftId).OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
        return new CashShiftDetail(
            summary,
            movements.Select(MapMovement).ToList(),
            closures.Select(x => new ShiftClosureRow(
                x.Id, x.Version, x.ExpectedCash, x.ActualCash, x.Variance,
                x.Tolerance, x.WithinTolerance, x.VarianceReason, x.ClosingNotes, x.ClosedAt)).ToList());
    }

    private static async Task<IReadOnlyList<CashShiftSummary>> MapShiftSummariesAsync(
        PosDbContext context, IReadOnlyList<CashierShiftEntity> shifts, CancellationToken cancellationToken)
    {
        var terminalIds = shifts.Select(x => x.TerminalId).Distinct().ToList();
        var userIds = shifts.Select(x => x.UserId).Distinct().ToList();
        var terminals = await context.Terminals.AsNoTracking()
            .Where(x => terminalIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var users = await context.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        return shifts.Select(x => new CashShiftSummary(
            x.Id, x.TerminalId, terminals.GetValueOrDefault(x.TerminalId)?.Name ?? "Terminal",
            x.UserId, users.GetValueOrDefault(x.UserId)?.Name ?? "User",
            x.BusinessDate, x.Status, x.OpeningCash, x.ExpectedCash,
            x.ActualCash, x.Variance, x.VarianceWithinTolerance, x.OpenedAt, x.ClosedAt)).ToList();
    }

    private async Task<IReadOnlyList<OperatingEntryRow>> MapOperatingEntriesAsync(
        PosDbContext context, IReadOnlyList<OperatingEntryEntity> entries, CancellationToken cancellationToken)
    {
        var locale = RequireUser().PreferredLocale;
        var categoryIds = entries.Select(x => x.ExpenseCategoryId).Distinct().ToList();
        var methodIds = entries.Select(x => x.PaymentMethodId).Distinct().ToList();
        var categories = await context.ExpenseCategories.AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var methods = await context.PaymentMethods.AsNoTracking()
            .Where(x => methodIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        return entries.Select(x =>
        {
            var category = categories[x.ExpenseCategoryId];
            var method = methods[x.PaymentMethodId];
            return new OperatingEntryRow(
                x.Id, x.Number, x.EntryType,
                Localize(category.NameEn, category.NameFa, category.NamePs, locale),
                Localize(method.NameEn, method.NameFa, method.NamePs, locale),
                method.IsCash, x.Amount, x.Reference, x.Description, x.OccurredAt);
        }).ToList();
    }

    private static CashMovementRow MapMovement(CashMovementEntity x) =>
        new(x.Id, x.CashierShiftId, x.MovementType, x.Direction, x.Amount,
            x.ExpectedCashAfter, x.ReferenceNumber, x.Reason, x.OccurredAt);

    private static ShiftClosureResult MapClosure(CashierShiftClosureEntity x) =>
        new(x.Id, x.CashierShiftId, x.Version, x.ExpectedCash, x.ActualCash,
            x.Variance, x.Tolerance, x.WithinTolerance, x.ClosedAt);

    private UserSessionSnapshot RequireUser() =>
        sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

    private static async Task<string> NextNumberAsync(
        PosDbContext context, string key, string prefix, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var sequence = await context.DocumentSequences.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (sequence is null)
        {
            sequence = new DocumentSequenceEntity { Key = key, NextValue = 1 };
            context.DocumentSequences.Add(sequence);
        }
        var value = sequence.NextValue++;
        await context.SaveChangesAsync(cancellationToken);
        return prefix + "-" + at.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
               value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch { "fa" when !string.IsNullOrWhiteSpace(fa) => fa!, "ps" when !string.IsNullOrWhiteSpace(ps) => ps!, _ => en };

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static void ValidateUuid(string value)
    {
        if (!Guid.TryParse(value, out _)) throw new InvalidOperationException("A valid idempotency key is required.");
    }
}
