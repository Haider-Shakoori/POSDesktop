using System.Globalization;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalCustomerService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : ICustomerService
{
    public async Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(
        string? search = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!authorizer.HasPermission("customers.view") &&
            !authorizer.HasPermission("customers.manage") &&
            !authorizer.HasPermission("customers.quick_create") &&
            !authorizer.HasPermission("sales.create"))
            throw new InvalidOperationException("The user is not allowed to view customers.");

        search = search?.Trim();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Customers.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, like) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, like)) ||
                (x.AlternatePhone != null && EF.Functions.Like(x.AlternatePhone, like)));
        }

        return (await query.OrderBy(x => x.Name).Take(1000).ToListAsync(cancellationToken))
            .Select(MapSummary).ToList();
    }

    public async Task<CustomerDetail?> GetCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("customers.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await context.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
        if (customer is null) return null;

        var ledger = await context.CustomerLedgerEntries.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var outstanding = await context.Sales.AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.BalanceDue > 0m)
            .OrderBy(x => x.SoldAt).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var collections = await context.CustomerCollections.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.Id)
            .Take(300)
            .ToListAsync(cancellationToken);

        var methodCodes = collections.Select(x => x.PaymentMethodCode).Distinct().ToList();
        var methods = await context.PaymentMethods.AsNoTracking()
            .Where(x => methodCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var locale = sessions.Current?.PreferredLocale ?? "en";
        return new CustomerDetail(
            MapSummary(customer),
            ledger.Select(x => new CustomerLedgerRow(
                x.Id, x.EntryType, x.Debit, x.Credit, x.BalanceAfter,
                x.ReferenceNumber, x.OccurredAt, x.Notes)).ToList(),
            outstanding.Select(x => new CustomerOutstandingSale(
                x.Id, x.Number, x.SoldAt, x.NetTotal, x.PaidAmount,
                x.BalanceDue, x.PaymentStatus)).ToList(),
            collections.Select(x =>
            {
                methods.TryGetValue(x.PaymentMethodCode, out var method);
                return new CustomerCollectionRow(
                    x.Id, x.Number,
                    method is null ? x.PaymentMethodCode : Localize(method, locale),
                    x.Amount, x.TenderedAmount, x.ChangeAmount,
                    x.Reference, x.CollectedAt, x.Notes);
            }).ToList());
    }

    public async Task<CustomerSummary> SaveCustomerAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var canManage = authorizer.HasPermission("customers.manage");
        if (request.Id is null)
        {
            if (!canManage && !authorizer.HasPermission("customers.quick_create"))
                throw new InvalidOperationException("The user is not allowed to create customers.");
        }
        else if (!canManage)
        {
            throw new InvalidOperationException("The user is not allowed to edit customers.");
        }

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Customer name is required.");
        if (request.CreditLimit < 0m || request.OpeningBalance < 0m)
            throw new InvalidOperationException("Customer credit amounts cannot be negative.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        CustomerEntity customer;
        if (request.Id is null)
        {
            customer = new CustomerEntity
            {
                Name = name,
                Phone = Clean(request.Phone),
                AlternatePhone = Clean(request.AlternatePhone),
                Address = Clean(request.Address),
                CreditLimit = Money(request.CreditLimit),
                OpeningBalance = Money(request.OpeningBalance),
                CurrentBalance = 0m,
                IsActive = request.IsActive,
            };
            context.Customers.Add(customer);
            await context.SaveChangesAsync(cancellationToken);

            if (customer.OpeningBalance > 0m)
            {
                await CustomerLedgerWriter.DebitAsync(
                    context, customer, customer.OpeningBalance,
                    "opening_balance", "customer", customer.Id,
                    "OPEN-" + customer.Id.ToString(CultureInfo.InvariantCulture),
                    sessions.Current?.UserId, "Customer opening receivable", cancellationToken);
            }
        }
        else
        {
            customer = await context.Customers.SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Customer was not found.");

            if (Money(request.OpeningBalance) != customer.OpeningBalance)
                throw new InvalidOperationException("Opening balance is immutable after customer creation.");

            customer.Name = name;
            customer.Phone = Clean(request.Phone);
            customer.AlternatePhone = Clean(request.AlternatePhone);
            customer.Address = Clean(request.Address);
            customer.CreditLimit = Money(request.CreditLimit);
            customer.IsActive = request.IsActive;
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = sessions.Current?.UserId,
            Event = request.Id is null ? "customers.customer.created" : "customers.customer.updated",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"customer_id\":" + customer.Id.ToString(CultureInfo.InvariantCulture) + "}",
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapSummary(customer);
    }

    public async Task<CustomerCollectionResult> CollectAsync(
        CustomerCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("customers.collect");
        ValidateUuid(request.IdempotencyKey);
        var amount = Money(request.Amount);
        if (amount <= 0m) throw new InvalidOperationException("Collection amount must be greater than zero.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.CustomerCollections.Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CustomerId != request.CustomerId ||
                !string.Equals(existing.PaymentMethodCode, request.PaymentMethodCode, StringComparison.OrdinalIgnoreCase) ||
                existing.Amount != amount)
                throw new InvalidOperationException("The collection idempotency key is already bound to another collection payload.");

            var current = await context.Customers.AsNoTracking().SingleAsync(x => x.Id == existing.CustomerId, cancellationToken);
            return await MapCollectionAsync(context, existing, current.CurrentBalance, cancellationToken);
        }

        var customer = await context.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer was not found.");
        if (!customer.IsActive) throw new InvalidOperationException("Collections cannot be posted to an inactive customer.");
        if (amount > customer.CurrentBalance) throw new InvalidOperationException("Collection amount cannot exceed the customer balance.");

        var method = await context.PaymentMethods.SingleOrDefaultAsync(
            x => x.Code == request.PaymentMethodCode && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected payment method is unavailable.");

        var tendered = Money(request.TenderedAmount ?? amount);
        if (method.IsCash)
        {
            if (tendered < amount) throw new InvalidOperationException("Cash tendered cannot be less than the collection amount.");
            var openShift = await context.CashierShifts.AnyAsync(
                x => x.UserId == sessions.Current!.UserId && x.Status == "open", cancellationToken);
            if (!openShift) throw new InvalidOperationException("Open a cashier shift before recording a cash collection.");
        }
        else if (tendered != amount)
        {
            throw new InvalidOperationException("Non-cash tendered amount must equal the collection amount.");
        }

        var collectedAt = request.CollectedAt ?? DateTimeOffset.UtcNow;
        var collection = new CustomerCollectionEntity
        {
            Number = await NextNumberAsync(context, "customer_collection", "COL", collectedAt, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            CustomerId = customer.Id,
            PaymentMethodCode = method.Code,
            RecordedByUserId = sessions.Current!.UserId,
            Amount = amount,
            TenderedAmount = tendered,
            ChangeAmount = method.IsCash ? Money(tendered - amount) : 0m,
            Reference = Clean(request.Reference),
            CollectedAt = collectedAt,
            Notes = Clean(request.Notes),
        };
        context.CustomerCollections.Add(collection);
        await context.SaveChangesAsync(cancellationToken);

        await CustomerLedgerWriter.CreditAsync(
            context, customer, amount, "collection", "collection", collection.Id,
            collection.Number, sessions.Current!.UserId, "Customer collection", cancellationToken);

        var remaining = amount;
        var sales = await context.Sales
            .Where(x => x.CustomerId == customer.Id && x.BalanceDue > 0m)
            .OrderBy(x => x.SoldAt).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var sale in sales)
        {
            if (remaining <= 0m) break;
            var applied = Math.Min(sale.BalanceDue, remaining);
            applied = Money(applied);

            collection.Allocations.Add(new CustomerCollectionAllocationEntity
            {
                SaleId = sale.Id,
                Amount = applied,
            });

            sale.Payments.Add(new SalePaymentEntity
            {
                CustomerId = customer.Id,
                RecordedByUserId = sessions.Current!.UserId,
                IdempotencyKey = "collection:" + collection.Id + ":sale:" + sale.Id,
                MethodCode = method.Code,
                Amount = applied,
                TenderedAmount = applied,
                ChangeAmount = 0m,
                Reference = collection.Reference,
                SourceType = "collection",
                SourceId = collection.Id,
                PaidAt = collectedAt,
                Notes = "Allocated from " + collection.Number,
            });

            sale.PaidAmount = Money(sale.PaidAmount + applied);
            sale.BalanceDue = Money(sale.BalanceDue - applied);
            sale.PaymentStatus = sale.BalanceDue == 0m ? "paid" : "partial";
            remaining = Money(remaining - applied);
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = sessions.Current!.UserId,
            Event = "customers.collection.recorded",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"collection_number\":\"" + collection.Number + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapCollectionAsync(context, collection, customer.CurrentBalance, cancellationToken);
    }

    private static async Task<CustomerCollectionResult> MapCollectionAsync(
        PosDbContext context,
        CustomerCollectionEntity collection,
        decimal balanceAfter,
        CancellationToken cancellationToken)
    {
        if (!context.Entry(collection).Collection(x => x.Allocations).IsLoaded)
            await context.Entry(collection).Collection(x => x.Allocations).LoadAsync(cancellationToken);

        var saleIds = collection.Allocations.Select(x => x.SaleId).ToList();
        var sales = await context.Sales.AsNoTracking()
            .Where(x => saleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return new CustomerCollectionResult(
            collection.Id, collection.Number, collection.CustomerId,
            collection.Amount, collection.TenderedAmount, collection.ChangeAmount,
            balanceAfter, collection.CollectedAt,
            collection.Allocations.OrderBy(x => x.Id).Select(x =>
                new CustomerCollectionAllocationRow(
                    x.SaleId,
                    sales.GetValueOrDefault(x.SaleId)?.Number ?? string.Empty,
                    x.Amount)).ToList());
    }

    private static CustomerSummary MapSummary(CustomerEntity x) =>
        new(x.Id, x.Name, x.Phone, x.AlternatePhone, x.Address,
            x.CreditLimit, x.OpeningBalance, x.CurrentBalance, x.IsActive);

    private static string Localize(PaymentMethodEntity method, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(method.NameFa) => method.NameFa!,
            "ps" when !string.IsNullOrWhiteSpace(method.NamePs) => method.NamePs!,
            _ => method.NameEn,
        };

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

    private static void ValidateUuid(string value)
    {
        if (!Guid.TryParse(value, out _)) throw new InvalidOperationException("A valid idempotency key is required.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
