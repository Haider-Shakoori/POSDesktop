using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class LocalSchemaUpgrade
{
    public static async Task ApplyAsync(PosDbContext context, CancellationToken cancellationToken)
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS units (
    Id INTEGER NOT NULL CONSTRAINT PK_units PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    Symbol TEXT NULL,
    DecimalPlaces INTEGER NOT NULL,
    IsActive INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_units_Code ON units (Code);

CREATE TABLE IF NOT EXISTS payment_methods (
    Id INTEGER NOT NULL CONSTRAINT PK_payment_methods PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    IsCash INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL,
    IsActive INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_payment_methods_Code ON payment_methods (Code);

CREATE TABLE IF NOT EXISTS products (
    Id INTEGER NOT NULL CONSTRAINT PK_products PRIMARY KEY AUTOINCREMENT,
    Sku TEXT NOT NULL,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    PurchaseCost TEXT NOT NULL,
    SellingPrice TEXT NOT NULL,
    MinimumSellingPrice TEXT NULL,
    StockOnHand TEXT NOT NULL,
    MinimumStock TEXT NOT NULL,
    TrackStock INTEGER NOT NULL,
    TrackExpiry INTEGER NOT NULL,
    IsActive INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_products_Sku ON products (Sku);

CREATE TABLE IF NOT EXISTS product_units (
    Id INTEGER NOT NULL CONSTRAINT PK_product_units PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    UnitId INTEGER NOT NULL,
    ConversionFactor TEXT NOT NULL,
    CanPurchase INTEGER NOT NULL,
    CanSell INTEGER NOT NULL,
    SellingPrice TEXT NULL,
    MinimumSellingPrice TEXT NULL,
    CONSTRAINT FK_product_units_products_ProductId FOREIGN KEY (ProductId) REFERENCES products (Id) ON DELETE CASCADE,
    CONSTRAINT FK_product_units_units_UnitId FOREIGN KEY (UnitId) REFERENCES units (Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_product_units_ProductId_UnitId ON product_units (ProductId, UnitId);

CREATE TABLE IF NOT EXISTS product_barcodes (
    Id INTEGER NOT NULL CONSTRAINT PK_product_barcodes PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    ProductUnitId INTEGER NOT NULL,
    Barcode TEXT NOT NULL,
    IsPrimary INTEGER NOT NULL,
    CONSTRAINT FK_product_barcodes_products_ProductId FOREIGN KEY (ProductId) REFERENCES products (Id) ON DELETE CASCADE,
    CONSTRAINT FK_product_barcodes_product_units_ProductUnitId FOREIGN KEY (ProductUnitId) REFERENCES product_units (Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_product_barcodes_Barcode ON product_barcodes (Barcode);

CREATE TABLE IF NOT EXISTS inventory_cost_layers (
    Id INTEGER NOT NULL CONSTRAINT PK_inventory_cost_layers PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    InitialQuantityBase TEXT NOT NULL,
    RemainingQuantityBase TEXT NOT NULL,
    UnitCostBase TEXT NOT NULL,
    ReceivedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_inventory_cost_layers_ProductId_ReceivedAt ON inventory_cost_layers (ProductId, ReceivedAt);

CREATE TABLE IF NOT EXISTS stock_movements (
    Id INTEGER NOT NULL CONSTRAINT PK_stock_movements PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    ActorUserId INTEGER NULL,
    MovementType TEXT NOT NULL,
    QuantityBase TEXT NOT NULL,
    BalanceAfter TEXT NOT NULL,
    UnitCostBase TEXT NULL,
    ReferenceType TEXT NULL,
    ReferenceId INTEGER NULL,
    Notes TEXT NULL,
    OccurredAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_stock_movements_ProductId_OccurredAt ON stock_movements (ProductId, OccurredAt);

CREATE TABLE IF NOT EXISTS cashier_shifts (
    Id INTEGER NOT NULL CONSTRAINT PK_cashier_shifts PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Status TEXT NOT NULL,
    OpeningCash TEXT NOT NULL,
    OpenedAt TEXT NOT NULL,
    ClosedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_cashier_shifts_UserId_Status ON cashier_shifts (UserId, Status);

CREATE TABLE IF NOT EXISTS document_sequences (
    Key TEXT NOT NULL CONSTRAINT PK_document_sequences PRIMARY KEY,
    NextValue INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS sales (
    Id INTEGER NOT NULL CONSTRAINT PK_sales PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    RequestFingerprint TEXT NOT NULL,
    CashierUserId INTEGER NOT NULL,
    CashierShiftId INTEGER NULL,
    Status TEXT NOT NULL,
    PaymentStatus TEXT NOT NULL,
    CustomerNameSnapshot TEXT NOT NULL,
    Subtotal TEXT NOT NULL,
    LineDiscountTotal TEXT NOT NULL,
    SaleDiscountAmount TEXT NOT NULL,
    NetTotal TEXT NOT NULL,
    CogsTotal TEXT NOT NULL,
    GrossProfit TEXT NOT NULL,
    PaidAmount TEXT NOT NULL,
    ChangeAmount TEXT NOT NULL,
    SoldAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_sales_Number ON sales (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_sales_IdempotencyKey ON sales (IdempotencyKey);

CREATE TABLE IF NOT EXISTS sale_items (
    Id INTEGER NOT NULL CONSTRAINT PK_sale_items PRIMARY KEY AUTOINCREMENT,
    SaleId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductUnitId INTEGER NOT NULL,
    ProductNameSnapshot TEXT NOT NULL,
    SkuSnapshot TEXT NOT NULL,
    UnitNameSnapshot TEXT NOT NULL,
    Quantity TEXT NOT NULL,
    ConversionFactor TEXT NOT NULL,
    QuantityBase TEXT NOT NULL,
    UnitPrice TEXT NOT NULL,
    MinimumUnitPrice TEXT NULL,
    LineSubtotal TEXT NOT NULL,
    LineDiscountAmount TEXT NOT NULL,
    AllocatedSaleDiscount TEXT NOT NULL,
    LineNetTotal TEXT NOT NULL,
    CogsAmount TEXT NOT NULL,
    GrossProfit TEXT NOT NULL,
    CONSTRAINT FK_sale_items_sales_SaleId FOREIGN KEY (SaleId) REFERENCES sales (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS sale_payments (
    Id INTEGER NOT NULL CONSTRAINT PK_sale_payments PRIMARY KEY AUTOINCREMENT,
    SaleId INTEGER NOT NULL,
    MethodCode TEXT NOT NULL,
    Amount TEXT NOT NULL,
    Reference TEXT NULL,
    PaidAt TEXT NOT NULL,
    CONSTRAINT FK_sale_payments_sales_SaleId FOREIGN KEY (SaleId) REFERENCES sales (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS held_sales (
    Id INTEGER NOT NULL CONSTRAINT PK_held_sales PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    CashierUserId INTEGER NOT NULL,
    SaleDiscountAmount TEXT NOT NULL,
    Status TEXT NOT NULL,
    Notes TEXT NULL,
    HeldAt TEXT NOT NULL,
    ResumedAt TEXT NULL,
    ReleasedAt TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_held_sales_Number ON held_sales (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_held_sales_IdempotencyKey ON held_sales (IdempotencyKey);

CREATE TABLE IF NOT EXISTS held_sale_items (
    Id INTEGER NOT NULL CONSTRAINT PK_held_sale_items PRIMARY KEY AUTOINCREMENT,
    HeldSaleId INTEGER NOT NULL,
    ProductUnitId INTEGER NOT NULL,
    ProductNameSnapshot TEXT NOT NULL,
    SkuSnapshot TEXT NOT NULL,
    UnitNameSnapshot TEXT NOT NULL,
    Quantity TEXT NOT NULL,
    LineDiscountAmount TEXT NOT NULL,
    UnitPriceSnapshot TEXT NOT NULL,
    CONSTRAINT FK_held_sale_items_held_sales_HeldSaleId FOREIGN KEY (HeldSaleId) REFERENCES held_sales (Id) ON DELETE CASCADE
);
""";

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        const string catalogSql = """
CREATE TABLE IF NOT EXISTS categories (
    Id INTEGER NOT NULL CONSTRAINT PK_categories PRIMARY KEY AUTOINCREMENT,
    ParentId INTEGER NULL,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_categories_SortOrder_NameEn ON categories (SortOrder, NameEn);

CREATE TABLE IF NOT EXISTS brands (
    Id INTEGER NOT NULL CONSTRAINT PK_brands PRIMARY KEY AUTOINCREMENT,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_brands_NameEn ON brands (NameEn);
""";
        await context.Database.ExecuteSqlRawAsync(catalogSql, cancellationToken);

        await EnsureColumnAsync(context, "products", "CategoryId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "BrandId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "BaseUnitId", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(context, "products", "DescriptionEn", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "DescriptionFa", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "DescriptionPs", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "ShelfLocation", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "ImagePath", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "WholesalePrice", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "products", "ReorderQuantity", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "product_units", "WholesalePrice", "TEXT NULL", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
UPDATE products
SET BaseUnitId = COALESCE(
    (SELECT UnitId FROM product_units WHERE ProductId = products.Id ORDER BY Id LIMIT 1),
    (SELECT Id FROM units ORDER BY Id LIMIT 1),
    0
)
WHERE BaseUnitId = 0;
""", cancellationToken);

        const string inventorySql = """
CREATE TABLE IF NOT EXISTS product_batches (
    Id INTEGER NOT NULL CONSTRAINT PK_product_batches PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    BatchNumber TEXT NOT NULL,
    ManufacturedAt TEXT NULL,
    ExpiresAt TEXT NULL,
    StockOnHand TEXT NOT NULL DEFAULT '0',
    IsBlocked INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_product_batches_ProductId_BatchNumber ON product_batches (ProductId, BatchNumber);

CREATE TABLE IF NOT EXISTS stock_counts (
    Id INTEGER NOT NULL CONSTRAINT PK_stock_counts PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    ApprovalIdempotencyKey TEXT NULL,
    CountedByUserId INTEGER NOT NULL,
    ApprovedByUserId INTEGER NULL,
    Status TEXT NOT NULL,
    CountedAt TEXT NOT NULL,
    ApprovedAt TEXT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_stock_counts_Number ON stock_counts (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_stock_counts_IdempotencyKey ON stock_counts (IdempotencyKey);

CREATE TABLE IF NOT EXISTS stock_count_items (
    Id INTEGER NOT NULL CONSTRAINT PK_stock_count_items PRIMARY KEY AUTOINCREMENT,
    StockCountId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductBatchId INTEGER NULL,
    ExpectedQuantityBase TEXT NOT NULL,
    PhysicalQuantityBase TEXT NOT NULL,
    VarianceQuantityBase TEXT NOT NULL,
    StockMovementId INTEGER NULL,
    CostAmount TEXT NOT NULL DEFAULT '0',
    CONSTRAINT FK_stock_count_items_stock_counts_StockCountId FOREIGN KEY (StockCountId) REFERENCES stock_counts (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS inventory_writeoffs (
    Id INTEGER NOT NULL CONSTRAINT PK_inventory_writeoffs PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    WriteoffType TEXT NOT NULL,
    PostedByUserId INTEGER NOT NULL,
    Reason TEXT NOT NULL,
    TotalCost TEXT NOT NULL DEFAULT '0',
    PostedAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_writeoffs_Number ON inventory_writeoffs (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_inventory_writeoffs_IdempotencyKey ON inventory_writeoffs (IdempotencyKey);

CREATE TABLE IF NOT EXISTS inventory_writeoff_items (
    Id INTEGER NOT NULL CONSTRAINT PK_inventory_writeoff_items PRIMARY KEY AUTOINCREMENT,
    InventoryWriteoffId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductBatchId INTEGER NULL,
    StockMovementId INTEGER NOT NULL,
    QuantityBase TEXT NOT NULL,
    CostAmount TEXT NOT NULL,
    CONSTRAINT FK_inventory_writeoff_items_inventory_writeoffs_InventoryWriteoffId FOREIGN KEY (InventoryWriteoffId) REFERENCES inventory_writeoffs (Id) ON DELETE CASCADE
);
""";
        await context.Database.ExecuteSqlRawAsync(inventorySql, cancellationToken);

        await EnsureColumnAsync(context, "stock_movements", "ProductBatchId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "SourceUnitId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "SourceQuantity", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "ConversionFactor", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "BatchBalanceAfter", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "SourceUnitCost", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "IdempotencyKey", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "inventory_cost_layers", "ProductBatchId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "inventory_cost_layers", "SourceStockMovementId", "INTEGER NULL", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
CREATE UNIQUE INDEX IF NOT EXISTS IX_stock_movements_IdempotencyKey
ON stock_movements (IdempotencyKey)
WHERE IdempotencyKey IS NOT NULL;
""", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        PosDbContext context,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA table_info(" + table + ");";
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            var exists = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            await reader.DisposeAsync();
            if (!exists)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition + ";";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
