using System.Globalization;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalPurchasingService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IPurchasingService
{
    private static readonly HashSet<string> ExpenseTypes =
        new(["transport", "loading", "unloading", "freight", "other"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PurchasePaymentMethods =
        new(["cash", "bank", "other"], StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync(
        string? search = null,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        DemandSupplierView();
        search = Clean(search);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Suppliers.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, like) ||
                (x.ContactPerson != null && EF.Functions.Like(x.ContactPerson, like)) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, like)) ||
                (x.AlternatePhone != null && EF.Functions.Like(x.AlternatePhone, like)));
        }

        return (await query.OrderBy(x => x.Name).Take(1000).ToListAsync(cancellationToken))
            .Select(MapSupplier).ToList();
    }

    public async Task<SupplierDetail?> GetSupplierAsync(
        long supplierId,
        CancellationToken cancellationToken = default)
    {
        DemandSupplierView();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var supplier = await context.Suppliers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == supplierId, cancellationToken);
        if (supplier is null) return null;

        var ledger = await context.SupplierLedgerEntries.AsNoTracking()
            .Where(x => x.SupplierId == supplierId)
            .OrderByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(x => x.SupplierId == supplierId && x.BalanceDue > 0m)
            .OrderBy(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var payments = await context.SupplierPayments.AsNoTracking()
            .Where(x => x.SupplierId == supplierId)
            .OrderByDescending(x => x.Id)
            .Take(300)
            .ToListAsync(cancellationToken);

        return new SupplierDetail(
            MapSupplier(supplier),
            ledger.Select(x => new SupplierLedgerRow(
                x.Id, x.EntryType, x.Debit, x.Credit, x.BalanceAfter,
                x.ReferenceNumber, x.OccurredAt, x.Notes)).ToList(),
            receipts.Select(x => new SupplierOpenReceiptRow(
                x.Id, x.Number, x.ReceivedAt, x.NetTotal, x.PaidAmount,
                x.ReturnedTotal, x.BalanceDue)).ToList(),
            payments.Select(x => new SupplierPaymentRow(
                x.Id, x.Number, x.MethodCode, x.Amount, x.Reference, x.PaidAt, x.Notes)).ToList());
    }

    public async Task<SupplierSummary> SaveSupplierAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("suppliers.manage");
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Supplier name is required.");
        if (name.Length > 180) throw new InvalidOperationException("Supplier name is too long.");
        if (request.OpeningBalance < 0m) throw new InvalidOperationException("Supplier opening balance cannot be negative.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = RequireUser();

        SupplierEntity supplier;
        if (request.Id is null)
        {
            supplier = new SupplierEntity
            {
                Name = name,
                ContactPerson = Clean(request.ContactPerson),
                Phone = Clean(request.Phone),
                AlternatePhone = Clean(request.AlternatePhone),
                Address = Clean(request.Address),
                OpeningBalance = Money(request.OpeningBalance),
                CurrentBalance = 0m,
                Notes = Clean(request.Notes),
                IsActive = request.IsActive,
            };
            context.Suppliers.Add(supplier);
            await context.SaveChangesAsync(cancellationToken);

            if (supplier.OpeningBalance > 0m)
            {
                await SupplierLedgerWriter.CreditAsync(
                    context, supplier, supplier.OpeningBalance,
                    "opening_balance", "supplier", supplier.Id,
                    null, user.UserId, "Supplier opening payable",
                    DateTimeOffset.UtcNow, cancellationToken);
            }
        }
        else
        {
            supplier = await context.Suppliers.SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Supplier was not found.");

            if (Money(request.OpeningBalance) != supplier.OpeningBalance)
                throw new InvalidOperationException("Opening balance is immutable after supplier creation.");

            supplier.Name = name;
            supplier.ContactPerson = Clean(request.ContactPerson);
            supplier.Phone = Clean(request.Phone);
            supplier.AlternatePhone = Clean(request.AlternatePhone);
            supplier.Address = Clean(request.Address);
            supplier.Notes = Clean(request.Notes);
            supplier.IsActive = request.IsActive;
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = request.Id is null ? "purchasing.supplier.created" : "purchasing.supplier.updated",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"supplier_id\":" + supplier.Id.ToString(CultureInfo.InvariantCulture) + "}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapSupplier(supplier);
    }

    public async Task<IReadOnlyList<PurchaseProductOption>> GetPurchasableProductsAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.view");
        var locale = RequireUser().PreferredLocale;
        search = Clean(search);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ProductUnits.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => x.CanPurchase && x.Product.IsActive && x.Unit.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Product.Sku, like) ||
                EF.Functions.Like(x.Product.NameEn, like) ||
                (x.Product.NameFa != null && EF.Functions.Like(x.Product.NameFa, like)) ||
                (x.Product.NamePs != null && EF.Functions.Like(x.Product.NamePs, like)));
        }

        return (await query.OrderBy(x => x.Product.NameEn).ThenBy(x => x.Id).Take(500).ToListAsync(cancellationToken))
            .Select(x => new PurchaseProductOption(
                x.Id, x.ProductId, x.Product.Sku,
                Localize(x.Product.NameEn, x.Product.NameFa, x.Product.NamePs, locale),
                Localize(x.Unit.NameEn, x.Unit.NameFa, x.Unit.NamePs, locale),
                x.Unit.DecimalPlaces, x.ConversionFactor, x.Product.PurchaseCost, x.Product.TrackExpiry))
            .ToList();
    }

    public async Task<IReadOnlyList<PurchaseOrderSummary>> GetPurchaseOrdersAsync(
        long? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.PurchaseOrders.AsNoTracking().AsQueryable();
        if (supplierId is not null) query = query.Where(x => x.SupplierId == supplierId.Value);
        var rows = await query.OrderByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);
        var names = await SupplierNamesAsync(context, rows.Select(x => x.SupplierId), cancellationToken);
        return rows.Select(x => MapOrderSummary(x, names.GetValueOrDefault(x.SupplierId) ?? "Unknown")).ToList();
    }

    public async Task<PurchaseOrderDetail?> GetPurchaseOrderAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadOrderDetailAsync(context, orderId, cancellationToken);
    }

    public async Task<PurchaseOrderDetail> CreatePurchaseOrderAsync(
        PurchaseOrderCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.create");
        if (request.Items.Count == 0) throw new InvalidOperationException("A purchase order requires at least one item.");
        if (request.ExpectedDate is not null && request.ExpectedDate.Value.Date < request.OrderDate.Date)
            throw new InvalidOperationException("Expected date cannot be before the order date.");

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var supplier = await context.Suppliers.SingleOrDefaultAsync(
            x => x.Id == request.SupplierId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected supplier is not active.");

        var unitIds = request.Items.Select(x => x.ProductUnitId).ToList();
        if (unitIds.Distinct().Count() != unitIds.Count)
            throw new InvalidOperationException("The same product unit cannot appear twice on one purchase order.");

        var units = await context.ProductUnits
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => unitIds.Contains(x.Id) && x.CanPurchase && x.Product.IsActive && x.Unit.IsActive)
            .ToListAsync(cancellationToken);
        if (units.Count != unitIds.Count)
            throw new InvalidOperationException("One or more purchase products are unavailable.");

        var byId = units.ToDictionary(x => x.Id);
        var prepared = new List<PreparedOrderLine>();
        decimal subtotal = 0m, lineDiscount = 0m, lineNet = 0m;

        foreach (var line in request.Items)
        {
            var unit = byId[line.ProductUnitId];
            if (line.Quantity <= 0m) throw new InvalidOperationException("Purchase-order quantity must be greater than zero.");
            EnsurePrecision(line.Quantity, unit.Unit.DecimalPlaces, "Purchase-order quantity");
            var cost = Cost(line.UnitCost);
            if (cost < 0m) throw new InvalidOperationException("Unit cost cannot be negative.");

            var lineSubtotal = Money(line.Quantity * cost);
            var discount = Money(line.LineDiscountAmount);
            if (discount < 0m || discount > lineSubtotal)
                throw new InvalidOperationException("Purchase-order line discount is invalid.");

            var net = Money(lineSubtotal - discount);
            prepared.Add(new PreparedOrderLine(unit, Quantity(line.Quantity), cost, lineSubtotal, discount, net, Clean(line.Notes)));
            subtotal = Money(subtotal + lineSubtotal);
            lineDiscount = Money(lineDiscount + discount);
            lineNet = Money(lineNet + net);
        }

        var orderDiscount = Money(request.OrderDiscountAmount);
        if (orderDiscount < 0m || orderDiscount > lineNet)
            throw new InvalidOperationException("Purchase-order discount is invalid.");

        var now = DateTimeOffset.UtcNow;
        var order = new PurchaseOrderEntity
        {
            Number = await NextNumberAsync(context, "purchase_order", "PO", now, cancellationToken),
            SupplierId = supplier.Id,
            CreatedByUserId = user.UserId,
            Status = "draft",
            OrderDate = request.OrderDate.Date,
            ExpectedDate = request.ExpectedDate?.Date,
            SupplierReference = Clean(request.SupplierReference),
            Subtotal = subtotal,
            LineDiscountTotal = lineDiscount,
            OrderDiscountAmount = orderDiscount,
            NetTotal = Money(lineNet - orderDiscount),
            CreatedAt = now,
            Notes = Clean(request.Notes),
        };
        context.PurchaseOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var line in prepared)
        {
            order.Items.Add(new PurchaseOrderItemEntity
            {
                ProductId = line.Unit.ProductId,
                ProductUnitId = line.Unit.Id,
                OrderedQuantity = line.Quantity,
                ReceivedQuantity = 0m,
                UnitCost = line.UnitCost,
                LineSubtotal = line.Subtotal,
                LineDiscountAmount = line.Discount,
                LineNetTotal = line.Net,
                Notes = line.Notes,
            });
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "purchasing.order.created",
            CreatedAt = now,
            DetailsJson = "{\"number\":\"" + order.Number + "\",\"supplier_id\":" + supplier.Id + "}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await LoadOrderDetailAsync(context, order.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDetail> ApprovePurchaseOrderAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.approve");
        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await context.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");
        if (order.Status != "draft") throw new InvalidOperationException("Only draft purchase orders can be approved.");

        order.Status = "approved";
        order.ApprovedByUserId = user.UserId;
        order.ApprovedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return (await LoadOrderDetailAsync(context, order.Id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDetail> CancelPurchaseOrderAsync(
        long orderId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.approve");
        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await context.PurchaseOrders.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (order.Status is not ("draft" or "approved"))
            throw new InvalidOperationException("This purchase order cannot be cancelled.");
        if (order.Items.Any(x => x.ReceivedQuantity > 0m))
            throw new InvalidOperationException("A purchase order with received stock cannot be cancelled.");

        order.Status = "cancelled";
        order.CancelledAt = DateTimeOffset.UtcNow;
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "purchasing.order.cancelled",
            CreatedAt = order.CancelledAt.Value,
            DetailsJson = "{\"purchase_order_id\":" + order.Id + "}",
        });
        await context.SaveChangesAsync(cancellationToken);
        return (await LoadOrderDetailAsync(context, order.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<GoodsReceiptSummary>> GetGoodsReceiptsAsync(
        long? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.GoodsReceipts.AsNoTracking().AsQueryable();
        if (supplierId is not null) query = query.Where(x => x.SupplierId == supplierId.Value);
        var rows = await query.OrderByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);
        var names = await SupplierNamesAsync(context, rows.Select(x => x.SupplierId), cancellationToken);
        return rows.Select(x => MapReceiptSummary(x, names.GetValueOrDefault(x.SupplierId) ?? "Unknown")).ToList();
    }

    public async Task<GoodsReceiptDetail?> GetGoodsReceiptAsync(
        long receiptId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadReceiptDetailAsync(context, receiptId, cancellationToken);
    }

    public async Task<GoodsReceiptDetail> PostGoodsReceiptAsync(
        GoodsReceiptPostRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.receive");
        ValidateUuid(request.IdempotencyKey);
        if (request.Items.Count == 0) throw new InvalidOperationException("A goods receipt requires at least one item.");
        if (request.PurchaseOrderId is null) authorizer.Demand("purchases.direct_receive");

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.GoodsReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.SupplierId != request.SupplierId || existing.PurchaseOrderId != request.PurchaseOrderId)
                throw new InvalidOperationException("The idempotency key is already bound to another goods receipt.");
            await transaction.CommitAsync(cancellationToken);
            return (await LoadReceiptDetailAsync(context, existing.Id, cancellationToken))!;
        }

        var supplier = await context.Suppliers.SingleOrDefaultAsync(
            x => x.Id == request.SupplierId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected supplier is not active.");

        PurchaseOrderEntity? order = null;
        Dictionary<long, PurchaseOrderItemEntity> poItems = [];
        if (request.PurchaseOrderId is not null)
        {
            order = await context.PurchaseOrders.Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Purchase order was not found.");
            if (order.Status is not ("approved" or "partially_received"))
                throw new InvalidOperationException("The purchase order is not open for receiving.");
            if (order.SupplierId != supplier.Id)
                throw new InvalidOperationException("Goods receipt supplier must match the purchase order supplier.");
            poItems = order.Items.ToDictionary(x => x.Id);
        }

        var requestedUnitIds = request.Items
            .Where(x => x.ProductUnitId is not null)
            .Select(x => x.ProductUnitId!.Value)
            .Concat(poItems.Values.Select(x => x.ProductUnitId))
            .Distinct()
            .ToList();

        var productUnits = await context.ProductUnits
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => requestedUnitIds.Contains(x.Id) && x.CanPurchase && x.Product.IsActive && x.Unit.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var seenOrderItems = new HashSet<long>();
        var seenDirectUnits = new HashSet<long>();
        var prepared = new List<PreparedReceiptLine>();
        decimal subtotal = 0m, lineDiscountTotal = 0m, basisTotal = 0m;

        foreach (var input in request.Items)
        {
            PurchaseOrderItemEntity? poItem = null;
            ProductUnitEntity unit;

            if (order is not null)
            {
                if (input.PurchaseOrderItemId is null || !poItems.TryGetValue(input.PurchaseOrderItemId.Value, out poItem))
                    throw new InvalidOperationException("Each receipt line must reference an item from the selected purchase order.");
                if (!seenOrderItems.Add(poItem.Id))
                    throw new InvalidOperationException("A purchase-order item cannot appear twice on one receipt.");
                if (!productUnits.TryGetValue(poItem.ProductUnitId, out unit!))
                    throw new InvalidOperationException("A purchase-order product is unavailable.");
            }
            else
            {
                if (input.ProductUnitId is null)
                    throw new InvalidOperationException("Direct receipt lines require a product unit.");
                if (!seenDirectUnits.Add(input.ProductUnitId.Value))
                    throw new InvalidOperationException("The same product unit cannot appear twice on one direct receipt.");
                if (!productUnits.TryGetValue(input.ProductUnitId.Value, out unit!))
                    throw new InvalidOperationException("A purchase product is unavailable.");
                if (input.UnitCost is null)
                    throw new InvalidOperationException("Direct receipt lines require an explicit unit cost.");
            }

            if (input.Quantity <= 0m) throw new InvalidOperationException("Received quantity must be greater than zero.");
            EnsurePrecision(input.Quantity, unit.Unit.DecimalPlaces, "Received quantity");

            if (poItem is not null)
            {
                var remaining = Quantity(poItem.OrderedQuantity - poItem.ReceivedQuantity);
                if (input.Quantity > remaining)
                    throw new InvalidOperationException("Received quantity exceeds the remaining purchase-order quantity.");
            }

            var unitCost = Cost(input.UnitCost ?? poItem!.UnitCost);
            if (unitCost < 0m) throw new InvalidOperationException("Received unit cost cannot be negative.");
            var lineSubtotal = Money(input.Quantity * unitCost);
            var lineDiscount = Money(input.LineDiscountAmount);
            if (lineDiscount < 0m || lineDiscount > lineSubtotal)
                throw new InvalidOperationException("Receipt line discount is invalid.");

            if (input.ManufacturedAt is not null && input.ExpiresAt is not null &&
                input.ExpiresAt.Value.Date < input.ManufacturedAt.Value.Date)
                throw new InvalidOperationException("Expiry date cannot be before manufacture date.");

            if (unit.Product.TrackExpiry &&
                (string.IsNullOrWhiteSpace(input.BatchNumber) || input.ExpiresAt is null))
                throw new InvalidOperationException("Batch number and expiry date are required for expiry-tracked products.");

            var basis = Money(lineSubtotal - lineDiscount);
            var line = new PreparedReceiptLine(
                poItem, unit, Quantity(input.Quantity), unitCost,
                lineSubtotal, lineDiscount, basis,
                Clean(input.BatchNumber), input.ManufacturedAt?.Date, input.ExpiresAt?.Date);
            prepared.Add(line);
            subtotal = Money(subtotal + lineSubtotal);
            lineDiscountTotal = Money(lineDiscountTotal + lineDiscount);
            basisTotal = Money(basisTotal + basis);
        }

        var receiptDiscount = Money(request.ReceiptDiscountAmount);
        if (receiptDiscount < 0m || receiptDiscount > basisTotal)
            throw new InvalidOperationException("Receipt discount is invalid.");

        var preparedExpenses = new List<GoodsReceiptExpenseRequest>();
        decimal expenseTotal = 0m;
        foreach (var expense in request.Expenses)
        {
            if (!ExpenseTypes.Contains(expense.Type))
                throw new InvalidOperationException("Invalid purchase expense type.");
            var amount = Money(expense.Amount);
            if (amount <= 0m) throw new InvalidOperationException("Purchase expense amount must be greater than zero.");
            preparedExpenses.Add(expense with { Type = expense.Type.ToLowerInvariant(), Amount = amount, Description = Clean(expense.Description) });
            expenseTotal = Money(expenseTotal + amount);
        }

        var discountAllocations = Allocate(receiptDiscount, prepared.Select(x => x.Basis).ToList());
        var discountedWeights = prepared.Select((x, i) => Money(x.Basis - discountAllocations[i])).ToList();
        if (!discountedWeights.Any(x => x > 0m))
            discountedWeights = prepared.Select(x => Quantity(x.Quantity * x.Unit.ConversionFactor)).ToList();
        var expenseAllocations = Allocate(expenseTotal, discountedWeights);

        var netTotal = Money(basisTotal - receiptDiscount + expenseTotal);
        var paidAmount = Money(request.PaidAmount);
        if (paidAmount < 0m || paidAmount > netTotal)
            throw new InvalidOperationException("Paid amount cannot exceed the goods receipt total.");

        var paymentMethod = Clean(request.PaymentMethodCode)?.ToLowerInvariant();
        if (paidAmount > 0m)
        {
            authorizer.Demand("purchases.record_payment");
            ValidatePurchasePaymentMethod(paymentMethod);
            if (paymentMethod == "cash")
                await DemandOpenCashShiftAsync(context, user.UserId, cancellationToken);
        }

        var receivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow;
        await BusinessDayGuard.EnsureOpenAsync(context, receivedAt, cancellationToken);
        var receipt = new GoodsReceiptEntity
        {
            Number = await NextNumberAsync(context, "goods_receipt", "GRN", receivedAt, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            SupplierId = supplier.Id,
            PurchaseOrderId = order?.Id,
            CreatedByUserId = user.UserId,
            PostedByUserId = user.UserId,
            Status = "posted",
            SupplierInvoiceReference = Clean(request.SupplierInvoiceReference),
            ReceivedAt = receivedAt,
            Subtotal = subtotal,
            LineDiscountTotal = lineDiscountTotal,
            ReceiptDiscountAmount = receiptDiscount,
            ExpenseTotal = expenseTotal,
            NetTotal = netTotal,
            PaidAmount = paidAmount,
            BalanceDue = Money(netTotal - paidAmount),
            ReturnedTotal = 0m,
            PostedAt = DateTimeOffset.UtcNow,
            Notes = Clean(request.Notes),
        };
        context.GoodsReceipts.Add(receipt);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var expense in preparedExpenses)
        {
            receipt.Expenses.Add(new GoodsReceiptExpenseEntity
            {
                Type = expense.Type,
                Description = expense.Description,
                Amount = expense.Amount,
            });
        }

        for (var index = 0; index < prepared.Count; index++)
        {
            var line = prepared[index];
            var allocatedDiscount = discountAllocations[index];
            var discountedBasis = Money(line.Basis - allocatedDiscount);
            var allocatedExpense = expenseAllocations[index];
            var landedTotal = Money(discountedBasis + allocatedExpense);
            var quantityBase = Quantity(line.Quantity * line.Unit.ConversionFactor);
            var sourceUnitLanded = Cost(landedTotal / line.Quantity);
            var baseUnitLanded = Cost(quantityBase == 0m ? 0m : landedTotal / quantityBase);

            ProductBatchEntity? batch = null;
            if (line.Unit.Product.TrackExpiry)
            {
                batch = await context.ProductBatches.SingleOrDefaultAsync(
                    x => x.ProductId == line.Unit.ProductId && x.BatchNumber == line.BatchNumber!, cancellationToken);
                if (batch is null)
                {
                    batch = new ProductBatchEntity
                    {
                        ProductId = line.Unit.ProductId,
                        BatchNumber = line.BatchNumber!,
                        ManufacturedAt = line.ManufacturedAt,
                        ExpiresAt = line.ExpiresAt,
                        StockOnHand = 0m,
                        IsBlocked = false,
                        Notes = "Received on " + receipt.Number,
                    };
                    context.ProductBatches.Add(batch);
                    await context.SaveChangesAsync(cancellationToken);
                }
                else if (batch.ExpiresAt?.Date != line.ExpiresAt?.Date)
                {
                    throw new InvalidOperationException("An existing batch has a different expiry date.");
                }
            }

            var receiptItem = new GoodsReceiptItemEntity
            {
                GoodsReceiptId = receipt.Id,
                PurchaseOrderItemId = line.PoItem?.Id,
                ProductId = line.Unit.ProductId,
                ProductUnitId = line.Unit.Id,
                Quantity = line.Quantity,
                ConversionFactor = line.Unit.ConversionFactor,
                QuantityBase = quantityBase,
                SourceUnitCost = line.UnitCost,
                LineSubtotal = line.Subtotal,
                LineDiscountAmount = line.Discount,
                AllocatedReceiptDiscount = allocatedDiscount,
                AllocatedExpense = allocatedExpense,
                LandedTotal = landedTotal,
                SourceUnitLandedCost = sourceUnitLanded,
                BaseUnitLandedCost = baseUnitLanded,
                BatchNumber = line.BatchNumber,
                ManufacturedAt = line.ManufacturedAt,
                ExpiresAt = line.ExpiresAt,
                ProductBatchId = batch?.Id,
            };
            context.GoodsReceiptItems.Add(receiptItem);
            await context.SaveChangesAsync(cancellationToken);

            var product = await context.Products.SingleAsync(x => x.Id == line.Unit.ProductId, cancellationToken);
            product.StockOnHand = Quantity(product.StockOnHand + quantityBase);
            product.PurchaseCost = Money(baseUnitLanded);
            if (batch is not null) batch.StockOnHand = Quantity(batch.StockOnHand + quantityBase);

            var movement = new StockMovementEntity
            {
                ProductId = product.Id,
                ProductBatchId = batch?.Id,
                SourceUnitId = line.Unit.UnitId,
                ActorUserId = user.UserId,
                MovementType = "purchase",
                SourceQuantity = line.Quantity,
                ConversionFactor = line.Unit.ConversionFactor,
                QuantityBase = quantityBase,
                BalanceAfter = product.StockOnHand,
                BatchBalanceAfter = batch?.StockOnHand,
                SourceUnitCost = sourceUnitLanded,
                UnitCostBase = baseUnitLanded,
                ReferenceType = "goods_receipt_item",
                ReferenceId = receiptItem.Id,
                IdempotencyKey = "grn:" + receiptItem.Id.ToString(CultureInfo.InvariantCulture),
                Notes = "Goods receipt " + receipt.Number,
                OccurredAt = receivedAt,
            };
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken);

            var layer = new InventoryCostLayerEntity
            {
                ProductId = product.Id,
                ProductBatchId = batch?.Id,
                SourceStockMovementId = movement.Id,
                InitialQuantityBase = quantityBase,
                RemainingQuantityBase = quantityBase,
                UnitCostBase = baseUnitLanded,
                ReceivedAt = receivedAt,
            };
            context.InventoryCostLayers.Add(layer);
            await context.SaveChangesAsync(cancellationToken);

            receiptItem.StockMovementId = movement.Id;
            receiptItem.InventoryCostLayerId = layer.Id;

            if (line.PoItem is not null)
                line.PoItem.ReceivedQuantity = Quantity(line.PoItem.ReceivedQuantity + line.Quantity);
        }

        if (order is not null)
        {
            order.Status = order.Items.All(x => x.ReceivedQuantity >= x.OrderedQuantity)
                ? "received"
                : "partially_received";
        }

        if (receipt.NetTotal > 0m)
        {
            await SupplierLedgerWriter.CreditAsync(
                context, supplier, receipt.NetTotal,
                "goods_receipt", "goods_receipt", receipt.Id, receipt.Number,
                user.UserId, "Posted goods receipt " + receipt.Number, receivedAt, cancellationToken);
        }

        if (paidAmount > 0m)
        {
            var payment = new PurchasePaymentEntity
            {
                GoodsReceiptId = receipt.Id,
                SupplierId = supplier.Id,
                RecordedByUserId = user.UserId,
                Amount = paidAmount,
                MethodCode = paymentMethod!,
                Reference = Clean(request.PaymentReference),
                PaidAt = receivedAt,
                Notes = Clean(request.PaymentNotes),
            };
            context.PurchasePayments.Add(payment);
            await context.SaveChangesAsync(cancellationToken);

            if (payment.MethodCode == "cash")
            {
                await CashLedgerEngine.RecordAsync(
                    context, user.UserId, payment.Amount,
                    "outflow", "purchase_payment", "purchase_payment", payment.Id,
                    receipt.Number, "Initial cash purchase payment", receivedAt,
                    "purchase-payment:" + payment.Id, null, cancellationToken);
            }

            await SupplierLedgerWriter.DebitAsync(
                context, supplier, paidAmount,
                "initial_purchase_payment", "purchase_payment", payment.Id, null,
                user.UserId, payment.Notes, receivedAt, cancellationToken);
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "purchasing.receipt.posted",
            CreatedAt = receipt.PostedAt,
            DetailsJson = "{\"number\":\"" + receipt.Number + "\",\"net_total\":\"" +
                          receipt.NetTotal.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await LoadReceiptDetailAsync(context, receipt.Id, cancellationToken))!;
    }

    public async Task<PurchaseReturnResult> PostPurchaseReturnAsync(
        PurchaseReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("purchases.return");
        ValidateUuid(request.IdempotencyKey);
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("A purchase return reason is required.");
        if (request.Items.Count == 0) throw new InvalidOperationException("A purchase return requires at least one item.");
        if (request.Items.Select(x => x.GoodsReceiptItemId).Distinct().Count() != request.Items.Count)
            throw new InvalidOperationException("Each returned goods receipt item must be unique.");

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.PurchaseReturns.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.GoodsReceiptId != request.GoodsReceiptId || existing.Reason != reason ||
                !RetryItemsMatch(existing.Items, request.Items))
                throw new InvalidOperationException("The purchase return idempotency key is already bound to another payload.");

            var existingReceipt = await context.GoodsReceipts.AsNoTracking()
                .SingleAsync(x => x.Id == existing.GoodsReceiptId, cancellationToken);
            var existingSupplier = await context.Suppliers.AsNoTracking()
                .SingleAsync(x => x.Id == existing.SupplierId, cancellationToken);
            return new PurchaseReturnResult(
                existing.Id, existing.Number, existing.GoodsReceiptId, existing.ReturnTotal,
                existingReceipt.BalanceDue, existingSupplier.CurrentBalance, existing.PostedAt);
        }

        var receipt = await context.GoodsReceipts.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == request.GoodsReceiptId, cancellationToken)
            ?? throw new InvalidOperationException("Goods receipt was not found.");
        var supplier = await context.Suppliers.SingleAsync(x => x.Id == receipt.SupplierId, cancellationToken);

        var ids = request.Items.Select(x => x.GoodsReceiptItemId).ToList();
        var receiptItems = receipt.Items.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        if (receiptItems.Count != ids.Count)
            throw new InvalidOperationException("The selected return item does not belong to this goods receipt.");

        var productUnitIds = receiptItems.Values.Select(x => x.ProductUnitId).Distinct().ToList();
        var precisions = await context.ProductUnits.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => productUnitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Unit.DecimalPlaces, cancellationToken);

        var prior = await context.PurchaseReturnItems.AsNoTracking()
            .Join(context.PurchaseReturns.AsNoTracking(),
                item => item.PurchaseReturnId, ret => ret.Id, (item, ret) => new { item, ret })
            .Where(x => x.ret.GoodsReceiptId == receipt.Id && ids.Contains(x.item.GoodsReceiptItemId))
            .Select(x => x.item)
            .ToListAsync(cancellationToken);
        var priorByItem = prior.GroupBy(x => x.GoodsReceiptItemId)
            .ToDictionary(x => x.Key, x => new PriorPurchaseReturn(
                Quantity(x.Sum(v => v.Quantity)),
                Money(x.Sum(v => v.ReturnAmount))));

        var prepared = new List<PreparedPurchaseReturn>();
        foreach (var input in request.Items)
        {
            var item = receiptItems[input.GoodsReceiptItemId];
            if (input.Quantity <= 0m) throw new InvalidOperationException("Purchase return quantity must be greater than zero.");
            EnsurePrecision(input.Quantity, precisions[item.ProductUnitId], "Purchase return quantity");

            var already = priorByItem.GetValueOrDefault(item.Id) ?? new PriorPurchaseReturn(0m, 0m);
            var remainingQuantity = Quantity(item.Quantity - already.Quantity);
            if (input.Quantity > remainingQuantity)
                throw new InvalidOperationException("Purchase return quantity exceeds the remaining returnable receipt quantity.");

            if (item.InventoryCostLayerId is null)
                throw new InvalidOperationException("The original receipt cost layer is unavailable.");
            var layer = await context.InventoryCostLayers.SingleAsync(
                x => x.Id == item.InventoryCostLayerId.Value, cancellationToken);
            var quantityBase = Quantity(input.Quantity * item.ConversionFactor);
            if (quantityBase > layer.RemainingQuantityBase)
                throw new InvalidOperationException("Purchase return quantity has already been consumed by sales or other stock use.");

            var product = await context.Products.SingleAsync(x => x.Id == item.ProductId, cancellationToken);
            if (quantityBase > product.StockOnHand)
                throw new InvalidOperationException("Insufficient on-hand stock to return this purchase.");

            ProductBatchEntity? batch = null;
            if (item.ProductBatchId is not null)
            {
                batch = await context.ProductBatches.SingleAsync(x => x.Id == item.ProductBatchId.Value, cancellationToken);
                if (quantityBase > batch.StockOnHand)
                    throw new InvalidOperationException("Insufficient stock remains in the original receipt batch.");
            }

            var remainingAmount = Money(item.LandedTotal - already.Amount);
            var returnAmount = input.Quantity == remainingQuantity
                ? remainingAmount
                : Money(input.Quantity * (item.LandedTotal / item.Quantity));
            if (returnAmount > remainingAmount) returnAmount = remainingAmount;

            prepared.Add(new PreparedPurchaseReturn(
                item, layer, product, batch, Quantity(input.Quantity),
                quantityBase, returnAmount));
        }

        var returnTotal = Money(prepared.Sum(x => x.ReturnAmount));
        var now = DateTimeOffset.UtcNow;
        await BusinessDayGuard.EnsureOpenAsync(context, now, cancellationToken);
        var purchaseReturn = new PurchaseReturnEntity
        {
            Number = await NextNumberAsync(context, "purchase_return", "PRT", now, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            GoodsReceiptId = receipt.Id,
            SupplierId = supplier.Id,
            CreatedByUserId = user.UserId,
            Reason = reason,
            ReturnTotal = returnTotal,
            PostedAt = now,
        };
        context.PurchaseReturns.Add(purchaseReturn);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var line in prepared)
        {
            line.Layer.RemainingQuantityBase = Quantity(line.Layer.RemainingQuantityBase - line.QuantityBase);
            line.Product.StockOnHand = Quantity(line.Product.StockOnHand - line.QuantityBase);
            if (line.Batch is not null)
                line.Batch.StockOnHand = Quantity(line.Batch.StockOnHand - line.QuantityBase);

            var movement = new StockMovementEntity
            {
                ProductId = line.Product.Id,
                ProductBatchId = line.Batch?.Id,
                SourceUnitId = null,
                ActorUserId = user.UserId,
                MovementType = "purchase_return",
                QuantityBase = -line.QuantityBase,
                BalanceAfter = line.Product.StockOnHand,
                BatchBalanceAfter = line.Batch?.StockOnHand,
                UnitCostBase = line.Layer.UnitCostBase,
                ReferenceType = "purchase_return",
                ReferenceId = purchaseReturn.Id,
                IdempotencyKey = "prt:" + purchaseReturn.Id + ":item:" + line.Item.Id,
                Notes = reason,
                OccurredAt = now,
            };
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken);

            context.PurchaseReturnItems.Add(new PurchaseReturnItemEntity
            {
                PurchaseReturnId = purchaseReturn.Id,
                GoodsReceiptItemId = line.Item.Id,
                InventoryCostLayerId = line.Layer.Id,
                StockMovementId = movement.Id,
                Quantity = line.Quantity,
                QuantityBase = line.QuantityBase,
                ReturnAmount = line.ReturnAmount,
                UnitCostBase = line.Layer.UnitCostBase,
                CostAmount = Cost(line.QuantityBase * line.Layer.UnitCostBase),
            });
        }

        if (returnTotal > 0m)
        {
            await SupplierLedgerWriter.DebitAsync(
                context, supplier, returnTotal,
                "purchase_return", "purchase_return", purchaseReturn.Id, purchaseReturn.Number,
                user.UserId, reason, now, cancellationToken);
        }

        receipt.ReturnedTotal = Money(receipt.ReturnedTotal + returnTotal);
        receipt.BalanceDue = Money(Math.Max(0m, receipt.NetTotal - receipt.PaidAmount - receipt.ReturnedTotal));

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "purchasing.purchase_return.posted",
            CreatedAt = now,
            DetailsJson = "{\"number\":\"" + purchaseReturn.Number + "\",\"return_total\":\"" +
                          returnTotal.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PurchaseReturnResult(
            purchaseReturn.Id, purchaseReturn.Number, receipt.Id, returnTotal,
            receipt.BalanceDue, supplier.CurrentBalance, now);
    }

    public async Task<SupplierPaymentResult> RecordSupplierPaymentAsync(
        SupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("suppliers.pay");
        ValidateUuid(request.IdempotencyKey);
        var amount = Money(request.Amount);
        if (amount <= 0m) throw new InvalidOperationException("Supplier payment amount must be greater than zero.");
        var method = request.MethodCode.Trim().ToLowerInvariant();
        ValidatePurchasePaymentMethod(method);

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.SupplierPayments.Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.SupplierId != request.SupplierId || existing.Amount != amount ||
                !string.Equals(existing.MethodCode, method, StringComparison.OrdinalIgnoreCase) ||
                existing.Reference != Clean(request.Reference))
                throw new InvalidOperationException("The supplier payment idempotency key is already bound to another payload.");

            var existingSupplier = await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == existing.SupplierId, cancellationToken);
            return await MapSupplierPaymentAsync(context, existing, existingSupplier.CurrentBalance, cancellationToken);
        }

        var supplier = await context.Suppliers.SingleOrDefaultAsync(x => x.Id == request.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier was not found.");
        if (!supplier.IsActive) throw new InvalidOperationException("Payments cannot be recorded for an inactive supplier.");
        if (supplier.CurrentBalance <= 0m) throw new InvalidOperationException("This supplier has no positive payable balance.");
        if (amount > supplier.CurrentBalance) throw new InvalidOperationException("Supplier payment cannot exceed the current payable balance.");
        if (method == "cash") await DemandOpenCashShiftAsync(context, user.UserId, cancellationToken);

        var paidAt = request.PaidAt ?? DateTimeOffset.UtcNow;
        await BusinessDayGuard.EnsureOpenAsync(context, paidAt, cancellationToken);
        var payment = new SupplierPaymentEntity
        {
            Number = await NextNumberAsync(context, "supplier_payment", "SPY", paidAt, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            SupplierId = supplier.Id,
            RecordedByUserId = user.UserId,
            Amount = amount,
            MethodCode = method,
            Reference = Clean(request.Reference),
            PaidAt = paidAt,
            Notes = Clean(request.Notes),
        };
        context.SupplierPayments.Add(payment);
        await context.SaveChangesAsync(cancellationToken);

        var remaining = amount;
        var receipts = await context.GoodsReceipts
            .Where(x => x.SupplierId == supplier.Id && x.BalanceDue > 0m)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var receipt in receipts)
        {
            if (remaining <= 0m) break;
            var take = Money(Math.Min(receipt.BalanceDue, remaining));
            if (take <= 0m) continue;

            payment.Allocations.Add(new SupplierPaymentAllocationEntity
            {
                GoodsReceiptId = receipt.Id,
                Amount = take,
            });
            receipt.PaidAmount = Money(receipt.PaidAmount + take);
            receipt.BalanceDue = Money(receipt.BalanceDue - take);
            remaining = Money(remaining - take);
        }

        if (method == "cash")
        {
            await CashLedgerEngine.RecordAsync(
                context, user.UserId, payment.Amount,
                "outflow", "supplier_payment", "supplier_payment", payment.Id,
                payment.Number, "Cash supplier payment", paidAt,
                "supplier-payment:" + payment.Id, null, cancellationToken);
        }

        await SupplierLedgerWriter.DebitAsync(
            context, supplier, amount,
            "supplier_payment", "supplier_payment", payment.Id, payment.Number,
            user.UserId, payment.Notes, paidAt, cancellationToken);

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "purchasing.supplier.payment_recorded",
            CreatedAt = paidAt,
            DetailsJson = "{\"number\":\"" + payment.Number + "\",\"amount\":\"" +
                          amount.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapSupplierPaymentAsync(context, payment, supplier.CurrentBalance, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchasePaymentMethodOption>> GetPaymentMethodsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorizer.HasPermission("purchases.record_payment") &&
            !authorizer.HasPermission("suppliers.pay") &&
            !authorizer.HasPermission("purchases.view"))
            throw new InvalidOperationException("The user is not allowed to view purchase payment methods.");

        var locale = sessions.Current?.PreferredLocale ?? "en";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var methods = await context.PaymentMethods.AsNoTracking()
            .Where(x => x.IsActive && (x.Code == "cash" || x.Code == "bank" || x.Code == "other"))
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        return methods.Select(x => new PurchasePaymentMethodOption(
            x.Code, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.IsCash)).ToList();
    }

    private async Task<PurchaseOrderDetail?> LoadOrderDetailAsync(
        PosDbContext context,
        long orderId,
        CancellationToken cancellationToken)
    {
        var order = await context.PurchaseOrders.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return null;

        var supplier = await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == order.SupplierId, cancellationToken);
        var lines = await MapOrderLinesAsync(context, order.Items, cancellationToken);
        return new PurchaseOrderDetail(
            MapOrderSummary(order, supplier.Name),
            order.SupplierReference, order.Subtotal, order.LineDiscountTotal,
            order.OrderDiscountAmount, order.Notes, lines);
    }

    private async Task<IReadOnlyList<PurchaseOrderLine>> MapOrderLinesAsync(
        PosDbContext context,
        IEnumerable<PurchaseOrderItemEntity> items,
        CancellationToken cancellationToken)
    {
        var locale = sessions.Current?.PreferredLocale ?? "en";
        var list = items.OrderBy(x => x.Id).ToList();
        var unitIds = list.Select(x => x.ProductUnitId).Distinct().ToList();
        var units = await context.ProductUnits.AsNoTracking()
            .Include(x => x.Product).Include(x => x.Unit)
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return list.Select(x =>
        {
            var u = units[x.ProductUnitId];
            return new PurchaseOrderLine(
                x.Id, x.ProductUnitId, u.Product.Sku,
                Localize(u.Product.NameEn, u.Product.NameFa, u.Product.NamePs, locale),
                Localize(u.Unit.NameEn, u.Unit.NameFa, u.Unit.NamePs, locale),
                u.Unit.DecimalPlaces, x.OrderedQuantity, x.ReceivedQuantity,
                Quantity(Math.Max(0m, x.OrderedQuantity - x.ReceivedQuantity)),
                x.UnitCost, x.LineDiscountAmount, x.LineNetTotal);
        }).ToList();
    }

    private async Task<GoodsReceiptDetail?> LoadReceiptDetailAsync(
        PosDbContext context,
        long receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await context.GoodsReceipts.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken);
        if (receipt is null) return null;

        var supplier = await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == receipt.SupplierId, cancellationToken);
        var itemIds = receipt.Items.Select(x => x.Id).ToList();
        var prior = await context.PurchaseReturnItems.AsNoTracking()
            .Where(x => itemIds.Contains(x.GoodsReceiptItemId))
            .GroupBy(x => x.GoodsReceiptItemId)
            .Select(x => new
            {
                GoodsReceiptItemId = x.Key,
                Quantity = x.Sum(v => v.Quantity),
                Amount = x.Sum(v => v.ReturnAmount),
            })
            .ToDictionaryAsync(x => x.GoodsReceiptItemId, cancellationToken);

        var unitIds = receipt.Items.Select(x => x.ProductUnitId).Distinct().ToList();
        var units = await context.ProductUnits.AsNoTracking()
            .Include(x => x.Product).Include(x => x.Unit)
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var locale = sessions.Current?.PreferredLocale ?? "en";

        var lines = receipt.Items.OrderBy(x => x.Id).Select(x =>
        {
            var u = units[x.ProductUnitId];
            var returned = prior.GetValueOrDefault(x.Id);
            var returnedQty = returned?.Quantity ?? 0m;
            var returnedAmount = returned?.Amount ?? 0m;
            return new GoodsReceiptLine(
                x.Id, x.ProductUnitId, u.Product.Sku,
                Localize(u.Product.NameEn, u.Product.NameFa, u.Product.NamePs, locale),
                Localize(u.Unit.NameEn, u.Unit.NameFa, u.Unit.NamePs, locale),
                u.Unit.DecimalPlaces, x.Quantity, x.QuantityBase, x.SourceUnitCost,
                x.LineDiscountAmount, x.AllocatedReceiptDiscount, x.AllocatedExpense,
                x.LandedTotal, x.SourceUnitLandedCost, x.BaseUnitLandedCost,
                x.BatchNumber, x.ManufacturedAt, x.ExpiresAt, returnedQty,
                Quantity(Math.Max(0m, x.Quantity - returnedQty)),
                Money(Math.Max(0m, x.LandedTotal - returnedAmount)));
        }).ToList();

        return new GoodsReceiptDetail(
            MapReceiptSummary(receipt, supplier.Name),
            receipt.SupplierInvoiceReference, receipt.Subtotal, receipt.LineDiscountTotal,
            receipt.ReceiptDiscountAmount, receipt.ExpenseTotal, receipt.Notes,
            lines,
            receipt.Expenses.OrderBy(x => x.Id)
                .Select(x => new GoodsReceiptExpenseRow(x.Type, x.Description, x.Amount)).ToList());
    }

    private static async Task<Dictionary<long, string>> SupplierNamesAsync(
        PosDbContext context,
        IEnumerable<long> supplierIds,
        CancellationToken cancellationToken)
    {
        var ids = supplierIds.Distinct().ToList();
        return await context.Suppliers.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    private async Task<SupplierPaymentResult> MapSupplierPaymentAsync(
        PosDbContext context,
        SupplierPaymentEntity payment,
        decimal balanceAfter,
        CancellationToken cancellationToken)
    {
        if (!context.Entry(payment).Collection(x => x.Allocations).IsLoaded)
            await context.Entry(payment).Collection(x => x.Allocations).LoadAsync(cancellationToken);

        var ids = payment.Allocations.Select(x => x.GoodsReceiptId).ToList();
        var receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return new SupplierPaymentResult(
            payment.Id, payment.Number, payment.SupplierId, payment.Amount,
            payment.MethodCode, balanceAfter, payment.PaidAt,
            payment.Allocations.OrderBy(x => x.Id)
                .Select(x => new SupplierPaymentAllocation(
                    x.GoodsReceiptId,
                    receipts.GetValueOrDefault(x.GoodsReceiptId)?.Number ?? string.Empty,
                    x.Amount)).ToList());
    }

    private static SupplierSummary MapSupplier(SupplierEntity x) =>
        new(x.Id, x.Name, x.ContactPerson, x.Phone, x.AlternatePhone, x.Address,
            x.OpeningBalance, x.CurrentBalance, x.Notes, x.IsActive);

    private static PurchaseOrderSummary MapOrderSummary(PurchaseOrderEntity x, string supplierName) =>
        new(x.Id, x.Number, x.SupplierId, supplierName, x.Status,
            x.OrderDate, x.ExpectedDate, x.NetTotal, x.CreatedAt);

    private static GoodsReceiptSummary MapReceiptSummary(GoodsReceiptEntity x, string supplierName) =>
        new(x.Id, x.Number, x.SupplierId, supplierName, x.PurchaseOrderId,
            x.Status, x.ReceivedAt, x.NetTotal, x.PaidAmount, x.ReturnedTotal, x.BalanceDue);

    private void DemandSupplierView()
    {
        if (!authorizer.HasPermission("suppliers.view") &&
            !authorizer.HasPermission("suppliers.manage") &&
            !authorizer.HasPermission("suppliers.pay") &&
            !authorizer.HasPermission("purchases.view"))
            throw new InvalidOperationException("The user is not allowed to view suppliers.");
    }

    private UserSessionSnapshot RequireUser() =>
        sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

    private static void ValidatePurchasePaymentMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method) || !PurchasePaymentMethods.Contains(method))
            throw new InvalidOperationException("A valid purchase payment method is required.");
    }

    private static async Task DemandOpenCashShiftAsync(
        PosDbContext context,
        long userId,
        CancellationToken cancellationToken)
    {
        var open = await context.CashierShifts.AnyAsync(
            x => x.UserId == userId && x.Status == "open", cancellationToken);
        if (!open) throw new InvalidOperationException("Open a cashier shift before recording a cash purchase payment.");
    }

    private static List<decimal> Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        var result = Enumerable.Repeat(0m, weights.Count).ToList();
        total = Money(total);
        if (total == 0m || weights.Count == 0) return result;

        var positiveTotal = weights.Where(x => x > 0m).Sum();
        if (positiveTotal <= 0m) return result;

        var remaining = total;
        var positiveIndices = Enumerable.Range(0, weights.Count).Where(i => weights[i] > 0m).ToList();
        for (var p = 0; p < positiveIndices.Count; p++)
        {
            var i = positiveIndices[p];
            var allocation = p == positiveIndices.Count - 1
                ? remaining
                : Money(total * weights[i] / positiveTotal);
            allocation = Math.Min(allocation, remaining);
            result[i] = allocation;
            remaining = Money(remaining - allocation);
        }

        return result;
    }

    private static bool RetryItemsMatch(
        IReadOnlyCollection<PurchaseReturnItemEntity> recorded,
        IReadOnlyList<PurchaseReturnLineRequest> requested)
    {
        var a = recorded.OrderBy(x => x.GoodsReceiptItemId)
            .Select(x => (x.GoodsReceiptItemId, Quantity(x.Quantity))).ToList();
        var b = requested.OrderBy(x => x.GoodsReceiptItemId)
            .Select(x => (x.GoodsReceiptItemId, Quantity(x.Quantity))).ToList();
        return a.SequenceEqual(b);
    }

    private static void EnsurePrecision(decimal value, int decimalPlaces, string label)
    {
        if (decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero) != value)
            throw new InvalidOperationException(label + " exceeds the selected unit precision.");
    }

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

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa!,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps!,
            _ => en,
        };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Cost(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private sealed record PreparedOrderLine(
        ProductUnitEntity Unit, decimal Quantity, decimal UnitCost, decimal Subtotal,
        decimal Discount, decimal Net, string? Notes);

    private sealed record PreparedReceiptLine(
        PurchaseOrderItemEntity? PoItem, ProductUnitEntity Unit, decimal Quantity,
        decimal UnitCost, decimal Subtotal, decimal Discount, decimal Basis,
        string? BatchNumber, DateTime? ManufacturedAt, DateTime? ExpiresAt);

    private sealed record PriorPurchaseReturn(decimal Quantity, decimal Amount);

    private sealed record PreparedPurchaseReturn(
        GoodsReceiptItemEntity Item, InventoryCostLayerEntity Layer, ProductEntity Product,
        ProductBatchEntity? Batch, decimal Quantity, decimal QuantityBase, decimal ReturnAmount);
}
