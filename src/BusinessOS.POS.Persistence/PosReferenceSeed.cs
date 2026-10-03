using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class PosReferenceSeed
{
    public static async Task ApplyAsync(PosDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.Units.AnyAsync(cancellationToken))
        {
            context.Units.AddRange(
                Unit("PCS", "Piece", "عدد", "عدد", "pc", 0),
                Unit("PACK", "Packet", "بسته", "پاکټ", "pack", 0),
                Unit("BOX", "Box", "بکس", "بکس", "box", 0),
                Unit("CTN", "Carton", "کارتن", "کارتن", "ctn", 0),
                Unit("BTL", "Bottle", "بوتل", "بوتل", "btl", 0),
                Unit("CAN", "Can", "قوطی", "قوطی", "can", 0),
                Unit("BAG", "Bag", "بوجی", "بوجۍ", "bag", 0),
                Unit("KG", "Kilogram", "کیلوگرام", "کیلوګرام", "kg", 3),
                Unit("G", "Gram", "گرام", "ګرام", "g", 0),
                Unit("L", "Liter", "لیتر", "لیټر", "L", 3),
                Unit("DOZ", "Dozen", "درجن", "درجن", "doz", 0));
        }

        if (!await context.PaymentMethods.AnyAsync(cancellationToken))
        {
            context.PaymentMethods.AddRange(
                Method("cash", "Cash", "نقد", "نغدي", true, 10),
                Method("bank", "Bank", "بانک", "بانک", false, 20),
                Method("mobile_wallet", "Mobile Wallet", "کیف پول موبایل", "موبایل والټ", false, 30),
                Method("other", "Other", "سایر", "نور", false, 40));
        }

        await context.SaveChangesAsync(cancellationToken);

        if (await context.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var units = await context.Units.ToDictionaryAsync(x => x.Code, cancellationToken);
        var demo = new (string En, string Fa, string Ps, string Unit, decimal Cost, decimal Price, decimal Qty)[]
        {
            ("Rice 5 kg", "برنج ۵ کیلو", "وریجې ۵ کیلو", "BAG", 420, 490, 20),
            ("Flour 5 kg", "آرد ۵ کیلو", "اوړه ۵ کیلو", "BAG", 240, 285, 24),
            ("Sugar 1 kg", "شکر ۱ کیلو", "بوره ۱ کیلو", "BAG", 70, 85, 40),
            ("Cooking Oil 1 L", "روغن پخت و پز ۱ لیتر", "د پخلي غوړي ۱ لیټر", "BTL", 115, 140, 35),
            ("Black Tea 500 g", "چای سیاه ۵۰۰ گرام", "تور چای ۵۰۰ ګرامه", "PACK", 190, 230, 20),
            ("Green Tea 250 g", "چای سبز ۲۵۰ گرام", "شین چای ۲۵۰ ګرامه", "PACK", 105, 130, 20),
            ("Salt 1 kg", "نمک ۱ کیلو", "مالګه ۱ کیلو", "BAG", 25, 35, 30),
            ("Red Lentils 1 kg", "دال سرخ ۱ کیلو", "سره دال ۱ کیلو", "BAG", 115, 140, 25),
            ("Chickpeas 1 kg", "نخود ۱ کیلو", "نخود ۱ کیلو", "BAG", 130, 160, 25),
            ("Pasta 500 g", "مکرونی ۵۰۰ گرام", "مکروني ۵۰۰ ګرامه", "PACK", 45, 60, 35),
            ("Biscuits Pack", "بیسکویت بسته", "بسکټ پاکټ", "PACK", 25, 35, 50),
            ("Chocolate Bar", "چاکلیت تخته ای", "چاکلېټ ټوټه", "PCS", 25, 35, 45),
            ("Bottled Water 500 ml", "آب معدنی ۵۰۰ ملی لیتر", "د اوبو بوتل ۵۰۰ ملي لیټر", "BTL", 12, 20, 72),
            ("Bottled Water 1.5 L", "آب معدنی ۱.۵ لیتر", "د اوبو بوتل ۱.۵ لیټر", "BTL", 25, 35, 40),
            ("Orange Juice 1 L", "آب مالته ۱ لیتر", "د مالټې جوس ۱ لیټر", "BTL", 75, 95, 25),
            ("Cola 330 ml", "نوشابه ۳۳۰ ملی لیتر", "کولا ۳۳۰ ملي لیټر", "CAN", 30, 40, 48),
            ("Milk 1 L", "شیر ۱ لیتر", "شیدې ۱ لیټر", "BTL", 65, 80, 25),
            ("Powdered Milk 400 g", "شیر خشک ۴۰۰ گرام", "وچې شیدې ۴۰۰ ګرامه", "CAN", 245, 295, 15),
            ("Laundry Detergent 1 kg", "پودر لباس شویی ۱ کیلو", "د جامو پوډر ۱ کیلو", "BAG", 115, 145, 25),
            ("Dishwashing Liquid 500 ml", "مایع ظرف شویی ۵۰۰ ملی لیتر", "د لوښو مایع ۵۰۰ ملي لیټر", "BTL", 65, 85, 25),
            ("Bath Soap", "صابون حمام", "د حمام صابون", "PCS", 30, 45, 45),
            ("Shampoo 400 ml", "شامپو ۴۰۰ ملی لیتر", "شامپو ۴۰۰ ملي لیټر", "BTL", 140, 175, 20),
            ("Toothpaste 100 ml", "کریم دندان ۱۰۰ ملی لیتر", "د غاښونو کریم ۱۰۰ ملي لیټر", "PCS", 70, 95, 25),
            ("Tissue Box", "دستمال کاغذی جعبه", "د کاغذي دستمالو بکس", "BOX", 55, 75, 30),
        };

        for (var index = 0; index < demo.Length; index++)
        {
            var item = demo[index];
            var product = new ProductEntity
            {
                Sku = $"DEMO-{index + 1:0000}",
                NameEn = item.En,
                NameFa = item.Fa,
                NamePs = item.Ps,
                PurchaseCost = item.Cost,
                SellingPrice = item.Price,
                MinimumSellingPrice = decimal.Round(item.Price * 0.85m, 2),
                StockOnHand = item.Qty,
                MinimumStock = 5m,
                TrackStock = true,
                IsActive = true,
            };
            context.Products.Add(product);
            await context.SaveChangesAsync(cancellationToken);

            var productUnit = new ProductUnitEntity
            {
                ProductId = product.Id,
                UnitId = units[item.Unit].Id,
                ConversionFactor = 1m,
                CanPurchase = true,
                CanSell = true,
            };
            context.ProductUnits.Add(productUnit);
            await context.SaveChangesAsync(cancellationToken);

            context.ProductBarcodes.Add(new ProductBarcodeEntity
            {
                ProductId = product.Id,
                ProductUnitId = productUnit.Id,
                Barcode = $"62910010{index + 1:00000}",
                IsPrimary = true,
            });

            context.InventoryCostLayers.Add(new InventoryCostLayerEntity
            {
                ProductId = product.Id,
                InitialQuantityBase = item.Qty,
                RemainingQuantityBase = item.Qty,
                UnitCostBase = item.Cost,
                ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(index),
            });

            context.StockMovements.Add(new StockMovementEntity
            {
                ProductId = product.Id,
                MovementType = "opening_stock",
                QuantityBase = item.Qty,
                BalanceAfter = item.Qty,
                UnitCostBase = item.Cost,
                Notes = "Demo opening inventory",
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static UnitEntity Unit(string code, string en, string fa, string ps, string symbol, int decimals) =>
        new() { Code = code, NameEn = en, NameFa = fa, NamePs = ps, Symbol = symbol, DecimalPlaces = decimals, IsActive = true };

    private static PaymentMethodEntity Method(string code, string en, string fa, string ps, bool cash, int order) =>
        new() { Code = code, NameEn = en, NameFa = fa, NamePs = ps, IsCash = cash, SortOrder = order, IsActive = true };
}
