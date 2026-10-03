using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class SupplierLedgerWriter
{
    public static Task<SupplierLedgerEntryEntity> CreditAsync(
        PosDbContext context,
        SupplierEntity supplier,
        decimal amount,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, supplier, 0m, Money(amount), entryType, referenceType,
            referenceId, referenceNumber, actorUserId, notes, occurredAt, cancellationToken);

    public static Task<SupplierLedgerEntryEntity> DebitAsync(
        PosDbContext context,
        SupplierEntity supplier,
        decimal amount,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, supplier, Money(amount), 0m, entryType, referenceType,
            referenceId, referenceNumber, actorUserId, notes, occurredAt, cancellationToken);

    private static async Task<SupplierLedgerEntryEntity> RecordAsync(
        PosDbContext context,
        SupplierEntity supplier,
        decimal debit,
        decimal credit,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        DateTimeOffset? occurredAt,
        CancellationToken cancellationToken)
    {
        var existing = await context.SupplierLedgerEntries
            .SingleOrDefaultAsync(x =>
                x.SupplierId == supplier.Id &&
                x.EntryType == entryType &&
                x.ReferenceType == referenceType &&
                x.ReferenceId == referenceId,
                cancellationToken);
        if (existing is not null) return existing;

        if (debit < 0m || credit < 0m || ((debit > 0m) == (credit > 0m)))
            throw new InvalidOperationException("A supplier ledger entry must contain exactly one positive debit or credit.");

        var balance = Money(supplier.CurrentBalance + credit - debit);
        supplier.CurrentBalance = balance;

        var entry = new SupplierLedgerEntryEntity
        {
            SupplierId = supplier.Id,
            ActorUserId = actorUserId,
            EntryType = entryType,
            Debit = debit,
            Credit = credit,
            BalanceAfter = balance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        context.SupplierLedgerEntries.Add(entry);
        return entry;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
