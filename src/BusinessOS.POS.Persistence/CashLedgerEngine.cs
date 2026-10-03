using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class CashLedgerEngine
{
    public static async Task<CashMovementEntity> RecordAsync(
        PosDbContext context,
        long actorUserId,
        decimal amount,
        string direction,
        string movementType,
        string? sourceType,
        long? sourceId,
        string? referenceNumber,
        string? reason,
        DateTimeOffset occurredAt,
        string idempotencyKey,
        long? shiftId = null,
        CancellationToken cancellationToken = default,
        bool allowOtherUser = false)
    {
        amount = Money(amount);
        if (amount <= 0m) throw new InvalidOperationException("Cash movement amount must be greater than zero.");
        if (direction is not ("inflow" or "outflow"))
            throw new InvalidOperationException("Cash movement direction is invalid.");

        var existing = await context.CashMovements.SingleOrDefaultAsync(
            x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Amount != amount ||
                existing.Direction != direction ||
                existing.MovementType != movementType ||
                existing.SourceType != sourceType ||
                existing.SourceId != sourceId ||
                (shiftId is not null && existing.CashierShiftId != shiftId.Value) ||
                Clean(existing.Reason) != Clean(reason))
                throw new InvalidOperationException("The cash movement idempotency key is already bound to another payload.");
            return existing;
        }

        CashierShiftEntity? shift = shiftId is null
            ? await context.CashierShifts
                .Where(x => x.UserId == actorUserId && x.Status == "open")
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : await context.CashierShifts.SingleOrDefaultAsync(
                x => x.Id == shiftId.Value, cancellationToken);

        if (shift is null)
            throw new InvalidOperationException("Open a cashier shift before recording a cash transaction.");
        if (shift.Status != "open")
            throw new InvalidOperationException("Cash movement requires an open cashier shift.");
        if (shift.UserId != actorUserId && !allowOtherUser)
            throw new InvalidOperationException("The selected cash drawer belongs to another user.");
        if (occurredAt < shift.OpenedAt)
            throw new InvalidOperationException("Cash transaction time cannot be before the cashier shift opened.");

        await EnsureOpeningMovementAsync(context, shift, actorUserId, cancellationToken);

        shift.ExpectedCash = Money(shift.ExpectedCash + (direction == "inflow" ? amount : -amount));
        var movement = new CashMovementEntity
        {
            IdempotencyKey = idempotencyKey,
            CashierShiftId = shift.Id,
            TerminalId = shift.TerminalId,
            ActorUserId = actorUserId,
            MovementType = movementType,
            Direction = direction,
            Amount = amount,
            ExpectedCashAfter = shift.ExpectedCash,
            SourceType = sourceType,
            SourceId = sourceId,
            ReferenceNumber = Clean(referenceNumber),
            Reason = Clean(reason),
            OccurredAt = occurredAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.CashMovements.Add(movement);
        await context.SaveChangesAsync(cancellationToken);
        return movement;
    }

    public static async Task<CashMovementEntity> EnsureOpeningMovementAsync(
        PosDbContext context,
        CashierShiftEntity shift,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var key = "shift:" + shift.Id + ":opening";
        var existing = await context.CashMovements.SingleOrDefaultAsync(
            x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return existing;

        shift.ExpectedCash = Money(shift.OpeningCash);
        var movement = new CashMovementEntity
        {
            IdempotencyKey = key,
            CashierShiftId = shift.Id,
            TerminalId = shift.TerminalId,
            ActorUserId = actorUserId,
            MovementType = "opening_float",
            Direction = "inflow",
            Amount = Money(shift.OpeningCash),
            ExpectedCashAfter = Money(shift.OpeningCash),
            SourceType = "cashier_shift",
            SourceId = shift.Id,
            Reason = "Shift opening float.",
            OccurredAt = shift.OpenedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.CashMovements.Add(movement);
        await context.SaveChangesAsync(cancellationToken);
        return movement;
    }

    public static async Task<decimal> RecalculateExpectedCashAsync(
        PosDbContext context,
        CashierShiftEntity shift,
        CancellationToken cancellationToken = default)
    {
        await EnsureOpeningMovementAsync(context, shift, shift.UserId, cancellationToken);
        var movements = await context.CashMovements.AsNoTracking()
            .Where(x => x.CashierShiftId == shift.Id)
            .ToListAsync(cancellationToken);
        var inflow = movements.Where(x => x.Direction == "inflow").Sum(x => x.Amount);
        var outflow = movements.Where(x => x.Direction == "outflow").Sum(x => x.Amount);
        shift.ExpectedCash = Money(inflow - outflow);
        await context.SaveChangesAsync(cancellationToken);
        return shift.ExpectedCash;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
