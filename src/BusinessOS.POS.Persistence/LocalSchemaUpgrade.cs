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

        await EnsureColumnAsync(context, "sale_payments", "TenderedAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "ChangeAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "Notes", "TEXT NULL", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
UPDATE sale_payments
SET TenderedAmount = Amount
WHERE TenderedAmount = '0';
""", cancellationToken);


        const string cashSql = """
CREATE TABLE IF NOT EXISTS terminals (
    Id INTEGER NOT NULL CONSTRAINT PK_terminals PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_terminals_Code ON terminals (Code);

CREATE TABLE IF NOT EXISTS expense_categories (
    Id INTEGER NOT NULL CONSTRAINT PK_expense_categories PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    EntryType TEXT NOT NULL,
    NameEn TEXT NOT NULL,
    NameFa TEXT NULL,
    NamePs TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    SortOrder INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_expense_categories_Code ON expense_categories (Code);

CREATE TABLE IF NOT EXISTS operating_entries (
    Id INTEGER NOT NULL CONSTRAINT PK_operating_entries PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    ExpenseCategoryId INTEGER NOT NULL,
    PaymentMethodId INTEGER NOT NULL,
    RecordedByUserId INTEGER NOT NULL,
    EntryType TEXT NOT NULL,
    Amount TEXT NOT NULL,
    Reference TEXT NULL,
    Description TEXT NULL,
    OccurredAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_operating_entries_Number ON operating_entries (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_operating_entries_IdempotencyKey ON operating_entries (IdempotencyKey);

CREATE TABLE IF NOT EXISTS cash_movements (
    Id INTEGER NOT NULL CONSTRAINT PK_cash_movements PRIMARY KEY AUTOINCREMENT,
    IdempotencyKey TEXT NOT NULL,
    CashierShiftId INTEGER NOT NULL,
    TerminalId INTEGER NOT NULL,
    ActorUserId INTEGER NULL,
    MovementType TEXT NOT NULL,
    Direction TEXT NOT NULL,
    Amount TEXT NOT NULL,
    ExpectedCashAfter TEXT NOT NULL,
    SourceType TEXT NULL,
    SourceId INTEGER NULL,
    ReferenceNumber TEXT NULL,
    Reason TEXT NULL,
    OccurredAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_cash_movements_IdempotencyKey ON cash_movements (IdempotencyKey);
CREATE INDEX IF NOT EXISTS IX_cash_movements_shift_id ON cash_movements (CashierShiftId, Id);
CREATE UNIQUE INDEX IF NOT EXISTS IX_cash_movements_unique_source
ON cash_movements (SourceType, SourceId, MovementType);

CREATE TABLE IF NOT EXISTS cashier_shift_closures (
    Id INTEGER NOT NULL CONSTRAINT PK_cashier_shift_closures PRIMARY KEY AUTOINCREMENT,
    IdempotencyKey TEXT NOT NULL,
    CashierShiftId INTEGER NOT NULL,
    Version INTEGER NOT NULL,
    ClosedByUserId INTEGER NOT NULL,
    ExpectedCash TEXT NOT NULL,
    ActualCash TEXT NOT NULL,
    Variance TEXT NOT NULL,
    Tolerance TEXT NOT NULL,
    WithinTolerance INTEGER NOT NULL,
    VarianceReason TEXT NULL,
    ClosingNotes TEXT NULL,
    ClosedAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_cashier_shift_closures_IdempotencyKey ON cashier_shift_closures (IdempotencyKey);
CREATE UNIQUE INDEX IF NOT EXISTS IX_cashier_shift_closures_shift_version
ON cashier_shift_closures (CashierShiftId, Version);
""";
        await context.Database.ExecuteSqlRawAsync(cashSql, cancellationToken);

        await EnsureColumnAsync(context, "cashier_shifts", "TerminalId", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "BusinessDate", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "OpenIdempotencyKey", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ExpectedCash", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ActualCash", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "Variance", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "VarianceWithinTolerance", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "VarianceReason", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ClosingNotes", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ClosedByUserId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ReopenedAt", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "cashier_shifts", "ReopenedByUserId", "INTEGER NULL", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
CREATE UNIQUE INDEX IF NOT EXISTS IX_cashier_shifts_OpenIdempotencyKey
ON cashier_shifts (OpenIdempotencyKey)
WHERE OpenIdempotencyKey IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_cashier_shifts_TerminalId_Status ON cashier_shifts (TerminalId, Status);
UPDATE cashier_shifts
SET ExpectedCash = OpeningCash
WHERE CAST(ExpectedCash AS REAL) = 0 AND CAST(OpeningCash AS REAL) <> 0;
UPDATE cashier_shifts
SET BusinessDate = substr(OpenedAt, 1, 10)
WHERE BusinessDate IS NULL;
""", cancellationToken);

        const string customerSalesSql = """
CREATE TABLE IF NOT EXISTS customers (
    Id INTEGER NOT NULL CONSTRAINT PK_customers PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT NULL,
    AlternatePhone TEXT NULL,
    Address TEXT NULL,
    CreditLimit TEXT NOT NULL DEFAULT '0',
    OpeningBalance TEXT NOT NULL DEFAULT '0',
    CurrentBalance TEXT NOT NULL DEFAULT '0',
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_customers_Name ON customers (Name);

CREATE TABLE IF NOT EXISTS customer_ledger_entries (
    Id INTEGER NOT NULL CONSTRAINT PK_customer_ledger_entries PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    ActorUserId INTEGER NULL,
    EntryType TEXT NOT NULL,
    Debit TEXT NOT NULL DEFAULT '0',
    Credit TEXT NOT NULL DEFAULT '0',
    BalanceAfter TEXT NOT NULL,
    ReferenceType TEXT NOT NULL,
    ReferenceId INTEGER NOT NULL,
    ReferenceNumber TEXT NULL,
    OccurredAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_customer_ledger_unique
ON customer_ledger_entries (CustomerId, EntryType, ReferenceType, ReferenceId);
CREATE INDEX IF NOT EXISTS IX_customer_ledger_customer_id
ON customer_ledger_entries (CustomerId, Id);

CREATE TABLE IF NOT EXISTS customer_collections (
    Id INTEGER NOT NULL CONSTRAINT PK_customer_collections PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    PaymentMethodCode TEXT NOT NULL,
    RecordedByUserId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    TenderedAmount TEXT NOT NULL,
    ChangeAmount TEXT NOT NULL,
    Reference TEXT NULL,
    CollectedAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_customer_collections_Number ON customer_collections (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_customer_collections_IdempotencyKey ON customer_collections (IdempotencyKey);

CREATE TABLE IF NOT EXISTS customer_collection_allocations (
    Id INTEGER NOT NULL CONSTRAINT PK_customer_collection_allocations PRIMARY KEY AUTOINCREMENT,
    CustomerCollectionId INTEGER NOT NULL,
    SaleId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    CONSTRAINT FK_customer_collection_allocations_collections FOREIGN KEY (CustomerCollectionId) REFERENCES customer_collections (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_customer_collection_allocation_unique
ON customer_collection_allocations (CustomerCollectionId, SaleId);

CREATE TABLE IF NOT EXISTS sale_returns (
    Id INTEGER NOT NULL CONSTRAINT PK_sale_returns PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    SaleId INTEGER NOT NULL,
    CreatedByUserId INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Status TEXT NOT NULL,
    Reason TEXT NOT NULL,
    ReturnTotal TEXT NOT NULL,
    CogsReversed TEXT NOT NULL,
    ReceivableReversed TEXT NOT NULL,
    RefundTotal TEXT NOT NULL,
    PostedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_sale_returns_Number ON sale_returns (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_sale_returns_IdempotencyKey ON sale_returns (IdempotencyKey);

CREATE TABLE IF NOT EXISTS sale_return_items (
    Id INTEGER NOT NULL CONSTRAINT PK_sale_return_items PRIMARY KEY AUTOINCREMENT,
    SaleReturnId INTEGER NOT NULL,
    SaleItemId INTEGER NOT NULL,
    Quantity TEXT NOT NULL,
    QuantityBase TEXT NOT NULL,
    ReturnAmount TEXT NOT NULL,
    CogsAmount TEXT NOT NULL,
    CONSTRAINT FK_sale_return_items_returns FOREIGN KEY (SaleReturnId) REFERENCES sale_returns (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS sale_refunds (
    Id INTEGER NOT NULL CONSTRAINT PK_sale_refunds PRIMARY KEY AUTOINCREMENT,
    IdempotencyKey TEXT NOT NULL,
    SaleReturnId INTEGER NOT NULL,
    PaymentMethodCode TEXT NOT NULL,
    RecordedByUserId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    Reference TEXT NULL,
    RefundedAt TEXT NOT NULL,
    Notes TEXT NULL,
    CONSTRAINT FK_sale_refunds_returns FOREIGN KEY (SaleReturnId) REFERENCES sale_returns (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_sale_refunds_IdempotencyKey ON sale_refunds (IdempotencyKey);
""";
        await context.Database.ExecuteSqlRawAsync(customerSalesSql, cancellationToken);

        await EnsureColumnAsync(context, "sales", "CustomerId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "sales", "BalanceDue", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sales", "ReturnedTotal", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sales", "ReceivableReversedTotal", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sales", "RefundedTotal", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
        await EnsureColumnAsync(context, "sales", "SettlementFinalizedAt", "TEXT NULL", cancellationToken);

        await EnsureColumnAsync(context, "sale_payments", "CustomerId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "RecordedByUserId", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "IdempotencyKey", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "SourceType", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(context, "sale_payments", "SourceId", "INTEGER NULL", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
CREATE UNIQUE INDEX IF NOT EXISTS IX_sale_payments_IdempotencyKey
ON sale_payments (IdempotencyKey)
WHERE IdempotencyKey IS NOT NULL;
""", cancellationToken);

        await EnsureColumnAsync(context, "held_sales", "CustomerId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(context, "held_sales", "CustomerNameSnapshot", "TEXT NOT NULL DEFAULT 'Walk-in Customer'", cancellationToken);
        await EnsureColumnAsync(context, "stock_movements", "SaleItemId", "INTEGER NULL", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
CREATE INDEX IF NOT EXISTS IX_stock_movements_SaleItemId ON stock_movements (SaleItemId);
""", cancellationToken);


        const string purchasingSql = """
CREATE TABLE IF NOT EXISTS suppliers (
    Id INTEGER NOT NULL CONSTRAINT PK_suppliers PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ContactPerson TEXT NULL,
    Phone TEXT NULL,
    AlternatePhone TEXT NULL,
    Address TEXT NULL,
    OpeningBalance TEXT NOT NULL DEFAULT '0',
    CurrentBalance TEXT NOT NULL DEFAULT '0',
    Notes TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_suppliers_Name ON suppliers (Name);

CREATE TABLE IF NOT EXISTS supplier_ledger_entries (
    Id INTEGER NOT NULL CONSTRAINT PK_supplier_ledger_entries PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    ActorUserId INTEGER NULL,
    EntryType TEXT NOT NULL,
    Debit TEXT NOT NULL DEFAULT '0',
    Credit TEXT NOT NULL DEFAULT '0',
    BalanceAfter TEXT NOT NULL,
    ReferenceType TEXT NOT NULL,
    ReferenceId INTEGER NOT NULL,
    ReferenceNumber TEXT NULL,
    OccurredAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_supplier_ledger_unique
ON supplier_ledger_entries (SupplierId, EntryType, ReferenceType, ReferenceId);
CREATE INDEX IF NOT EXISTS IX_supplier_ledger_supplier_id
ON supplier_ledger_entries (SupplierId, Id);

CREATE TABLE IF NOT EXISTS purchase_orders (
    Id INTEGER NOT NULL CONSTRAINT PK_purchase_orders PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    SupplierId INTEGER NOT NULL,
    CreatedByUserId INTEGER NOT NULL,
    ApprovedByUserId INTEGER NULL,
    Status TEXT NOT NULL,
    OrderDate TEXT NOT NULL,
    ExpectedDate TEXT NULL,
    SupplierReference TEXT NULL,
    Subtotal TEXT NOT NULL,
    LineDiscountTotal TEXT NOT NULL,
    OrderDiscountAmount TEXT NOT NULL,
    NetTotal TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    ApprovedAt TEXT NULL,
    CancelledAt TEXT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_purchase_orders_Number ON purchase_orders (Number);
CREATE INDEX IF NOT EXISTS IX_purchase_orders_supplier_id ON purchase_orders (SupplierId, Id);

CREATE TABLE IF NOT EXISTS purchase_order_items (
    Id INTEGER NOT NULL CONSTRAINT PK_purchase_order_items PRIMARY KEY AUTOINCREMENT,
    PurchaseOrderId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductUnitId INTEGER NOT NULL,
    OrderedQuantity TEXT NOT NULL,
    ReceivedQuantity TEXT NOT NULL DEFAULT '0',
    UnitCost TEXT NOT NULL,
    LineSubtotal TEXT NOT NULL,
    LineDiscountAmount TEXT NOT NULL,
    LineNetTotal TEXT NOT NULL,
    Notes TEXT NULL,
    CONSTRAINT FK_purchase_order_items_orders FOREIGN KEY (PurchaseOrderId) REFERENCES purchase_orders (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS goods_receipts (
    Id INTEGER NOT NULL CONSTRAINT PK_goods_receipts PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    SupplierId INTEGER NOT NULL,
    PurchaseOrderId INTEGER NULL,
    CreatedByUserId INTEGER NOT NULL,
    PostedByUserId INTEGER NOT NULL,
    Status TEXT NOT NULL,
    SupplierInvoiceReference TEXT NULL,
    ReceivedAt TEXT NOT NULL,
    Subtotal TEXT NOT NULL,
    LineDiscountTotal TEXT NOT NULL,
    ReceiptDiscountAmount TEXT NOT NULL,
    ExpenseTotal TEXT NOT NULL,
    NetTotal TEXT NOT NULL,
    PaidAmount TEXT NOT NULL,
    BalanceDue TEXT NOT NULL,
    ReturnedTotal TEXT NOT NULL DEFAULT '0',
    PostedAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_goods_receipts_Number ON goods_receipts (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_goods_receipts_IdempotencyKey ON goods_receipts (IdempotencyKey);
CREATE INDEX IF NOT EXISTS IX_goods_receipts_supplier_id ON goods_receipts (SupplierId, Id);

CREATE TABLE IF NOT EXISTS goods_receipt_items (
    Id INTEGER NOT NULL CONSTRAINT PK_goods_receipt_items PRIMARY KEY AUTOINCREMENT,
    GoodsReceiptId INTEGER NOT NULL,
    PurchaseOrderItemId INTEGER NULL,
    ProductId INTEGER NOT NULL,
    ProductUnitId INTEGER NOT NULL,
    Quantity TEXT NOT NULL,
    ConversionFactor TEXT NOT NULL,
    QuantityBase TEXT NOT NULL,
    SourceUnitCost TEXT NOT NULL,
    LineSubtotal TEXT NOT NULL,
    LineDiscountAmount TEXT NOT NULL,
    AllocatedReceiptDiscount TEXT NOT NULL,
    AllocatedExpense TEXT NOT NULL,
    LandedTotal TEXT NOT NULL,
    SourceUnitLandedCost TEXT NOT NULL,
    BaseUnitLandedCost TEXT NOT NULL,
    BatchNumber TEXT NULL,
    ManufacturedAt TEXT NULL,
    ExpiresAt TEXT NULL,
    ProductBatchId INTEGER NULL,
    StockMovementId INTEGER NULL,
    InventoryCostLayerId INTEGER NULL,
    CONSTRAINT FK_goods_receipt_items_receipts FOREIGN KEY (GoodsReceiptId) REFERENCES goods_receipts (Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_goods_receipt_items_StockMovementId ON goods_receipt_items (StockMovementId);

CREATE TABLE IF NOT EXISTS goods_receipt_expenses (
    Id INTEGER NOT NULL CONSTRAINT PK_goods_receipt_expenses PRIMARY KEY AUTOINCREMENT,
    GoodsReceiptId INTEGER NOT NULL,
    Type TEXT NOT NULL,
    Description TEXT NULL,
    Amount TEXT NOT NULL,
    CONSTRAINT FK_goods_receipt_expenses_receipts FOREIGN KEY (GoodsReceiptId) REFERENCES goods_receipts (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS purchase_payments (
    Id INTEGER NOT NULL CONSTRAINT PK_purchase_payments PRIMARY KEY AUTOINCREMENT,
    GoodsReceiptId INTEGER NOT NULL,
    SupplierId INTEGER NOT NULL,
    RecordedByUserId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    MethodCode TEXT NOT NULL,
    Reference TEXT NULL,
    PaidAt TEXT NOT NULL,
    Notes TEXT NULL,
    CONSTRAINT FK_purchase_payments_receipts FOREIGN KEY (GoodsReceiptId) REFERENCES goods_receipts (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS supplier_payments (
    Id INTEGER NOT NULL CONSTRAINT PK_supplier_payments PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    SupplierId INTEGER NOT NULL,
    RecordedByUserId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    MethodCode TEXT NOT NULL,
    Reference TEXT NULL,
    PaidAt TEXT NOT NULL,
    Notes TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_supplier_payments_Number ON supplier_payments (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_supplier_payments_IdempotencyKey ON supplier_payments (IdempotencyKey);

CREATE TABLE IF NOT EXISTS supplier_payment_allocations (
    Id INTEGER NOT NULL CONSTRAINT PK_supplier_payment_allocations PRIMARY KEY AUTOINCREMENT,
    SupplierPaymentId INTEGER NOT NULL,
    GoodsReceiptId INTEGER NOT NULL,
    Amount TEXT NOT NULL,
    CONSTRAINT FK_supplier_payment_allocations_payments FOREIGN KEY (SupplierPaymentId) REFERENCES supplier_payments (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_supplier_payment_allocations_unique
ON supplier_payment_allocations (SupplierPaymentId, GoodsReceiptId);

CREATE TABLE IF NOT EXISTS purchase_returns (
    Id INTEGER NOT NULL CONSTRAINT PK_purchase_returns PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    GoodsReceiptId INTEGER NOT NULL,
    SupplierId INTEGER NOT NULL,
    CreatedByUserId INTEGER NOT NULL,
    Reason TEXT NOT NULL,
    ReturnTotal TEXT NOT NULL,
    PostedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_purchase_returns_Number ON purchase_returns (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_purchase_returns_IdempotencyKey ON purchase_returns (IdempotencyKey);

CREATE TABLE IF NOT EXISTS purchase_return_items (
    Id INTEGER NOT NULL CONSTRAINT PK_purchase_return_items PRIMARY KEY AUTOINCREMENT,
    PurchaseReturnId INTEGER NOT NULL,
    GoodsReceiptItemId INTEGER NOT NULL,
    InventoryCostLayerId INTEGER NOT NULL,
    StockMovementId INTEGER NOT NULL,
    Quantity TEXT NOT NULL,
    QuantityBase TEXT NOT NULL,
    ReturnAmount TEXT NOT NULL,
    UnitCostBase TEXT NOT NULL,
    CostAmount TEXT NOT NULL,
    CONSTRAINT FK_purchase_return_items_returns FOREIGN KEY (PurchaseReturnId) REFERENCES purchase_returns (Id) ON DELETE CASCADE
);
""";
        await context.Database.ExecuteSqlRawAsync(purchasingSql, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
INSERT OR IGNORE INTO terminals (Id, Code, Name, IsActive)
VALUES (1, 'COUNTER-1', 'Main Counter', 1);

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'shift:' || Id || ':opening', Id, TerminalId, UserId, 'opening_float', 'inflow',
       OpeningCash, OpeningCash, 'cashier_shift', Id, NULL, 'Opening float backfill.', OpenedAt, OpenedAt
FROM cashier_shifts;

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'sale-payment:' || sp.Id, s.CashierShiftId, cs.TerminalId, sp.RecordedByUserId,
       'cash_sale', 'inflow', sp.Amount, '0', 'sale_payment', sp.Id, s.Number,
       'Cash sale payment backfill.', sp.PaidAt, sp.PaidAt
FROM sale_payments sp
JOIN sales s ON s.Id = sp.SaleId
JOIN cashier_shifts cs ON cs.Id = s.CashierShiftId
WHERE sp.MethodCode = 'cash'
  AND s.CashierShiftId IS NOT NULL
  AND COALESCE(sp.SourceType, '') <> 'collection';

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'customer-collection:' || cc.Id,
       (SELECT cs.Id FROM cashier_shifts cs
        WHERE cs.UserId = cc.RecordedByUserId
          AND cc.CollectedAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR cc.CollectedAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       (SELECT cs.TerminalId FROM cashier_shifts cs
        WHERE cs.UserId = cc.RecordedByUserId
          AND cc.CollectedAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR cc.CollectedAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       cc.RecordedByUserId, 'customer_collection', 'inflow', cc.Amount, '0',
       'customer_collection', cc.Id, cc.Number, 'Cash customer collection backfill.',
       cc.CollectedAt, cc.CollectedAt
FROM customer_collections cc
WHERE cc.PaymentMethodCode = 'cash'
  AND EXISTS (
      SELECT 1 FROM cashier_shifts cs
      WHERE cs.UserId = cc.RecordedByUserId
        AND cc.CollectedAt >= cs.OpenedAt
        AND (cs.ClosedAt IS NULL OR cc.CollectedAt <= cs.ClosedAt));

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'purchase-payment:' || pp.Id,
       (SELECT cs.Id FROM cashier_shifts cs
        WHERE cs.UserId = pp.RecordedByUserId
          AND pp.PaidAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR pp.PaidAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       (SELECT cs.TerminalId FROM cashier_shifts cs
        WHERE cs.UserId = pp.RecordedByUserId
          AND pp.PaidAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR pp.PaidAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       pp.RecordedByUserId, 'purchase_payment', 'outflow', pp.Amount, '0',
       'purchase_payment', pp.Id, NULL, 'Initial cash purchase payment backfill.',
       pp.PaidAt, pp.PaidAt
FROM purchase_payments pp
WHERE pp.MethodCode = 'cash'
  AND EXISTS (
      SELECT 1 FROM cashier_shifts cs
      WHERE cs.UserId = pp.RecordedByUserId
        AND pp.PaidAt >= cs.OpenedAt
        AND (cs.ClosedAt IS NULL OR pp.PaidAt <= cs.ClosedAt));

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'supplier-payment:' || sp.Id,
       (SELECT cs.Id FROM cashier_shifts cs
        WHERE cs.UserId = sp.RecordedByUserId
          AND sp.PaidAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR sp.PaidAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       (SELECT cs.TerminalId FROM cashier_shifts cs
        WHERE cs.UserId = sp.RecordedByUserId
          AND sp.PaidAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR sp.PaidAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       sp.RecordedByUserId, 'supplier_payment', 'outflow', sp.Amount, '0',
       'supplier_payment', sp.Id, sp.Number, 'Cash supplier payment backfill.',
       sp.PaidAt, sp.PaidAt
FROM supplier_payments sp
WHERE sp.MethodCode = 'cash'
  AND EXISTS (
      SELECT 1 FROM cashier_shifts cs
      WHERE cs.UserId = sp.RecordedByUserId
        AND sp.PaidAt >= cs.OpenedAt
        AND (cs.ClosedAt IS NULL OR sp.PaidAt <= cs.ClosedAt));

INSERT OR IGNORE INTO cash_movements
(IdempotencyKey, CashierShiftId, TerminalId, ActorUserId, MovementType, Direction, Amount,
 ExpectedCashAfter, SourceType, SourceId, ReferenceNumber, Reason, OccurredAt, CreatedAt)
SELECT 'sale-refund:' || sr.Id,
       (SELECT cs.Id FROM cashier_shifts cs
        WHERE cs.UserId = sr.RecordedByUserId
          AND sr.RefundedAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR sr.RefundedAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       (SELECT cs.TerminalId FROM cashier_shifts cs
        WHERE cs.UserId = sr.RecordedByUserId
          AND sr.RefundedAt >= cs.OpenedAt
          AND (cs.ClosedAt IS NULL OR sr.RefundedAt <= cs.ClosedAt)
        ORDER BY cs.Id DESC LIMIT 1),
       sr.RecordedByUserId, 'sale_refund', 'outflow', sr.Amount, '0',
       'sale_refund', sr.Id, NULL, 'Cash sale refund backfill.',
       sr.RefundedAt, sr.RefundedAt
FROM sale_refunds sr
WHERE sr.PaymentMethodCode = 'cash'
  AND EXISTS (
      SELECT 1 FROM cashier_shifts cs
      WHERE cs.UserId = sr.RecordedByUserId
        AND sr.RefundedAt >= cs.OpenedAt
        AND (cs.ClosedAt IS NULL OR sr.RefundedAt <= cs.ClosedAt));

WITH running AS (
    SELECT Id,
           ROUND(SUM(CASE WHEN Direction = 'inflow' THEN CAST(Amount AS REAL) ELSE -CAST(Amount AS REAL) END)
                 OVER (PARTITION BY CashierShiftId ORDER BY OccurredAt, Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW), 2) AS Expected
    FROM cash_movements
)
UPDATE cash_movements
SET ExpectedCashAfter = CAST((SELECT Expected FROM running WHERE running.Id = cash_movements.Id) AS TEXT);

UPDATE cashier_shifts
SET ExpectedCash = COALESCE((
    SELECT cm.ExpectedCashAfter
    FROM cash_movements cm
    WHERE cm.CashierShiftId = cashier_shifts.Id
    ORDER BY cm.OccurredAt DESC, cm.Id DESC
    LIMIT 1
), OpeningCash);
""", cancellationToken);

        await EnsureColumnAsync(context, "sales", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "sale_returns", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "customer_collections", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "goods_receipts", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "purchase_returns", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "purchase_payments", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "supplier_payments", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);
        await EnsureColumnAsync(context, "operating_entries", "BusinessDate", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
UPDATE sales SET BusinessDate = substr(SoldAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE sale_returns SET BusinessDate = substr(PostedAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE customer_collections SET BusinessDate = substr(CollectedAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE goods_receipts SET BusinessDate = substr(ReceivedAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE purchase_returns SET BusinessDate = substr(PostedAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE purchase_payments SET BusinessDate = substr(PaidAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE supplier_payments SET BusinessDate = substr(PaidAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';
UPDATE operating_entries SET BusinessDate = substr(OccurredAt, 1, 10) || ' 00:00:00' WHERE BusinessDate LIKE '0001-01-01%';

CREATE INDEX IF NOT EXISTS IX_sales_BusinessDate ON sales (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_sale_returns_BusinessDate ON sale_returns (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_customer_collections_BusinessDate ON customer_collections (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_goods_receipts_BusinessDate ON goods_receipts (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_purchase_returns_BusinessDate ON purchase_returns (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_purchase_payments_BusinessDate ON purchase_payments (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_supplier_payments_BusinessDate ON supplier_payments (BusinessDate);
CREATE INDEX IF NOT EXISTS IX_operating_entries_BusinessDate ON operating_entries (BusinessDate);
""", cancellationToken);

        const string closingSql = """
CREATE TABLE IF NOT EXISTS business_days (
    Id INTEGER NOT NULL CONSTRAINT PK_business_days PRIMARY KEY AUTOINCREMENT,
    BusinessDate TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'open',
    ClosedAt TEXT NULL,
    ClosedByUserId INTEGER NULL,
    ReopenedAt TEXT NULL,
    ReopenedByUserId INTEGER NULL,
    ReopenReason TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_business_days_BusinessDate ON business_days (BusinessDate);

CREATE TABLE IF NOT EXISTS business_day_closures (
    Id INTEGER NOT NULL CONSTRAINT PK_business_day_closures PRIMARY KEY AUTOINCREMENT,
    BusinessDayId INTEGER NOT NULL,
    IdempotencyKey TEXT NOT NULL,
    Number TEXT NOT NULL,
    Version INTEGER NOT NULL,
    ClosedByUserId INTEGER NOT NULL,
    ShiftCount INTEGER NOT NULL,
    SalesCount INTEGER NOT NULL,
    SalesSubtotal TEXT NOT NULL,
    SalesLineDiscountTotal TEXT NOT NULL,
    SalesDiscountTotal TEXT NOT NULL,
    SalesNetTotal TEXT NOT NULL,
    SalesReturnTotal TEXT NOT NULL,
    NetSalesTotal TEXT NOT NULL,
    SalesCogsTotal TEXT NOT NULL,
    CogsReversedTotal TEXT NOT NULL,
    NetCogsTotal TEXT NOT NULL,
    GrossProfitTotal TEXT NOT NULL,
    CustomerCollectionsTotal TEXT NOT NULL,
    PurchasesTotal TEXT NOT NULL,
    PurchaseReturnsTotal TEXT NOT NULL,
    SupplierPaymentsTotal TEXT NOT NULL,
    OperatingExpensesTotal TEXT NOT NULL,
    OtherIncomeTotal TEXT NOT NULL,
    NetProfitTotal TEXT NOT NULL,
    OpeningCashTotal TEXT NOT NULL,
    CashInflowTotal TEXT NOT NULL,
    CashOutflowTotal TEXT NOT NULL,
    ExpectedCashTotal TEXT NOT NULL,
    ActualCashTotal TEXT NOT NULL,
    VarianceTotal TEXT NOT NULL,
    CashBreakdownJson TEXT NULL,
    Notes TEXT NULL,
    ClosedAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_business_day_closures_IdempotencyKey
ON business_day_closures (IdempotencyKey);
CREATE UNIQUE INDEX IF NOT EXISTS IX_business_day_closures_Number
ON business_day_closures (Number);
CREATE UNIQUE INDEX IF NOT EXISTS IX_business_day_closures_day_version
ON business_day_closures (BusinessDayId, Version);

INSERT OR IGNORE INTO business_days (BusinessDate, Status)
SELECT BusinessDate, 'open'
FROM (
    SELECT BusinessDate FROM cashier_shifts
    UNION SELECT BusinessDate FROM sales
    UNION SELECT BusinessDate FROM sale_returns
    UNION SELECT BusinessDate FROM customer_collections
    UNION SELECT BusinessDate FROM goods_receipts
    UNION SELECT BusinessDate FROM purchase_returns
    UNION SELECT BusinessDate FROM purchase_payments
    UNION SELECT BusinessDate FROM supplier_payments
    UNION SELECT BusinessDate FROM operating_entries
)
WHERE BusinessDate IS NOT NULL
  AND BusinessDate NOT LIKE '0001-01-01%';
""";
        await context.Database.ExecuteSqlRawAsync(closingSql, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
UPDATE sales
SET BalanceDue = CASE
    WHEN CAST(NetTotal AS REAL) > CAST(PaidAmount AS REAL)
    THEN CAST(CAST(NetTotal AS REAL) - CAST(PaidAmount AS REAL) AS TEXT)
    ELSE '0'
END
WHERE BalanceDue = '0';
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
