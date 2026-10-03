using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class CustomerLedgerWriter
{
    public static Task<CustomerLedgerEntryEntity> DebitAsync(
        PosDbContext context,
        CustomerEntity customer,
        decimal amount,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, customer, Money(amount), 0m, entryType, referenceType,
            referenceId, referenceNumber, actorUserId, notes, cancellationToken);

    public static Task<CustomerLedgerEntryEntity> CreditAsync(
        PosDbContext context,
        CustomerEntity customer,
        decimal amount,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        CancellationToken cancellationToken = default) =>
        RecordAsync(context, customer, 0m, Money(amount), entryType, referenceType,
            referenceId, referenceNumber, actorUserId, notes, cancellationToken);

    private static async Task<CustomerLedgerEntryEntity> RecordAsync(
        PosDbContext context,
        CustomerEntity customer,
        decimal debit,
        decimal credit,
        string entryType,
        string referenceType,
        long referenceId,
        string? referenceNumber,
        long? actorUserId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var existing = await context.CustomerLedgerEntries
            .SingleOrDefaultAsync(x =>
                x.CustomerId == customer.Id &&
                x.EntryType == entryType &&
                x.ReferenceType == referenceType &&
                x.ReferenceId == referenceId,
                cancellationToken);
        if (existing is not null) return existing;

        if (debit < 0m || credit < 0m || ((debit > 0m) == (credit > 0m)))
            throw new InvalidOperationException("A customer ledger entry must contain exactly one positive debit or credit.");

        var balance = Money(customer.CurrentBalance + debit - credit);
        if (balance < 0m)
            throw new InvalidOperationException("Customer balance cannot become negative.");

        customer.CurrentBalance = balance;
        var entry = new CustomerLedgerEntryEntity
        {
            CustomerId = customer.Id,
            ActorUserId = actorUserId,
            EntryType = entryType,
            Debit = debit,
            Credit = credit,
            BalanceAfter = balance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            OccurredAt = DateTimeOffset.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        context.CustomerLedgerEntries.Add(entry);
        return entry;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
