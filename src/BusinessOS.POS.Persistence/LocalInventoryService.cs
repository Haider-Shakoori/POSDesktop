using System.Globalization;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalInventoryService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IInventoryService
{
    public async Task<InventoryReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var products = await context.Products.AsNoTracking()
            .Include(x => x.BaseUnit)
            .Include(x => x.Units).ThenInclude(x => x.Unit)
            .Where(x => x.IsActive && x.TrackStock)
            .OrderBy(x => x.NameEn)
            .ToListAsync(cancellationToken);

        var productIds = products.Select(x => x.Id).ToList();
        var batches = await context.ProductBatches.AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var batchLookup = batches.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.ToList());

        var options = products.Select(product =>
        {
            var productBatches = batchLookup.GetValueOrDefault(product.Id, []);
            return new InventoryProductOption(
                product.Id,
                product.Sku,
                Localize(product.NameEn, product.NameFa, product.NamePs, locale),
                product.StockOnHand,
                product.MinimumStock,
                product.ReorderQuantity,
                product.TrackExpiry,
                product.Units.OrderBy(x => x.Id).Select(x => new InventoryUnitOption(
                    x.Id,
                    x.UnitId,
                    Localize(x.Unit.NameEn, x.Unit.NameFa, x.Unit.NamePs, locale),
                    x.Unit.Code,
                    x.Unit.DecimalPlaces,
                    x.ConversionFactor,
                    x.CanPurchase)).ToList(),
                productBatches.Select(MapBatch).ToList());
        }).ToList();

        var targets = new List<InventoryTargetOption>();
        foreach (var product in options)
        {
            if (!product.TrackExpiry)
            {
                targets.Add(new InventoryTargetOption(
                    product.ProductId + ":",
                    product.ProductId,
                    null,
                    product.Sku + " · " + product.Name,
                    product.StockOnHand,
                    false,
                    false));
                continue;
            }

            foreach (var batch in product.Batches)
            {
                targets.Add(new InventoryTargetOption(
                    product.ProductId + ":" + batch.Id,
                    product.ProductId,
                    batch.Id,
                    product.Sku + " · " + product.Name + " · Batch " + batch.BatchNumber +
                    (batch.ExpiresAt is null ? string.Empty : " · Exp " + batch.ExpiresAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    batch.StockOnHand,
                    batch.IsExpired,
                    batch.IsBlocked));
            }
        }

        return new InventoryReferenceData(options, targets);
    }

    public async Task<IReadOnlyList<InventoryStockRow>> GetStockAsync(
        string? search = null,
        bool lowStockOnly = false,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();
        search = search?.Trim();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products.AsNoTracking()
            .Include(x => x.BaseUnit)
            .Where(x => x.TrackStock);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Sku, like) ||
                EF.Functions.Like(x.NameEn, like) ||
                (x.NameFa != null && EF.Functions.Like(x.NameFa, like)) ||
                (x.NamePs != null && EF.Functions.Like(x.NamePs, like)));
        }

        if (lowStockOnly)
        {
            query = query.Where(x => x.StockOnHand <= x.MinimumStock);
        }

        var products = await query.OrderBy(x => x.NameEn).Take(1500).ToListAsync(cancellationToken);
        var ids = products.Select(x => x.Id).ToList();
        var batches = await context.ProductBatches.AsNoTracking()
            .Where(x => ids.Contains(x.ProductId))
            .ToListAsync(cancellationToken);
        var byProduct = batches.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.ToList());
        var today = DateTime.Today;

        return products.Select(x =>
        {
            var productBatches = byProduct.GetValueOrDefault(x.Id, []);
            var expired = productBatches.Where(b => b.ExpiresAt is not null && b.ExpiresAt.Value.Date < today).ToList();
            return new InventoryStockRow(
                x.Id,
                x.Sku,
                Localize(x.NameEn, x.NameFa, x.NamePs, locale),
                Localize(x.BaseUnit.NameEn, x.BaseUnit.NameFa, x.BaseUnit.NamePs, locale),
                x.StockOnHand,
                x.MinimumStock,
                x.ReorderQuantity,
                x.StockOnHand <= x.MinimumStock,
                x.TrackExpiry,
                productBatches.Count,
                expired.Count,
                Quantity(expired.Sum(b => b.StockOnHand)));
        }).ToList();
    }

    public async Task<IReadOnlyList<InventoryMovementRow>> GetMovementsAsync(
        long? productId = null,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();
        take = Math.Clamp(take, 1, 1000);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.StockMovements.AsNoTracking().AsQueryable();
        if (productId is not null)
        {
            query = query.Where(x => x.ProductId == productId);
        }

        var movements = await query.OrderByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
        var productIds = movements.Select(x => x.ProductId).Distinct().ToList();
        var batchIds = movements.Where(x => x.ProductBatchId != null).Select(x => x.ProductBatchId!.Value).Distinct().ToList();
        var unitIds = movements.Where(x => x.SourceUnitId != null).Select(x => x.SourceUnitId!.Value).Distinct().ToList();

        var products = await context.Products.AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var batches = await context.ProductBatches.AsNoTracking()
            .Where(x => batchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var units = await context.Units.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return movements.Select(x =>
        {
            var product = products[x.ProductId];
            var batch = x.ProductBatchId is null ? null : batches.GetValueOrDefault(x.ProductBatchId.Value);
            var unit = x.SourceUnitId is null ? null : units.GetValueOrDefault(x.SourceUnitId.Value);
            return new InventoryMovementRow(
                x.Id,
                x.ProductId,
                Localize(product.NameEn, product.NameFa, product.NamePs, locale),
                product.Sku,
                x.MovementType,
                x.QuantityBase,
                x.BalanceAfter,
                batch?.BatchNumber,
                x.BatchBalanceAfter,
                unit is null ? null : Localize(unit.NameEn, unit.NameFa, unit.NamePs, locale),
                x.SourceQuantity,
                x.SourceUnitCost,
                x.UnitCostBase,
                x.ReferenceType,
                x.ReferenceId,
                x.Notes,
                x.OccurredAt);
        }).ToList();
    }

    public async Task<InventoryMovementRow> RecordOpeningStockAsync(
        OpeningStockRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.opening_stock");
        ValidateUuid(request.IdempotencyKey);
        if (request.Quantity <= 0m) throw new InvalidOperationException("Opening stock quantity must be greater than zero.");
        if (request.UnitCost < 0m) throw new InvalidOperationException("Opening stock unit cost cannot be negative.");

        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProductId != request.ProductId || existing.MovementType != "opening_stock")
                throw new InvalidOperationException("The idempotency key belongs to another stock movement.");
            await transaction.CommitAsync(cancellationToken);
            return await MapMovementAsync(context, existing, locale, cancellationToken);
        }

        var product = await context.Products
            .Include(x => x.Units).ThenInclude(x => x.Unit)
            .SingleOrDefaultAsync(x => x.Id == request.ProductId, cancellationToken)
            ?? throw new InvalidOperationException("Product was not found.");

        if (!product.TrackStock) throw new InvalidOperationException("Opening stock cannot be recorded for a product that does not track stock.");

        var sourceUnit = product.Units.SingleOrDefault(x => x.UnitId == request.UnitId)
            ?? throw new InvalidOperationException("The selected unit is not configured for this product.");
        ValidatePrecision(request.Quantity, sourceUnit.Unit.DecimalPlaces, "Quantity");

        var baseQuantity = Quantity(request.Quantity * sourceUnit.ConversionFactor);
        var batch = await ResolveBatchAsync(context, product, request.BatchNumber, request.ManufacturedAt, request.ExpiresAt, request.Notes, cancellationToken);
        var sourceCost = request.UnitCost is null ? null : Cost(request.UnitCost.Value);
        var baseCost = sourceCost is null ? product.PurchaseCost : Cost(sourceCost.Value / sourceUnit.ConversionFactor);

        var movement = RecordMovement(
            context, product, batch, "opening_stock", baseQuantity,
            sourceUnit.UnitId, request.Quantity, sourceUnit.ConversionFactor,
            sourceCost, baseCost, request.IdempotencyKey, request.Notes);

        await context.SaveChangesAsync(cancellationToken);
        context.InventoryCostLayers.Add(new InventoryCostLayerEntity
        {
            ProductId = product.Id,
            ProductBatchId = batch?.Id,
            SourceStockMovementId = movement.Id,
            InitialQuantityBase = baseQuantity,
            RemainingQuantityBase = baseQuantity,
            UnitCostBase = baseCost,
            ReceivedAt = movement.OccurredAt,
        });
        AddAudit(context, "inventory.opening_stock.recorded", product.Sku);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapMovementAsync(context, movement, locale, cancellationToken);
    }

    public async Task<InventoryMovementRow> AdjustStockAsync(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.adjust");
        ValidateUuid(request.IdempotencyKey);
        if (request.QuantityBase <= 0m) throw new InvalidOperationException("Adjustment quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("An adjustment reason is required.");

        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return await MapMovementAsync(context, existing, locale, cancellationToken);
        }

        var target = await ResolveTargetAsync(context, request.TargetKey, cancellationToken);
        var quantity = Quantity(request.QuantityBase);
        var movementType = request.IsIncrease ? "adjustment_in" : "adjustment_out";
        var signed = request.IsIncrease ? quantity : -quantity;

        decimal? unitCost = target.Product.PurchaseCost;
        decimal costAmount = 0m;
        if (!request.IsIncrease)
        {
            EnsureAvailable(target.Product, target.Batch, quantity);
            costAmount = await ConsumeCostAsync(context, target.Product, target.Batch, quantity, cancellationToken);
            unitCost = quantity == 0m ? 0m : Cost(costAmount / quantity);
        }

        var movement = RecordMovement(
            context, target.Product, target.Batch, movementType, signed,
            null, null, null, null, unitCost, request.IdempotencyKey,
            request.Reason.Trim() + (string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : " · " + request.Notes.Trim()));

        await context.SaveChangesAsync(cancellationToken);

        if (request.IsIncrease)
        {
            context.InventoryCostLayers.Add(new InventoryCostLayerEntity
            {
                ProductId = target.Product.Id,
                ProductBatchId = target.Batch?.Id,
                SourceStockMovementId = movement.Id,
                InitialQuantityBase = quantity,
                RemainingQuantityBase = quantity,
                UnitCostBase = target.Product.PurchaseCost,
                ReceivedAt = movement.OccurredAt,
            });
        }

        AddAudit(context, "inventory.stock.adjusted", target.Product.Sku);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapMovementAsync(context, movement, locale, cancellationToken);
    }

    public async Task<StockCountDetail> CreateStockCountAsync(
        CreateStockCountRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.count");
        ValidateUuid(request.IdempotencyKey);
        if (request.Items.Count is < 1 or > 100) throw new InvalidOperationException("A stock count requires between 1 and 100 items.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.StockCounts.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return await MapStockCountAsync(context, existing, cancellationToken);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = new StockCountEntity
        {
            Number = await NextNumberAsync(context, "stock_count", "SCN", cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            CountedByUserId = sessions.Current!.UserId,
            Status = "draft",
            CountedAt = DateTimeOffset.UtcNow,
            Notes = Clean(request.Notes),
        };
        context.StockCounts.Add(count);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var line in request.Items)
        {
            if (!seen.Add(line.TargetKey)) throw new InvalidOperationException("A stock count cannot contain the same product/batch twice.");
            if (line.PhysicalQuantityBase < 0m) throw new InvalidOperationException("Physical stock count cannot be negative.");

            var target = await ResolveTargetAsync(context, line.TargetKey, cancellationToken);
            var expected = target.Batch?.StockOnHand ?? target.Product.StockOnHand;
            count.Items.Add(new StockCountItemEntity
            {
                ProductId = target.Product.Id,
                ProductBatchId = target.Batch?.Id,
                ExpectedQuantityBase = Quantity(expected),
                PhysicalQuantityBase = Quantity(line.PhysicalQuantityBase),
                VarianceQuantityBase = Quantity(line.PhysicalQuantityBase - expected),
                CostAmount = 0m,
            });
        }

        AddAudit(context, "inventory.stock_count.created", count.Number);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapStockCountAsync(context, count, cancellationToken);
    }

    public async Task<StockCountDetail> ApproveStockCountAsync(
        long stockCountId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.count.approve");
        ValidateUuid(idempotencyKey);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var count = await context.StockCounts.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == stockCountId, cancellationToken)
            ?? throw new InvalidOperationException("Stock count was not found.");

        if (count.Status == "approved")
        {
            if (count.ApprovalIdempotencyKey != idempotencyKey)
                throw new InvalidOperationException("This stock count has already been approved.");
            await transaction.CommitAsync(cancellationToken);
            return await MapStockCountAsync(context, count, cancellationToken);
        }

        if (count.Status != "draft") throw new InvalidOperationException("Only draft stock counts can be approved.");

        foreach (var item in count.Items.OrderBy(x => x.Id))
        {
            var product = await context.Products.SingleAsync(x => x.Id == item.ProductId, cancellationToken);
            ProductBatchEntity? batch = item.ProductBatchId is null
                ? null
                : await context.ProductBatches.SingleAsync(x => x.Id == item.ProductBatchId, cancellationToken);

            var current = batch?.StockOnHand ?? product.StockOnHand;
            if (Quantity(current) != Quantity(item.ExpectedQuantityBase))
                throw new InvalidOperationException("Stock changed after count " + count.Number + " was captured. Recount the affected item before approval.");

            var variance = Quantity(item.PhysicalQuantityBase - item.ExpectedQuantityBase);
            if (variance == 0m) continue;

            decimal costAmount;
            StockMovementEntity movement;
            if (variance > 0m)
            {
                movement = RecordMovement(
                    context, product, batch, "adjustment_in", variance,
                    null, null, null, null, product.PurchaseCost,
                    "stock-count:" + count.Id + ":item:" + item.Id,
                    "Stock count " + count.Number + " positive variance",
                    "stock_count_item", item.Id);
                await context.SaveChangesAsync(cancellationToken);
                context.InventoryCostLayers.Add(new InventoryCostLayerEntity
                {
                    ProductId = product.Id,
                    ProductBatchId = batch?.Id,
                    SourceStockMovementId = movement.Id,
                    InitialQuantityBase = variance,
                    RemainingQuantityBase = variance,
                    UnitCostBase = product.PurchaseCost,
                    ReceivedAt = movement.OccurredAt,
                });
                costAmount = Money(variance * product.PurchaseCost);
            }
            else
            {
                var loss = -variance;
                EnsureAvailable(product, batch, loss);
                costAmount = await ConsumeCostAsync(context, product, batch, loss, cancellationToken);
                movement = RecordMovement(
                    context, product, batch, "adjustment_out", -loss,
                    null, null, null, null, Cost(costAmount / loss),
                    "stock-count:" + count.Id + ":item:" + item.Id,
                    "Stock count " + count.Number + " negative variance",
                    "stock_count_item", item.Id);
            }

            await context.SaveChangesAsync(cancellationToken);
            item.StockMovementId = movement.Id;
            item.CostAmount = costAmount;
        }

        count.ApprovalIdempotencyKey = idempotencyKey;
        count.ApprovedByUserId = sessions.Current!.UserId;
        count.ApprovedAt = DateTimeOffset.UtcNow;
        count.Status = "approved";
        AddAudit(context, "inventory.stock_count.approved", count.Number);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MapStockCountAsync(context, count, cancellationToken);
    }

    public async Task<IReadOnlyList<StockCountSummary>> GetStockCountsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var counts = await context.StockCounts.AsNoTracking().Include(x => x.Items)
            .OrderByDescending(x => x.Id).Take(200).ToListAsync(cancellationToken);
        return counts.Select(x => new StockCountSummary(
            x.Id, x.Number, x.Status, x.Items.Count,
            Quantity(x.Items.Sum(i => Math.Abs(i.VarianceQuantityBase))),
            x.CountedAt, x.ApprovedAt)).ToList();
    }

    public async Task<InventoryWriteoffResult> PostWriteoffAsync(
        InventoryWriteoffRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.writeoff");
        ValidateUuid(request.IdempotencyKey);
        var type = request.WriteoffType.Trim().ToLowerInvariant();
        if (type is not ("damage" or "expiry")) throw new InvalidOperationException("Inventory write-off type must be damage or expiry.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("A reason is required for inventory write-off.");
        if (request.Items.Count is < 1 or > 100) throw new InvalidOperationException("An inventory write-off requires between 1 and 100 items.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.InventoryWriteoffs.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return MapWriteoff(existing);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var writeoff = new InventoryWriteoffEntity
        {
            Number = await NextNumberAsync(context, "inventory_writeoff_" + type, type == "damage" ? "DMG" : "EXP", cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            WriteoffType = type,
            PostedByUserId = sessions.Current!.UserId,
            Reason = request.Reason.Trim(),
            PostedAt = DateTimeOffset.UtcNow,
            Notes = Clean(request.Notes),
        };
        context.InventoryWriteoffs.Add(writeoff);
        await context.SaveChangesAsync(cancellationToken);

        decimal totalCost = 0m;
        decimal totalQuantity = 0m;
        foreach (var line in request.Items)
        {
            if (!seen.Add(line.TargetKey)) throw new InvalidOperationException("An inventory write-off cannot contain the same product/batch twice.");
            if (line.QuantityBase <= 0m) throw new InvalidOperationException("Inventory write-off quantity must be greater than zero.");

            var target = await ResolveTargetAsync(context, line.TargetKey, cancellationToken);
            var quantity = Quantity(line.QuantityBase);

            if (type == "expiry")
            {
                if (!target.Product.TrackExpiry || target.Batch is null)
                    throw new InvalidOperationException("Expiry write-off is only valid for expiry-tracked batches.");
                if (target.Batch.ExpiresAt is null || target.Batch.ExpiresAt.Value.Date >= DateTime.Today)
                    throw new InvalidOperationException("Expiry write-off requires a batch that is already expired.");
            }

            EnsureAvailable(target.Product, target.Batch, quantity);
            var cost = await ConsumeCostAsync(context, target.Product, target.Batch, quantity, cancellationToken);
            var movement = RecordMovement(
                context, target.Product, target.Batch, type == "damage" ? "damage" : "expiry",
                -quantity, null, null, null, null, Cost(cost / quantity),
                "inventory-writeoff:" + writeoff.Id + ":" + target.Product.Id + ":" + (target.Batch?.Id.ToString(CultureInfo.InvariantCulture) ?? "none"),
                char.ToUpperInvariant(type[0]) + type[1..] + " write-off " + writeoff.Number + ": " + writeoff.Reason,
                "inventory_writeoff", writeoff.Id);
            await context.SaveChangesAsync(cancellationToken);

            writeoff.Items.Add(new InventoryWriteoffItemEntity
            {
                ProductId = target.Product.Id,
                ProductBatchId = target.Batch?.Id,
                StockMovementId = movement.Id,
                QuantityBase = quantity,
                CostAmount = cost,
            });
            totalCost += cost;
            totalQuantity += quantity;
        }

        writeoff.TotalCost = Money(totalCost);
        AddAudit(context, "inventory.writeoff.posted", writeoff.Number);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapWriteoff(writeoff, Quantity(totalQuantity));
    }

    public async Task<IReadOnlyList<InventoryWriteoffResult>> GetWriteoffsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.InventoryWriteoffs.AsNoTracking().Include(x => x.Items)
            .OrderByDescending(x => x.Id).Take(200).ToListAsync(cancellationToken);
        return rows.Select(x => MapWriteoff(x)).ToList();
    }

    private async Task<Target> ResolveTargetAsync(PosDbContext context, string key, CancellationToken cancellationToken)
    {
        var parts = (key ?? string.Empty).Split(':', StringSplitOptions.None);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var productId) || productId <= 0)
            throw new InvalidOperationException("Invalid inventory target.");

        long? batchId = null;
        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            if (!long.TryParse(parts[1], out var parsed) || parsed <= 0)
                throw new InvalidOperationException("Invalid inventory target.");
            batchId = parsed;
        }

        var product = await context.Products.SingleOrDefaultAsync(x => x.Id == productId && x.TrackStock, cancellationToken)
            ?? throw new InvalidOperationException("The selected inventory product is unavailable.");

        ProductBatchEntity? batch = null;
        if (product.TrackExpiry)
        {
            if (batchId is null) throw new InvalidOperationException("Expiry-tracked inventory operations must target a specific batch.");
            batch = await context.ProductBatches.SingleOrDefaultAsync(x => x.Id == batchId && x.ProductId == product.Id, cancellationToken)
                ?? throw new InvalidOperationException("The selected batch is unavailable.");
        }
        else if (batchId is not null)
        {
            throw new InvalidOperationException("A batch cannot be selected for a product that does not track expiry.");
        }

        return new Target(product, batch);
    }

    private async Task<ProductBatchEntity?> ResolveBatchAsync(
        PosDbContext context,
        ProductEntity product,
        string? batchNumber,
        DateTime? manufacturedAt,
        DateTime? expiresAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        batchNumber = Clean(batchNumber);

        if (product.TrackExpiry && batchNumber is null)
            throw new InvalidOperationException("A batch number is required for expiry-tracked products.");
        if (product.TrackExpiry && expiresAt is null)
            throw new InvalidOperationException("An expiry date is required for expiry-tracked products.");
        if (manufacturedAt is not null && expiresAt is not null && expiresAt.Value.Date < manufacturedAt.Value.Date)
            throw new InvalidOperationException("Expiry date cannot be before the manufacturing date.");
        if (batchNumber is null) return null;

        var existing = await context.ProductBatches
            .SingleOrDefaultAsync(x => x.ProductId == product.Id && x.BatchNumber == batchNumber, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsBlocked) throw new InvalidOperationException("Stock cannot be added to a blocked batch.");
            if (expiresAt is not null && existing.ExpiresAt?.Date != expiresAt.Value.Date)
                throw new InvalidOperationException("The expiry date does not match the existing batch.");
            return existing;
        }

        var batch = new ProductBatchEntity
        {
            ProductId = product.Id,
            BatchNumber = batchNumber,
            ManufacturedAt = manufacturedAt?.Date,
            ExpiresAt = expiresAt?.Date,
            Notes = Clean(notes),
            StockOnHand = 0m,
            IsBlocked = false,
        };
        context.ProductBatches.Add(batch);
        await context.SaveChangesAsync(cancellationToken);
        return batch;
    }

    private StockMovementEntity RecordMovement(
        PosDbContext context,
        ProductEntity product,
        ProductBatchEntity? batch,
        string type,
        decimal baseQuantity,
        long? sourceUnitId,
        decimal? sourceQuantity,
        decimal? conversionFactor,
        decimal? sourceUnitCost,
        decimal? unitCostBase,
        string idempotencyKey,
        string? notes,
        string? referenceType = null,
        long? referenceId = null)
    {
        if (!product.TrackStock) throw new InvalidOperationException("Stock movement is not allowed for a product that does not track stock.");
        if (product.TrackExpiry && batch is null) throw new InvalidOperationException("A batch is required for expiry-tracked product movements.");
        if (batch is not null && batch.ProductId != product.Id) throw new InvalidOperationException("The selected batch does not belong to this product.");
        if (baseQuantity == 0m) throw new InvalidOperationException("Stock movement quantity cannot be zero.");

        var productBalance = Quantity(product.StockOnHand + baseQuantity);
        if (productBalance < 0m) throw new InvalidOperationException("Insufficient stock. Negative stock is disabled.");

        decimal? batchBalance = null;
        if (batch is not null)
        {
            batchBalance = Quantity(batch.StockOnHand + baseQuantity);
            if (batchBalance < 0m) throw new InvalidOperationException("A batch balance cannot become negative.");
            batch.StockOnHand = batchBalance.Value;
        }
        product.StockOnHand = productBalance;

        var movement = new StockMovementEntity
        {
            ProductId = product.Id,
            ProductBatchId = batch?.Id,
            SourceUnitId = sourceUnitId,
            ActorUserId = sessions.Current!.UserId,
            MovementType = type,
            SourceQuantity = sourceQuantity is null ? null : Quantity(sourceQuantity.Value),
            ConversionFactor = conversionFactor is null ? null : Quantity(conversionFactor.Value),
            QuantityBase = Quantity(baseQuantity),
            BalanceAfter = productBalance,
            BatchBalanceAfter = batchBalance,
            SourceUnitCost = sourceUnitCost is null ? null : Cost(sourceUnitCost.Value),
            UnitCostBase = unitCostBase is null ? null : Cost(unitCostBase.Value),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            IdempotencyKey = idempotencyKey,
            Notes = Clean(notes),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        context.StockMovements.Add(movement);
        return movement;
    }

    private async Task<decimal> ConsumeCostAsync(
        PosDbContext context,
        ProductEntity product,
        ProductBatchEntity? batch,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var query = context.InventoryCostLayers
            .Where(x => x.ProductId == product.Id && x.RemainingQuantityBase > 0m);
        if (batch is not null)
            query = query.Where(x => x.ProductBatchId == batch.Id);

        var layers = await query.OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var remaining = quantity;
        decimal total = 0m;

        foreach (var layer in layers)
        {
            if (remaining <= 0m) break;
            var take = Math.Min(remaining, layer.RemainingQuantityBase);
            layer.RemainingQuantityBase = Quantity(layer.RemainingQuantityBase - take);
            total += take * layer.UnitCostBase;
            remaining = Quantity(remaining - take);
        }

        if (remaining > 0m)
            total += remaining * product.PurchaseCost;

        return Money(total);
    }

    private static void EnsureAvailable(ProductEntity product, ProductBatchEntity? batch, decimal quantity)
    {
        var available = batch?.StockOnHand ?? product.StockOnHand;
        if (quantity > available) throw new InvalidOperationException("Inventory quantity exceeds available physical stock.");
    }

    private async Task<InventoryMovementRow> MapMovementAsync(
        PosDbContext context,
        StockMovementEntity movement,
        string locale,
        CancellationToken cancellationToken)
    {
        var product = await context.Products.AsNoTracking().SingleAsync(x => x.Id == movement.ProductId, cancellationToken);
        ProductBatchEntity? batch = movement.ProductBatchId is null
            ? null
            : await context.ProductBatches.AsNoTracking().SingleAsync(x => x.Id == movement.ProductBatchId, cancellationToken);
        UnitEntity? unit = movement.SourceUnitId is null
            ? null
            : await context.Units.AsNoTracking().SingleAsync(x => x.Id == movement.SourceUnitId, cancellationToken);

        return new InventoryMovementRow(
            movement.Id,
            movement.ProductId,
            Localize(product.NameEn, product.NameFa, product.NamePs, locale),
            product.Sku,
            movement.MovementType,
            movement.QuantityBase,
            movement.BalanceAfter,
            batch?.BatchNumber,
            movement.BatchBalanceAfter,
            unit is null ? null : Localize(unit.NameEn, unit.NameFa, unit.NamePs, locale),
            movement.SourceQuantity,
            movement.SourceUnitCost,
            movement.UnitCostBase,
            movement.ReferenceType,
            movement.ReferenceId,
            movement.Notes,
            movement.OccurredAt);
    }

    private async Task<StockCountDetail> MapStockCountAsync(
        PosDbContext context,
        StockCountEntity count,
        CancellationToken cancellationToken)
    {
        if (!context.Entry(count).Collection(x => x.Items).IsLoaded)
            await context.Entry(count).Collection(x => x.Items).LoadAsync(cancellationToken);

        var productIds = count.Items.Select(x => x.ProductId).Distinct().ToList();
        var batchIds = count.Items.Where(x => x.ProductBatchId != null).Select(x => x.ProductBatchId!.Value).Distinct().ToList();
        var products = await context.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var batches = await context.ProductBatches.AsNoTracking().Where(x => batchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var locale = RequireLocale();

        return new StockCountDetail(
            count.Id,
            count.Number,
            count.Status,
            count.CountedAt,
            count.ApprovedAt,
            count.Notes,
            count.Items.OrderBy(x => x.Id).Select(x =>
            {
                var p = products[x.ProductId];
                var b = x.ProductBatchId is null ? null : batches.GetValueOrDefault(x.ProductBatchId.Value);
                return new StockCountItemRow(
                    x.Id,
                    x.ProductId + ":" + (x.ProductBatchId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Localize(p.NameEn, p.NameFa, p.NamePs, locale),
                    b?.BatchNumber,
                    x.ExpectedQuantityBase,
                    x.PhysicalQuantityBase,
                    x.VarianceQuantityBase,
                    x.CostAmount);
            }).ToList());
    }

    private static InventoryWriteoffResult MapWriteoff(InventoryWriteoffEntity row, decimal? knownQuantity = null) =>
        new(
            row.Id,
            row.Number,
            row.WriteoffType,
            row.Reason,
            row.Items.Count,
            knownQuantity ?? Quantity(row.Items.Sum(x => x.QuantityBase)),
            row.TotalCost,
            row.PostedAt,
            row.Notes);

    private async Task<string> NextNumberAsync(PosDbContext context, string key, string prefix, CancellationToken cancellationToken)
    {
        var sequence = await context.DocumentSequences.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (sequence is null)
        {
            sequence = new DocumentSequenceEntity { Key = key, NextValue = 1 };
            context.DocumentSequences.Add(sequence);
        }

        var value = sequence.NextValue++;
        await context.SaveChangesAsync(cancellationToken);
        return prefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
               value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private void AddAudit(PosDbContext context, string eventName, string value) =>
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = sessions.Current!.UserId,
            Event = eventName,
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"value\":\"" + EscapeJson(value) + "\"}",
        });

    private string RequireLocale() =>
        sessions.Current?.PreferredLocale ?? throw new InvalidOperationException("No user is signed in.");

    private static InventoryBatchRow MapBatch(ProductBatchEntity batch) =>
        new(
            batch.Id,
            batch.BatchNumber,
            batch.ManufacturedAt,
            batch.ExpiresAt,
            batch.StockOnHand,
            batch.IsBlocked,
            batch.Notes,
            batch.ExpiresAt is not null && batch.ExpiresAt.Value.Date < DateTime.Today);

    private static void ValidateUuid(string value)
    {
        if (!Guid.TryParse(value, out _)) throw new InvalidOperationException("A valid idempotency key is required.");
    }

    private static void ValidatePrecision(decimal value, int places, string field)
    {
        var normalized = Math.Abs(value);
        var decimals = 0;
        while (normalized != decimal.Truncate(normalized) && decimals < 28)
        {
            normalized *= 10m;
            decimals++;
        }
        if (decimals > places) throw new InvalidOperationException(field + " exceeds the selected unit precision of " + places + " decimal places.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Cost(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps,
            _ => en,
        };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record Target(ProductEntity Product, ProductBatchEntity? Batch);
}
