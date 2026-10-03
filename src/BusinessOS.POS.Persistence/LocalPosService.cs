using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalPosService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IPosService
{
    public async Task<PosReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default)
    {
        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var methods = await context.PaymentMethods
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var language = user.PreferredLocale;
        var payments = methods.Select(x => new PosPaymentMethod(
            x.Id,
            x.Code,
            Localize(x.NameEn, x.NameFa, x.NamePs, language),
            x.IsCash)).ToList();

        var shift = await context.CashierShifts
            .AsNoTracking()
            .Where(x => x.UserId == user.UserId && x.Status == "open")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new PosReferenceData(
            payments,
            shift is null
                ? new PosShiftState(false, null, 0m, null)
                : new PosShiftState(true, shift.Id, shift.OpeningCash, shift.OpenedAt));
    }

    public async Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(
        string query,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("pos.access");
        var user = RequireUser();
        query = (query ?? string.Empty).Trim();
        take = Math.Clamp(take, 1, 100);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (query.Length > 0)
        {
            var exactBarcode = await context.ProductBarcodes
                .AsNoTracking()
                .Include(x => x.ProductUnit)
                    .ThenInclude(x => x.Product)
                .Include(x => x.ProductUnit)
                    .ThenInclude(x => x.Unit)
                .FirstOrDefaultAsync(
                    x => x.Barcode == query &&
                         x.Product.IsActive &&
                         x.ProductUnit.CanSell,
                    cancellationToken);

            if (exactBarcode is not null)
            {
                var availableBase = await GetSellableBaseQuantityAsync(
                    context,
                    exactBarcode.ProductUnit.Product,
                    cancellationToken);
                return [MapProduct(
                    exactBarcode.ProductUnit,
                    user.PreferredLocale,
                    exactBarcode.Barcode,
                    availableBase)];
            }
        }

        var units = await context.ProductUnits
            .AsNoTracking()
            .Include(x => x.Product)
                .ThenInclude(x => x.Barcodes)
            .Include(x => x.Unit)
            .Where(x => x.CanSell && x.Product.IsActive)
            .OrderBy(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);

        IEnumerable<ProductUnitEntity> filtered = units;
        if (query.Length > 0)
        {
            filtered = units.Where(x =>
                Contains(x.Product.Sku, query) ||
                Contains(x.Product.NameEn, query) ||
                Contains(x.Product.NameFa, query) ||
                Contains(x.Product.NamePs, query) ||
                x.Product.Barcodes.Any(b => Contains(b.Barcode, query)));
        }

        var selected = filtered.Take(take).ToList();
        var expiryProductIds = selected
            .Where(x => x.Product.TrackExpiry)
            .Select(x => x.ProductId)
            .Distinct()
            .ToList();

        var sellableByProduct = new Dictionary<long, decimal>();
        if (expiryProductIds.Count > 0)
        {
            var allBatches = await context.ProductBatches
                .AsNoTracking()
                .Where(x => expiryProductIds.Contains(x.ProductId))
                .ToListAsync(cancellationToken);
            var today = DateTime.Today;
            sellableByProduct = allBatches
                .Where(x => !x.IsBlocked && x.ExpiresAt is not null && x.ExpiresAt.Value.Date >= today && x.StockOnHand > 0m)
                .GroupBy(x => x.ProductId)
                .ToDictionary(x => x.Key, x => Quantity(x.Sum(b => b.StockOnHand)));
        }

        return selected
            .Select(x => MapProduct(
                x,
                user.PreferredLocale,
                null,
                x.Product.TrackExpiry ? sellableByProduct.GetValueOrDefault(x.ProductId, 0m) : null))
            .ToList();
    }

    public async Task<PosShiftState> OpenShiftAsync(
        decimal openingCash,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("shifts.open");
        if (openingCash < 0m)
        {
            throw new InvalidOperationException("Opening cash cannot be negative.");
        }

        var user = RequireUser();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.CashierShifts
            .Where(x => x.UserId == user.UserId && x.Status == "open")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return new PosShiftState(true, existing.Id, existing.OpeningCash, existing.OpenedAt);
        }

        var shift = new CashierShiftEntity
        {
            UserId = user.UserId,
            OpeningCash = Money(openingCash),
            Status = "open",
            OpenedAt = DateTimeOffset.UtcNow,
        };
        context.CashierShifts.Add(shift);
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "cash.shift.opened",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"opening_cash\":\"" + Money(openingCash).ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });
        await context.SaveChangesAsync(cancellationToken);

        return new PosShiftState(true, shift.Id, shift.OpeningCash, shift.OpenedAt);
    }

    public async Task<PosCheckoutResult> CheckoutAsync(
        PosCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.create");
        ValidateIdempotencyKey(request.IdempotencyKey);

        if (request.Lines.Count is < 1 or > 200)
        {
            throw new InvalidOperationException("A sale requires between 1 and 200 items.");
        }

        if (request.Payments.Count is < 1 or > 8)
        {
            throw new InvalidOperationException("At least one payment is required.");
        }

        var user = RequireUser();
        var fingerprint = Fingerprint(request);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var existing = await context.Sales
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This checkout key is already bound to a different cart.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ToResult(existing);
        }

        var lineIds = request.Lines.Select(x => x.ProductUnitId).ToList();
        if (lineIds.Distinct().Count() != lineIds.Count)
        {
            throw new InvalidOperationException("The same product unit cannot appear twice in one sale.");
        }

        var productUnits = await context.ProductUnits
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => lineIds.Contains(x.Id) && x.CanSell && x.Product.IsActive)
            .ToListAsync(cancellationToken);

        if (productUnits.Count != lineIds.Count)
        {
            throw new InvalidOperationException("One or more selected products are unavailable.");
        }

        var byId = productUnits.ToDictionary(x => x.Id);
        var prepared = new List<PreparedLine>(request.Lines.Count);
        decimal subtotal = 0m;
        decimal lineDiscountTotal = 0m;

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0m)
            {
                throw new InvalidOperationException("Sale quantity must be greater than zero.");
            }

            var productUnit = byId[line.ProductUnitId];
            EnsurePrecision(line.Quantity, productUnit.Unit.DecimalPlaces);

            var price = Money(productUnit.SellingPrice ?? productUnit.Product.SellingPrice);
            var minimumPrice = productUnit.MinimumSellingPrice ?? productUnit.Product.MinimumSellingPrice;
            var lineSubtotal = Money(line.Quantity * price);
            var discount = Money(line.LineDiscountAmount);

            if (discount < 0m || discount > lineSubtotal)
            {
                throw new InvalidOperationException("Sale line discount is invalid.");
            }

            if (discount > 0m)
            {
                authorizer.Demand("sales.discount");
            }

            var quantityBase = Quantity(line.Quantity * productUnit.ConversionFactor);
            if (productUnit.Product.TrackStock)
            {
                if (productUnit.Product.StockOnHand < quantityBase)
                {
                    throw new InvalidOperationException("Insufficient stock for " + productUnit.Product.NameEn + ".");
                }

                if (productUnit.Product.TrackExpiry)
                {
                    var sellableBase = await GetSellableBaseQuantityAsync(context, productUnit.Product, cancellationToken);
                    if (sellableBase < quantityBase)
                    {
                        throw new InvalidOperationException(
                            "Insufficient non-expired batch stock for " + productUnit.Product.NameEn + ".");
                    }
                }
            }

            subtotal += lineSubtotal;
            lineDiscountTotal += discount;
            prepared.Add(new PreparedLine(productUnit, line.Quantity, quantityBase, price, minimumPrice, lineSubtotal, discount));
        }

        subtotal = Money(subtotal);
        lineDiscountTotal = Money(lineDiscountTotal);
        var basisTotal = Money(subtotal - lineDiscountTotal);
        var saleDiscount = Money(request.SaleDiscountAmount);

        if (saleDiscount < 0m || saleDiscount > basisTotal)
        {
            throw new InvalidOperationException("Sale discount is invalid.");
        }

        if (saleDiscount > 0m)
        {
            authorizer.Demand("sales.discount");
        }

        AllocateSaleDiscount(prepared, saleDiscount, basisTotal);

        foreach (var line in prepared)
        {
            if (line.MinimumPrice is null)
            {
                continue;
            }

            var effective = line.Quantity == 0m ? 0m : Money(line.NetTotal / line.Quantity);
            if (effective < line.MinimumPrice.Value && !authorizer.HasPermission("sales.override_min_price"))
            {
                throw new InvalidOperationException(
                    "The final price for " + line.ProductUnit.Product.NameEn + " is below its configured minimum price.");
            }
        }

        var netTotal = Money(basisTotal - saleDiscount);
        var paymentMethods = await context.PaymentMethods
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        decimal tendered = 0m;
        var hasCash = false;
        foreach (var payment in request.Payments)
        {
            if (payment.Amount <= 0m)
            {
                throw new InvalidOperationException("Payment amount must be greater than zero.");
            }

            if (!paymentMethods.TryGetValue(payment.MethodCode, out var method))
            {
                throw new InvalidOperationException("The selected payment method is unavailable.");
            }

            tendered += Money(payment.Amount);
            hasCash |= method.IsCash;
        }

        tendered = Money(tendered);
        if (tendered < netTotal)
        {
            throw new InvalidOperationException("Payment does not cover the sale total.");
        }

        if (tendered > netTotal && !hasCash)
        {
            throw new InvalidOperationException("Change can only be returned when cash is part of the payment.");
        }

        CashierShiftEntity? shift = null;
        if (hasCash)
        {
            shift = await context.CashierShifts
                .Where(x => x.UserId == user.UserId && x.Status == "open")
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (shift is null)
            {
                throw new InvalidOperationException("Open a cashier shift before completing a cash sale.");
            }
        }

        var soldAt = DateTimeOffset.UtcNow;
        var sale = new SaleEntity
        {
            Number = await NextNumberAsync(context, "sale", "SAL", soldAt, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            RequestFingerprint = fingerprint,
            CashierUserId = user.UserId,
            CashierShiftId = shift?.Id,
            Subtotal = subtotal,
            LineDiscountTotal = lineDiscountTotal,
            SaleDiscountAmount = saleDiscount,
            NetTotal = netTotal,
            PaidAmount = netTotal,
            ChangeAmount = Money(tendered - netTotal),
            SoldAt = soldAt,
            Notes = request.Notes?.Trim(),
        };
        context.Sales.Add(sale);
        await context.SaveChangesAsync(cancellationToken);

        decimal cogsTotal = 0m;
        foreach (var line in prepared)
        {
            var product = line.ProductUnit.Product;
            var cogs = await ConsumeFifoAsync(context, product, line.QuantityBase, cancellationToken);
            cogsTotal += cogs;

            if (product.TrackStock)
            {
                await DeductPhysicalStockAsync(
                    context,
                    product,
                    line.ProductUnit,
                    line.Quantity,
                    line.QuantityBase,
                    cogs,
                    sale,
                    user.UserId,
                    soldAt,
                    cancellationToken);
            }

            sale.Items.Add(new SaleItemEntity
            {
                ProductId = product.Id,
                ProductUnitId = line.ProductUnit.Id,
                ProductNameSnapshot = Localize(product.NameEn, product.NameFa, product.NamePs, user.PreferredLocale),
                SkuSnapshot = product.Sku,
                UnitNameSnapshot = Localize(line.ProductUnit.Unit.NameEn, line.ProductUnit.Unit.NameFa, line.ProductUnit.Unit.NamePs, user.PreferredLocale),
                Quantity = line.Quantity,
                ConversionFactor = line.ProductUnit.ConversionFactor,
                QuantityBase = line.QuantityBase,
                UnitPrice = line.UnitPrice,
                MinimumUnitPrice = line.MinimumPrice,
                LineSubtotal = line.LineSubtotal,
                LineDiscountAmount = line.LineDiscount,
                AllocatedSaleDiscount = line.AllocatedSaleDiscount,
                LineNetTotal = line.NetTotal,
                CogsAmount = cogs,
                GrossProfit = Money(line.NetTotal - cogs),
            });
        }

        sale.CogsTotal = Money(cogsTotal);
        sale.GrossProfit = Money(sale.NetTotal - sale.CogsTotal);

        foreach (var payment in request.Payments)
        {
            sale.Payments.Add(new SalePaymentEntity
            {
                MethodCode = payment.MethodCode,
                Amount = Money(payment.Amount),
                Reference = payment.Reference?.Trim(),
                PaidAt = soldAt,
            });
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.UserId,
            Event = "sales.sale.completed",
            CreatedAt = soldAt,
            DetailsJson = "{\"sale_number\":\"" + sale.Number + "\",\"net_total\":\"" +
                          sale.NetTotal.ToString("0.00", CultureInfo.InvariantCulture) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(sale);
    }

    public async Task<PosHeldSaleSummary> HoldAsync(
        PosHoldRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.hold");
        ValidateIdempotencyKey(request.IdempotencyKey);

        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException("A held sale requires at least one item.");
        }

        var user = RequireUser();
        if (request.SaleDiscountAmount > 0m)
        {
            authorizer.Demand("sales.discount");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.HeldSales
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return ToHeldSummary(existing);
        }

        var ids = request.Lines.Select(x => x.ProductUnitId).Distinct().ToList();
        var units = await context.ProductUnits
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => ids.Contains(x.Id) && x.CanSell && x.Product.IsActive)
            .ToListAsync(cancellationToken);

        if (units.Count != ids.Count)
        {
            throw new InvalidOperationException("One or more products are unavailable.");
        }

        var byId = units.ToDictionary(x => x.Id);
        var now = DateTimeOffset.UtcNow;
        var held = new HeldSaleEntity
        {
            Number = await NextNumberAsync(context, "held_sale", "HLD", now, cancellationToken),
            IdempotencyKey = request.IdempotencyKey,
            CashierUserId = user.UserId,
            SaleDiscountAmount = Money(request.SaleDiscountAmount),
            Notes = request.Notes?.Trim(),
            HeldAt = now,
        };

        foreach (var line in request.Lines)
        {
            var productUnit = byId[line.ProductUnitId];
            var price = Money(productUnit.SellingPrice ?? productUnit.Product.SellingPrice);
            var subtotal = Money(price * line.Quantity);
            var discount = Money(line.LineDiscountAmount);

            if (line.Quantity <= 0m || discount < 0m || discount > subtotal)
            {
                throw new InvalidOperationException("Held sale line is invalid.");
            }

            if (discount > 0m)
            {
                authorizer.Demand("sales.discount");
            }

            held.Items.Add(new HeldSaleItemEntity
            {
                ProductUnitId = productUnit.Id,
                ProductNameSnapshot = Localize(productUnit.Product.NameEn, productUnit.Product.NameFa, productUnit.Product.NamePs, user.PreferredLocale),
                SkuSnapshot = productUnit.Product.Sku,
                UnitNameSnapshot = Localize(productUnit.Unit.NameEn, productUnit.Unit.NameFa, productUnit.Unit.NamePs, user.PreferredLocale),
                Quantity = line.Quantity,
                LineDiscountAmount = discount,
                UnitPriceSnapshot = price,
            });
        }

        context.HeldSales.Add(held);
        await context.SaveChangesAsync(cancellationToken);
        return ToHeldSummary(held);
    }

    public async Task<IReadOnlyList<PosHeldSaleSummary>> GetHeldSalesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.hold");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.HeldSales
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.Status == "held");

        if (!authorizer.HasPermission("sales.void"))
        {
            query = query.Where(x => x.CashierUserId == user.UserId);
        }

        return (await query.OrderByDescending(x => x.Id).Take(50).ToListAsync(cancellationToken))
            .Select(ToHeldSummary)
            .ToList();
    }

    public async Task<PosHeldSaleDetail> ResumeHeldSaleAsync(
        long heldSaleId,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("sales.hold");
        var user = RequireUser();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var held = await context.HeldSales
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == heldSaleId, cancellationToken)
            ?? throw new InvalidOperationException("Held sale was not found.");

        if (held.Status != "held")
        {
            throw new InvalidOperationException("Only an active held sale can be resumed.");
        }

        if (held.CashierUserId != user.UserId && !authorizer.HasPermission("sales.void"))
        {
            throw new InvalidOperationException("This held sale belongs to another cashier.");
        }

        var ids = held.Items.Select(x => x.ProductUnitId).Distinct().ToList();
        var units = await context.ProductUnits
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Unit)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var lines = held.Items.Select(item =>
        {
            var productUnit = units[item.ProductUnitId];
            return new PosHeldSaleLine(
                item.ProductUnitId,
                item.ProductNameSnapshot,
                item.SkuSnapshot,
                item.UnitNameSnapshot,
                item.Quantity,
                item.UnitPriceSnapshot,
                item.LineDiscountAmount,
                Available(productUnit),
                productUnit.MinimumSellingPrice ?? productUnit.Product.MinimumSellingPrice,
                productUnit.Product.TrackStock,
                productUnit.Unit.DecimalPlaces,
                productUnit.ConversionFactor);
        }).ToList();

        held.Status = "resumed";
        held.ResumedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PosHeldSaleDetail(
            held.Id,
            held.Number,
            held.SaleDiscountAmount,
            held.Notes,
            lines);
    }

    private BusinessOS.POS.Domain.Authentication.UserSessionSnapshot RequireUser() =>
        sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

    private PosProductSearchItem MapProduct(
        ProductUnitEntity unit,
        string language,
        string? matchedBarcode,
        decimal? availableBaseOverride = null) =>
        new(
            unit.Id,
            unit.ProductId,
            Localize(unit.Product.NameEn, unit.Product.NameFa, unit.Product.NamePs, language),
            unit.Product.Sku,
            Localize(unit.Unit.NameEn, unit.Unit.NameFa, unit.Unit.NamePs, language),
            unit.Unit.DecimalPlaces,
            unit.ConversionFactor,
            Money(unit.SellingPrice ?? unit.Product.SellingPrice),
            unit.MinimumSellingPrice ?? unit.Product.MinimumSellingPrice,
            Available(unit, availableBaseOverride),
            unit.Product.TrackStock,
            matchedBarcode);

    private static decimal? Available(ProductUnitEntity unit, decimal? availableBaseOverride = null) =>
        !unit.Product.TrackStock
            ? null
            : unit.ConversionFactor <= 0m
                ? 0m
                : Quantity((availableBaseOverride ?? unit.Product.StockOnHand) / unit.ConversionFactor);

    private static bool Contains(string? source, string value) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps,
            _ => en,
        };

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void EnsurePrecision(decimal value, int decimalPlaces)
    {
        var rounded = decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
        if (rounded != value)
        {
            throw new InvalidOperationException("Sale quantity exceeds the selected unit precision.");
        }
    }

    private static void ValidateIdempotencyKey(string value)
    {
        if (!Guid.TryParse(value, out _))
        {
            throw new InvalidOperationException("A valid checkout idempotency key is required.");
        }
    }

    private static string Fingerprint(PosCheckoutRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(Money(request.SaleDiscountAmount).ToString(CultureInfo.InvariantCulture)).Append('|');

        foreach (var line in request.Lines.OrderBy(x => x.ProductUnitId))
        {
            builder.Append(line.ProductUnitId).Append(':')
                .Append(Quantity(line.Quantity).ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(Money(line.LineDiscountAmount).ToString(CultureInfo.InvariantCulture)).Append('|');
        }

        foreach (var payment in request.Payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal))
        {
            builder.Append(payment.MethodCode).Append(':')
                .Append(Money(payment.Amount).ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(payment.Reference?.Trim()).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AllocateSaleDiscount(List<PreparedLine> lines, decimal saleDiscount, decimal basisTotal)
    {
        if (saleDiscount <= 0m || basisTotal <= 0m)
        {
            foreach (var line in lines)
            {
                line.NetTotal = Money(line.LineSubtotal - line.LineDiscount);
            }
            return;
        }

        var remaining = saleDiscount;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var basis = Money(line.LineSubtotal - line.LineDiscount);
            var allocation = i == lines.Count - 1
                ? remaining
                : Money(saleDiscount * basis / basisTotal);

            allocation = Math.Min(allocation, basis);
            line.AllocatedSaleDiscount = allocation;
            line.NetTotal = Money(basis - allocation);
            remaining = Money(remaining - allocation);
        }
    }

    private static async Task<decimal> GetSellableBaseQuantityAsync(
        PosDbContext context,
        ProductEntity product,
        CancellationToken cancellationToken)
    {
        if (!product.TrackStock)
        {
            return 0m;
        }

        if (!product.TrackExpiry)
        {
            return product.StockOnHand;
        }

        var batches = await context.ProductBatches
            .AsNoTracking()
            .Where(x => x.ProductId == product.Id && !x.IsBlocked && x.StockOnHand > 0m)
            .ToListAsync(cancellationToken);

        var today = DateTime.Today;
        return Quantity(batches
            .Where(x => x.ExpiresAt is not null && x.ExpiresAt.Value.Date >= today)
            .Sum(x => x.StockOnHand));
    }

    private static async Task DeductPhysicalStockAsync(
        PosDbContext context,
        ProductEntity product,
        ProductUnitEntity productUnit,
        decimal sourceQuantity,
        decimal quantityBase,
        decimal cogs,
        SaleEntity sale,
        long actorUserId,
        DateTimeOffset soldAt,
        CancellationToken cancellationToken)
    {
        var averageCost = quantityBase == 0m ? 0m : decimal.Round(cogs / quantityBase, 4);

        if (!product.TrackExpiry)
        {
            product.StockOnHand = Quantity(product.StockOnHand - quantityBase);
            context.StockMovements.Add(new StockMovementEntity
            {
                ProductId = product.Id,
                SourceUnitId = productUnit.UnitId,
                ActorUserId = actorUserId,
                MovementType = "sale",
                SourceQuantity = -sourceQuantity,
                ConversionFactor = productUnit.ConversionFactor,
                QuantityBase = -quantityBase,
                BalanceAfter = product.StockOnHand,
                UnitCostBase = averageCost,
                ReferenceType = "sale",
                ReferenceId = sale.Id,
                IdempotencyKey = "sale:" + sale.Id + ":product:" + product.Id,
                Notes = "Sale " + sale.Number,
                OccurredAt = soldAt,
            });
            return;
        }

        var candidateBatches = await context.ProductBatches
            .Where(x => x.ProductId == product.Id && !x.IsBlocked && x.StockOnHand > 0m)
            .ToListAsync(cancellationToken);
        var today = DateTime.Today;
        var batches = candidateBatches
            .Where(x => x.ExpiresAt is not null && x.ExpiresAt.Value.Date >= today)
            .OrderBy(x => x.ExpiresAt)
            .ThenBy(x => x.Id)
            .ToList();

        var sellable = Quantity(batches.Sum(x => x.StockOnHand));
        if (sellable < quantityBase)
        {
            throw new InvalidOperationException(
                "Insufficient non-expired batch stock for " + product.NameEn + ".");
        }

        var remaining = quantityBase;
        foreach (var batch in batches)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, batch.StockOnHand);
            if (take <= 0m)
            {
                continue;
            }

            batch.StockOnHand = Quantity(batch.StockOnHand - take);
            product.StockOnHand = Quantity(product.StockOnHand - take);
            remaining = Quantity(remaining - take);

            context.StockMovements.Add(new StockMovementEntity
            {
                ProductId = product.Id,
                ProductBatchId = batch.Id,
                ActorUserId = actorUserId,
                MovementType = "sale",
                QuantityBase = -take,
                BalanceAfter = product.StockOnHand,
                BatchBalanceAfter = batch.StockOnHand,
                UnitCostBase = averageCost,
                ReferenceType = "sale",
                ReferenceId = sale.Id,
                IdempotencyKey = "sale:" + sale.Id + ":product:" + product.Id + ":batch:" + batch.Id,
                Notes = "Sale " + sale.Number + " · FEFO batch " + batch.BatchNumber,
                OccurredAt = soldAt,
            });
        }
    }

    private static async Task<decimal> ConsumeFifoAsync(
        PosDbContext context,
        ProductEntity product,
        decimal quantityBase,
        CancellationToken cancellationToken)
    {
        if (!product.TrackStock || quantityBase <= 0m)
        {
            return 0m;
        }

        var layers = await context.InventoryCostLayers
            .Where(x => x.ProductId == product.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var remaining = quantityBase;
        decimal total = 0m;

        foreach (var layer in layers)
        {
            if (remaining <= 0m)
            {
                break;
            }

            if (layer.RemainingQuantityBase <= 0m)
            {
                continue;
            }

            var take = Math.Min(remaining, layer.RemainingQuantityBase);
            layer.RemainingQuantityBase = Quantity(layer.RemainingQuantityBase - take);
            total += take * layer.UnitCostBase;
            remaining = Quantity(remaining - take);
        }

        if (remaining > 0m)
        {
            total += remaining * product.PurchaseCost;
        }

        return Money(total);
    }

    private static async Task<string> NextNumberAsync(
        PosDbContext context,
        string key,
        string prefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sequence = await context.DocumentSequences.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (sequence is null)
        {
            sequence = new DocumentSequenceEntity { Key = key, NextValue = 1 };
            context.DocumentSequences.Add(sequence);
        }

        var value = sequence.NextValue;
        sequence.NextValue++;
        await context.SaveChangesAsync(cancellationToken);
        return prefix + "-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static PosCheckoutResult ToResult(SaleEntity sale) =>
        new(sale.Id, sale.Number, sale.NetTotal, sale.PaidAmount, sale.ChangeAmount, sale.CogsTotal, sale.GrossProfit, sale.SoldAt);

    private static PosHeldSaleSummary ToHeldSummary(HeldSaleEntity held)
    {
        var subtotal = held.Items.Sum(x => Money((x.Quantity * x.UnitPriceSnapshot) - x.LineDiscountAmount));
        return new PosHeldSaleSummary(
            held.Id,
            held.Number,
            held.Items.Count,
            Money(Math.Max(0m, subtotal - held.SaleDiscountAmount)),
            held.HeldAt);
    }

    private sealed class PreparedLine(
        ProductUnitEntity productUnit,
        decimal quantity,
        decimal quantityBase,
        decimal unitPrice,
        decimal? minimumPrice,
        decimal lineSubtotal,
        decimal lineDiscount)
    {
        public ProductUnitEntity ProductUnit { get; } = productUnit;
        public decimal Quantity { get; } = quantity;
        public decimal QuantityBase { get; } = quantityBase;
        public decimal UnitPrice { get; } = unitPrice;
        public decimal? MinimumPrice { get; } = minimumPrice;
        public decimal LineSubtotal { get; } = lineSubtotal;
        public decimal LineDiscount { get; } = lineDiscount;
        public decimal AllocatedSaleDiscount { get; set; }
        public decimal NetTotal { get; set; }
    }
}
