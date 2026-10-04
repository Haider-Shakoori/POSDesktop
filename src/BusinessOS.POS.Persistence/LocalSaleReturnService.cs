using System.Globalization;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalSaleReturnService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : ISaleReturnService
{
    public Task<SaleReturnResult> ReturnAsync(
        SaleReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.return");
        return PostAsync(
            request.IdempotencyKey,
            request.SaleId,
            request.Reason,
            request.Items,
            request.Refunds,
            "return",
            cancellationToken);
    }

    public Task<SaleReturnResult> VoidAsync(
        SaleVoidRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.void");
        return PostAsync(
            request.IdempotencyKey,
            request.SaleId,
            request.Reason,
            [],
            request.Refunds,
            "void",
            cancellationToken);
    }

    public async Task<IReadOnlyList<SaleReturnSummary>> GetReturnsAsync(
        long? saleId = null,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.SaleReturns.AsNoTracking().AsQueryable();
        if (saleId is not null) query = query.Where(x => x.SaleId == saleId.Value);

        var rows = await query.OrderByDescending(x => x.Id).Take(300).ToListAsync(cancellationToken);
        var saleIds = rows.Select(x => x.SaleId).Distinct().ToList();
        var sales = await context.Sales.AsNoTracking()
            .Where(x => saleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return rows.Select(x => new SaleReturnSummary(
            x.Id, x.Number, x.SaleId,
            sales.GetValueOrDefault(x.SaleId)?.Number ?? string.Empty,
            x.Type, x.Reason, x.ReturnTotal, x.ReceivableReversed,
            x.RefundTotal, x.PostedAt)).ToList();
    }


    public async Task<IReadOnlyList<SaleRefundMethod>> GetRefundMethodsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorizer.HasPermission("sales.return") && !authorizer.HasPermission("sales.void"))
            throw new InvalidOperationException("The user is not allowed to refund sales.");

        var locale = sessions.Current?.PreferredLocale ?? "en";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await context.PaymentMethods.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken))
            .Select(x => new SaleRefundMethod(
                x.Code,
                locale switch
                {
                    "fa" when !string.IsNullOrWhiteSpace(x.NameFa) => x.NameFa!,
                    "ps" when !string.IsNullOrWhiteSpace(x.NamePs) => x.NamePs!,
                    _ => x.NameEn,
                },
                x.IsCash))
            .ToList();
    }

    private async Task<SaleReturnResult> PostAsync(
        string idempotencyKey,
        long saleId,
        string reason,
        IReadOnlyList<SaleReturnLineRequest> requestedItems,
        IReadOnlyList<SaleRefundRequest> requestedRefunds,
        string type,
        CancellationToken cancellationToken)
    {
        ValidateUuid(idempotencyKey);
        reason = reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A return or void reason is required.");

        var user = sessions.Current ?? throw new InvalidOperationException("No user is signed in.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.SaleReturns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.SaleId != saleId || existing.Type != type || existing.Reason != reason)
                throw new InvalidOperationException("The return idempotency key is already bound to another reversal.");
            await transaction.CommitAsync(cancellationToken);
            var existingSale = await context.Sales.AsNoTracking().SingleAsync(x => x.Id == existing.SaleId, cancellationToken);
            return MapResult(existing, existingSale);
        }

        var sale = await context.Sales
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken)
            ?? throw new InvalidOperationException("Sale was not found.");

        if (sale.Status == "voided")
            throw new InvalidOperationException("A voided sale cannot be reversed again.");

        var priorReturnItems = await context.SaleReturnItems.AsNoTracking()
            .Join(context.SaleReturns.AsNoTracking(),
                item => item.SaleReturnId,
                ret => ret.Id,
                (item, ret) => new { item, ret })
            .Where(x => x.ret.SaleId == sale.Id)
            .Select(x => x.item)
            .ToListAsync(cancellationToken);

        var priorByItem = priorReturnItems
            .GroupBy(x => x.SaleItemId)
            .ToDictionary(
                x => x.Key,
                x => new PriorReturn(
                    Quantity(x.Sum(i => i.Quantity)),
                    Money(x.Sum(i => i.ReturnAmount)),
                    Money(x.Sum(i => i.CogsAmount))));

        var productUnitIds = sale.Items.Select(x => x.ProductUnitId).Distinct().ToList();
        var unitPrecision = await context.ProductUnits.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => productUnitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Unit.DecimalPlaces, cancellationToken);

        List<PreparedReturn> prepared;
        if (type == "void")
        {
            prepared = sale.Items
                .Select(item =>
                {
                    var alreadyReturned = priorByItem.GetValueOrDefault(item.Id)?.QuantityReturned ?? 0m;
                    var remaining = Quantity(item.Quantity - alreadyReturned);
                    return remaining <= 0m
                        ? null
                        : PrepareLine(item, remaining, unitPrecision[item.ProductUnitId], priorByItem);
                })
                .Where(x => x is not null)
                .Cast<PreparedReturn>()
                .ToList();
        }
        else
        {
            if (requestedItems.Count == 0)
                throw new InvalidOperationException("A return requires at least one item.");
            if (requestedItems.Select(x => x.SaleItemId).Distinct().Count() != requestedItems.Count)
                throw new InvalidOperationException("Each returned sale item must be unique.");

            prepared = [];
            foreach (var input in requestedItems)
            {
                var item = sale.Items.SingleOrDefault(x => x.Id == input.SaleItemId)
                    ?? throw new InvalidOperationException("The selected item does not belong to this sale.");
                var line = PrepareLine(item, input.Quantity, unitPrecision[item.ProductUnitId], priorByItem)
                    ?? throw new InvalidOperationException("No returnable sale quantity remains.");
                prepared.Add(line);
            }
        }

        if (prepared.Count == 0)
            throw new InvalidOperationException("No returnable sale quantity remains.");

        var returnTotal = Money(prepared.Sum(x => x.ReturnAmount));
        var receivableReversed = sale.CustomerId is not null
            ? Math.Min(returnTotal, sale.BalanceDue)
            : 0m;
        receivableReversed = Money(receivableReversed);
        var refundDue = Money(returnTotal - receivableReversed);
        var refundablePaid = Money(sale.PaidAmount - sale.RefundedTotal);
        if (refundDue > refundablePaid)
            throw new InvalidOperationException("The reversal exceeds the refundable paid amount for this sale.");

        var refunds = await PrepareRefundsAsync(context, requestedRefunds, refundDue, user.UserId, cancellationToken);

        var postedAt = DateTimeOffset.UtcNow;
        await BusinessDayGuard.EnsureOpenAsync(context, postedAt, cancellationToken);
        var saleReturn = new SaleReturnEntity
        {
            Number = await NextNumberAsync(context, "sale_return", type == "void" ? "VOID" : "RET", postedAt, cancellationToken),
            IdempotencyKey = idempotencyKey,
            SaleId = sale.Id,
            CreatedByUserId = user.UserId,
            BusinessDate = BusinessDayGuard.LocalBusinessDate(postedAt),
            Type = type,
            Status = "posted",
            Reason = reason,
            ReturnTotal = returnTotal,
            ReceivableReversed = receivableReversed,
            RefundTotal = refundDue,
            PostedAt = postedAt,
        };
        context.SaleReturns.Add(saleReturn);
        await context.SaveChangesAsync(cancellationToken);

        decimal cogsReversed = 0m;
        foreach (var line in prepared)
        {
            var returnItem = new SaleReturnItemEntity
            {
                SaleReturnId = saleReturn.Id,
                SaleItemId = line.SaleItem.Id,
                Quantity = line.Quantity,
                QuantityBase = line.QuantityBase,
                ReturnAmount = line.ReturnAmount,
                CogsAmount = line.CogsAmount,
            };
            context.SaleReturnItems.Add(returnItem);
            await context.SaveChangesAsync(cancellationToken);

            await RestoreStockAsync(
                context, sale, line.SaleItem, returnItem, type, saleReturn.Number,
                line.QuantityBase, line.CogsAmount, user.UserId, postedAt, cancellationToken);
            cogsReversed = Money(cogsReversed + line.CogsAmount);
        }

        saleReturn.CogsReversed = cogsReversed;

        if (receivableReversed > 0m && sale.CustomerId is not null)
        {
            var customer = await context.Customers.SingleAsync(x => x.Id == sale.CustomerId.Value, cancellationToken);
            await CustomerLedgerWriter.CreditAsync(
                context, customer, receivableReversed,
                type == "void" ? "sale_void" : "sale_return",
                "sale_return", saleReturn.Id, saleReturn.Number,
                user.UserId, char.ToUpperInvariant(type[0]) + type[1..] + " receivable reversal for " + sale.Number,
                cancellationToken);
        }

        for (var i = 0; i < refunds.Count; i++)
        {
            var refund = refunds[i];
            var refundEntity = new SaleRefundEntity
            {
                IdempotencyKey = idempotencyKey + ":refund:" + i,
                SaleReturnId = saleReturn.Id,
                PaymentMethodCode = refund.Method.Code,
                RecordedByUserId = user.UserId,
                Amount = refund.Amount,
                Reference = refund.Reference,
                RefundedAt = postedAt,
                Notes = refund.Notes,
            };
            context.SaleRefunds.Add(refundEntity);
            await context.SaveChangesAsync(cancellationToken);

            if (refund.Method.IsCash)
            {
                await CashLedgerEngine.RecordAsync(
                    context, user.UserId, refundEntity.Amount,
                    "outflow", "sale_refund", "sale_refund", refundEntity.Id,
                    saleReturn.Number, "Cash sale refund", postedAt,
                    "sale-refund:" + refundEntity.Id, null, cancellationToken);
            }
        }

        sale.ReturnedTotal = Money(sale.ReturnedTotal + returnTotal);
        sale.ReceivableReversedTotal = Money(sale.ReceivableReversedTotal + receivableReversed);
        sale.RefundedTotal = Money(sale.RefundedTotal + refundDue);
        sale.BalanceDue = Money(sale.BalanceDue - receivableReversed);
        sale.Status = type == "void"
            ? "voided"
            : sale.ReturnedTotal >= sale.NetTotal ? "returned" : "partially_returned";

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = type == "void" ? "sales.sale.voided" : "sales.sale.returned",
            CreatedAt = postedAt,
            DetailsJson = "{\"return_number\":\"" + saleReturn.Number + "\",\"return_total\":\"" +
                          returnTotal.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(saleReturn, sale);
    }

    private PreparedReturn? PrepareLine(
        SaleItemEntity item,
        decimal requestedQuantity,
        int decimalPlaces,
        IReadOnlyDictionary<long, PriorReturn> priorByItem)
    {
        if (requestedQuantity <= 0m)
            throw new InvalidOperationException("Return quantity must be greater than zero.");

        var prior = priorByItem.GetValueOrDefault(item.Id) ?? new PriorReturn(0m, 0m, 0m);
        var remainingQuantity = Quantity(item.Quantity - prior.QuantityReturned);
        if (remainingQuantity <= 0m) return null;
        if (requestedQuantity > remainingQuantity)
            throw new InvalidOperationException("Return quantity exceeds the remaining returnable quantity.");

        EnsurePrecision(requestedQuantity, decimalPlaces);
        var remainingAmount = Money(item.LineNetTotal - prior.AmountReturned);
        var remainingCogs = Money(item.CogsAmount - prior.CogsReturned);
        var quantity = Quantity(requestedQuantity);
        var quantityBase = Quantity(quantity * item.ConversionFactor);

        var amount = quantity == remainingQuantity
            ? remainingAmount
            : Money(quantity * Money(item.LineNetTotal / item.Quantity));
        if (amount > remainingAmount) amount = remainingAmount;

        var cogs = quantity == remainingQuantity
            ? remainingCogs
            : Money(quantityBase * (item.QuantityBase == 0m ? 0m : item.CogsAmount / item.QuantityBase));
        if (cogs > remainingCogs) cogs = remainingCogs;

        return new PreparedReturn(item, quantity, quantityBase, amount, cogs);
    }

    private async Task<List<PreparedRefund>> PrepareRefundsAsync(
        PosDbContext context,
        IReadOnlyList<SaleRefundRequest> requested,
        decimal refundDue,
        long userId,
        CancellationToken cancellationToken)
    {
        if (refundDue <= 0m) return [];
        if (requested.Count == 0)
            throw new InvalidOperationException("Refund payment evidence is required for the paid portion of this reversal.");

        var methods = await context.PaymentMethods.Where(x => x.IsActive).ToListAsync(cancellationToken);
        var byCode = methods.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var prepared = new List<PreparedRefund>(requested.Count);
        decimal total = 0m;
        foreach (var input in requested)
        {
            if (!byCode.TryGetValue(input.PaymentMethodCode, out var method))
                throw new InvalidOperationException("The selected refund payment method is unavailable.");
            var amount = Money(input.Amount);
            if (amount <= 0m) throw new InvalidOperationException("Refund amount must be greater than zero.");

            if (method.IsCash)
            {
                var shiftOpen = await context.CashierShifts.AnyAsync(
                    x => x.UserId == userId && x.Status == "open", cancellationToken);
                if (!shiftOpen) throw new InvalidOperationException("Open a cashier shift before posting a cash refund.");
            }

            total = Money(total + amount);
            prepared.Add(new PreparedRefund(method, amount, Clean(input.Reference), Clean(input.Notes)));
        }

        if (total != refundDue)
            throw new InvalidOperationException("Refund payment amounts must equal the paid portion being reversed.");
        return prepared;
    }

    private async Task RestoreStockAsync(
        PosDbContext context,
        SaleEntity sale,
        SaleItemEntity saleItem,
        SaleReturnItemEntity returnItem,
        string type,
        string returnNumber,
        decimal quantityBase,
        decimal cogsAmount,
        long actorUserId,
        DateTimeOffset postedAt,
        CancellationToken cancellationToken)
    {
        var product = await context.Products.SingleAsync(x => x.Id == saleItem.ProductId, cancellationToken);
        if (!product.TrackStock || quantityBase <= 0m) return;

        var originalMovements = await context.StockMovements
            .Where(x =>
                x.MovementType == "sale" &&
                x.ReferenceType == "sale" &&
                x.ReferenceId == sale.Id &&
                (x.SaleItemId == saleItem.Id ||
                 (x.SaleItemId == null && x.ProductId == saleItem.ProductId)))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var restoredMovements = await context.StockMovements.AsNoTracking()
            .Where(x =>
                x.SaleItemId == saleItem.Id &&
                (x.MovementType == "sale_return" || x.MovementType == "sale_void"))
            .ToListAsync(cancellationToken);

        static long BatchKey(long? batchId) => batchId ?? 0L;

        var restoredByBatch = restoredMovements
            .GroupBy(x => BatchKey(x.ProductBatchId))
            .ToDictionary(x => x.Key, x => Quantity(x.Sum(m => m.QuantityBase)));

        var unitCostBase = quantityBase == 0m ? 0m : decimal.Round(cogsAmount / quantityBase, 4, MidpointRounding.AwayFromZero);
        var remaining = quantityBase;

        foreach (var original in originalMovements)
        {
            if (remaining <= 0m) break;
            var originallySold = Quantity(-original.QuantityBase);
            var alreadyRestored = restoredByBatch.GetValueOrDefault(BatchKey(original.ProductBatchId), 0m);
            var availableToRestore = Quantity(Math.Max(0m, originallySold - alreadyRestored));
            if (availableToRestore <= 0m) continue;

            var take = Math.Min(remaining, availableToRestore);
            ProductBatchEntity? batch = null;
            if (original.ProductBatchId is not null)
            {
                batch = await context.ProductBatches.SingleAsync(x => x.Id == original.ProductBatchId.Value, cancellationToken);
                batch.StockOnHand = Quantity(batch.StockOnHand + take);
            }

            product.StockOnHand = Quantity(product.StockOnHand + take);
            var movement = new StockMovementEntity
            {
                ProductId = product.Id,
                ProductBatchId = batch?.Id,
                SaleItemId = saleItem.Id,
                ActorUserId = actorUserId,
                MovementType = type == "void" ? "sale_void" : "sale_return",
                QuantityBase = take,
                BalanceAfter = product.StockOnHand,
                BatchBalanceAfter = batch?.StockOnHand,
                UnitCostBase = unitCostBase,
                ReferenceType = "sale_return",
                ReferenceId = returnItem.SaleReturnId,
                IdempotencyKey = "sale-return:" + returnItem.Id + ":batch:" + (batch?.Id.ToString(CultureInfo.InvariantCulture) ?? "none"),
                Notes = char.ToUpperInvariant(type[0]) + type[1..] + " " + returnNumber + " for sale " + sale.Number,
                OccurredAt = postedAt,
            };
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken);

            context.InventoryCostLayers.Add(new InventoryCostLayerEntity
            {
                ProductId = product.Id,
                ProductBatchId = batch?.Id,
                SourceStockMovementId = movement.Id,
                InitialQuantityBase = take,
                RemainingQuantityBase = take,
                UnitCostBase = unitCostBase,
                ReceivedAt = postedAt,
            });
            remaining = Quantity(remaining - take);
        }

        if (remaining > 0m)
            throw new InvalidOperationException("The original stock allocation for this sale cannot be fully restored.");
    }

    private static SaleReturnResult MapResult(SaleReturnEntity row, SaleEntity sale) =>
        new(row.Id, row.Number, row.SaleId, row.Type, row.ReturnTotal, row.CogsReversed,
            row.ReceivableReversed, row.RefundTotal, sale.Status, sale.BalanceDue, row.PostedAt);

    private static async Task<string> NextNumberAsync(
        PosDbContext context,
        string key,
        string prefix,
        DateTimeOffset at,
        CancellationToken cancellationToken)
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
        if (!Guid.TryParse(value, out _))
            throw new InvalidOperationException("A valid idempotency key is required.");
    }

    private static void EnsurePrecision(decimal requested, int decimalPlaces)
    {
        var rounded = decimal.Round(requested, decimalPlaces, MidpointRounding.AwayFromZero);
        if (rounded != requested)
            throw new InvalidOperationException("Return quantity exceeds the selected unit precision.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private sealed record PriorReturn(decimal QuantityReturned, decimal AmountReturned, decimal CogsReturned);
    private sealed record PreparedReturn(
        SaleItemEntity SaleItem,
        decimal Quantity,
        decimal QuantityBase,
        decimal ReturnAmount,
        decimal CogsAmount);
    private sealed record PreparedRefund(
        PaymentMethodEntity Method,
        decimal Amount,
        string? Reference,
        string? Notes);
}
