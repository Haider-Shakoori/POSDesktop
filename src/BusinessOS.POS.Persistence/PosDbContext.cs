using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<BrandEntity> Brands => Set<BrandEntity>();
    public DbSet<UnitEntity> Units => Set<UnitEntity>();
    public DbSet<PaymentMethodEntity> PaymentMethods => Set<PaymentMethodEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<CustomerLedgerEntryEntity> CustomerLedgerEntries => Set<CustomerLedgerEntryEntity>();
    public DbSet<CustomerCollectionEntity> CustomerCollections => Set<CustomerCollectionEntity>();
    public DbSet<CustomerCollectionAllocationEntity> CustomerCollectionAllocations => Set<CustomerCollectionAllocationEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<ProductUnitEntity> ProductUnits => Set<ProductUnitEntity>();
    public DbSet<ProductBarcodeEntity> ProductBarcodes => Set<ProductBarcodeEntity>();
    public DbSet<InventoryCostLayerEntity> InventoryCostLayers => Set<InventoryCostLayerEntity>();
    public DbSet<ProductBatchEntity> ProductBatches => Set<ProductBatchEntity>();
    public DbSet<StockMovementEntity> StockMovements => Set<StockMovementEntity>();
    public DbSet<StockCountEntity> StockCounts => Set<StockCountEntity>();
    public DbSet<StockCountItemEntity> StockCountItems => Set<StockCountItemEntity>();
    public DbSet<InventoryWriteoffEntity> InventoryWriteoffs => Set<InventoryWriteoffEntity>();
    public DbSet<InventoryWriteoffItemEntity> InventoryWriteoffItems => Set<InventoryWriteoffItemEntity>();
    public DbSet<CashierShiftEntity> CashierShifts => Set<CashierShiftEntity>();
    public DbSet<DocumentSequenceEntity> DocumentSequences => Set<DocumentSequenceEntity>();
    public DbSet<SaleEntity> Sales => Set<SaleEntity>();
    public DbSet<SaleItemEntity> SaleItems => Set<SaleItemEntity>();
    public DbSet<SalePaymentEntity> SalePayments => Set<SalePaymentEntity>();
    public DbSet<HeldSaleEntity> HeldSales => Set<HeldSaleEntity>();
    public DbSet<HeldSaleItemEntity> HeldSaleItems => Set<HeldSaleItemEntity>();
    public DbSet<SaleReturnEntity> SaleReturns => Set<SaleReturnEntity>();
    public DbSet<SaleReturnItemEntity> SaleReturnItems => Set<SaleReturnItemEntity>();
    public DbSet<SaleRefundEntity> SaleRefunds => Set<SaleRefundEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<UserEntity>();
        user.ToTable("users");
        user.HasKey(x => x.Id);
        user.Property(x => x.Name).HasMaxLength(120).IsRequired();
        user.Property(x => x.Username).HasMaxLength(100).IsRequired();
        user.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        user.Property(x => x.Email).HasMaxLength(180);
        user.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        user.Property(x => x.PreferredLocale).HasMaxLength(5).IsRequired();

        var role = modelBuilder.Entity<RoleEntity>();
        role.ToTable("roles");
        role.HasKey(x => x.Id);
        role.Property(x => x.Name).HasMaxLength(100).IsRequired();
        role.Property(x => x.Label).HasMaxLength(120).IsRequired();
        role.HasIndex(x => x.Name).IsUnique();

        var permission = modelBuilder.Entity<PermissionEntity>();
        permission.ToTable("permissions");
        permission.HasKey(x => x.Id);
        permission.Property(x => x.Name).HasMaxLength(120).IsRequired();
        permission.Property(x => x.Label).HasMaxLength(180).IsRequired();
        permission.HasIndex(x => x.Name).IsUnique();

        user.HasMany(x => x.Roles).WithMany(x => x.Users).UsingEntity("user_roles");
        role.HasMany(x => x.Permissions).WithMany(x => x.Roles).UsingEntity("role_permissions");

        var audit = modelBuilder.Entity<AuditLogEntity>();
        audit.ToTable("audit_logs");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Event).HasMaxLength(120).IsRequired();
        audit.HasIndex(x => x.CreatedAt);
        audit.HasIndex(x => x.ActorUserId);

        var category = modelBuilder.Entity<CategoryEntity>();
        category.ToTable("categories");
        category.HasKey(x => x.Id);
        category.Property(x => x.NameEn).HasMaxLength(160).IsRequired();
        category.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        category.HasIndex(x => new { x.SortOrder, x.NameEn });

        var brand = modelBuilder.Entity<BrandEntity>();
        brand.ToTable("brands");
        brand.HasKey(x => x.Id);
        brand.Property(x => x.NameEn).HasMaxLength(160).IsRequired();
        brand.HasIndex(x => x.NameEn).IsUnique();

        var unit = modelBuilder.Entity<UnitEntity>();
        unit.ToTable("units");
        unit.HasKey(x => x.Id);
        unit.Property(x => x.Code).HasMaxLength(30).IsRequired();
        unit.HasIndex(x => x.Code).IsUnique();

        var paymentMethod = modelBuilder.Entity<PaymentMethodEntity>();
        paymentMethod.ToTable("payment_methods");
        paymentMethod.HasKey(x => x.Id);
        paymentMethod.Property(x => x.Code).HasMaxLength(40).IsRequired();
        paymentMethod.HasIndex(x => x.Code).IsUnique();

        var customer = modelBuilder.Entity<CustomerEntity>();
        customer.ToTable("customers");
        customer.HasKey(x => x.Id);
        customer.Property(x => x.Name).HasMaxLength(180).IsRequired();
        customer.Property(x => x.Phone).HasMaxLength(50);
        customer.Property(x => x.AlternatePhone).HasMaxLength(50);
        customer.Property(x => x.CreditLimit).HasPrecision(18, 2);
        customer.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        customer.Property(x => x.CurrentBalance).HasPrecision(18, 2);
        customer.HasIndex(x => x.Name);

        var ledger = modelBuilder.Entity<CustomerLedgerEntryEntity>();
        ledger.ToTable("customer_ledger_entries");
        ledger.HasKey(x => x.Id);
        ledger.Property(x => x.EntryType).HasMaxLength(40).IsRequired();
        ledger.Property(x => x.Debit).HasPrecision(18, 2);
        ledger.Property(x => x.Credit).HasPrecision(18, 2);
        ledger.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        ledger.HasIndex(x => new { x.CustomerId, x.EntryType, x.ReferenceType, x.ReferenceId }).IsUnique();
        ledger.HasIndex(x => new { x.CustomerId, x.Id });

        var collection = modelBuilder.Entity<CustomerCollectionEntity>();
        collection.ToTable("customer_collections");
        collection.HasKey(x => x.Id);
        collection.Property(x => x.Number).HasMaxLength(40).IsRequired();
        collection.HasIndex(x => x.Number).IsUnique();
        collection.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        collection.HasIndex(x => x.IdempotencyKey).IsUnique();
        collection.Property(x => x.Amount).HasPrecision(18, 2);
        collection.Property(x => x.TenderedAmount).HasPrecision(18, 2);
        collection.Property(x => x.ChangeAmount).HasPrecision(18, 2);
        collection.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.CustomerCollectionId).OnDelete(DeleteBehavior.Cascade);

        var collectionAllocation = modelBuilder.Entity<CustomerCollectionAllocationEntity>();
        collectionAllocation.ToTable("customer_collection_allocations");
        collectionAllocation.HasKey(x => x.Id);
        collectionAllocation.Property(x => x.Amount).HasPrecision(18, 2);
        collectionAllocation.HasIndex(x => new { x.CustomerCollectionId, x.SaleId }).IsUnique();

        var product = modelBuilder.Entity<ProductEntity>();
        product.ToTable("products");
        product.HasKey(x => x.Id);
        product.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        product.HasIndex(x => x.Sku).IsUnique();
        product.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        product.Property(x => x.ShelfLocation).HasMaxLength(100);
        product.Property(x => x.ImagePath).HasMaxLength(500);
        product.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        product.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
        product.HasOne(x => x.BaseUnit).WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
        product.Property(x => x.PurchaseCost).HasPrecision(18, 4);
        product.Property(x => x.SellingPrice).HasPrecision(18, 2);
        product.Property(x => x.MinimumSellingPrice).HasPrecision(18, 2);
        product.Property(x => x.WholesalePrice).HasPrecision(18, 2);
        product.Property(x => x.StockOnHand).HasPrecision(20, 6);
        product.Property(x => x.MinimumStock).HasPrecision(20, 6);
        product.Property(x => x.ReorderQuantity).HasPrecision(20, 6);

        var productUnit = modelBuilder.Entity<ProductUnitEntity>();
        productUnit.ToTable("product_units");
        productUnit.HasKey(x => x.Id);
        productUnit.Property(x => x.ConversionFactor).HasPrecision(20, 6);
        productUnit.Property(x => x.SellingPrice).HasPrecision(18, 2);
        productUnit.Property(x => x.MinimumSellingPrice).HasPrecision(18, 2);
        productUnit.Property(x => x.WholesalePrice).HasPrecision(18, 2);
        productUnit.HasIndex(x => new { x.ProductId, x.UnitId }).IsUnique();
        productUnit.HasOne(x => x.Product).WithMany(x => x.Units).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        productUnit.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);

        var barcode = modelBuilder.Entity<ProductBarcodeEntity>();
        barcode.ToTable("product_barcodes");
        barcode.HasKey(x => x.Id);
        barcode.Property(x => x.Barcode).HasMaxLength(191).IsRequired();
        barcode.HasIndex(x => x.Barcode).IsUnique();
        barcode.HasOne(x => x.Product).WithMany(x => x.Barcodes).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        barcode.HasOne(x => x.ProductUnit).WithMany(x => x.Barcodes).HasForeignKey(x => x.ProductUnitId).OnDelete(DeleteBehavior.Restrict);

        var batch = modelBuilder.Entity<ProductBatchEntity>();
        batch.ToTable("product_batches");
        batch.HasKey(x => x.Id);
        batch.Property(x => x.BatchNumber).HasMaxLength(100).IsRequired();
        batch.Property(x => x.StockOnHand).HasPrecision(20, 6);
        batch.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        batch.HasIndex(x => new { x.ProductId, x.BatchNumber }).IsUnique();

        var layer = modelBuilder.Entity<InventoryCostLayerEntity>();
        layer.ToTable("inventory_cost_layers");
        layer.HasKey(x => x.Id);
        layer.Property(x => x.InitialQuantityBase).HasPrecision(20, 6);
        layer.Property(x => x.RemainingQuantityBase).HasPrecision(20, 6);
        layer.Property(x => x.UnitCostBase).HasPrecision(18, 4);
        layer.HasIndex(x => new { x.ProductId, x.Id });

        var move = modelBuilder.Entity<StockMovementEntity>();
        move.ToTable("stock_movements");
        move.HasKey(x => x.Id);
        move.Property(x => x.SourceQuantity).HasPrecision(20, 6);
        move.Property(x => x.ConversionFactor).HasPrecision(20, 6);
        move.Property(x => x.QuantityBase).HasPrecision(20, 6);
        move.Property(x => x.BalanceAfter).HasPrecision(20, 6);
        move.Property(x => x.BatchBalanceAfter).HasPrecision(20, 6);
        move.Property(x => x.SourceUnitCost).HasPrecision(18, 4);
        move.Property(x => x.UnitCostBase).HasPrecision(18, 4);
        move.Property(x => x.IdempotencyKey).HasMaxLength(64);
        move.HasIndex(x => x.IdempotencyKey).IsUnique();
        move.HasIndex(x => new { x.ProductId, x.Id });
        move.HasIndex(x => x.SaleItemId);

        var stockCount = modelBuilder.Entity<StockCountEntity>();
        stockCount.ToTable("stock_counts");
        stockCount.HasKey(x => x.Id);
        stockCount.Property(x => x.Number).HasMaxLength(40).IsRequired();
        stockCount.HasIndex(x => x.Number).IsUnique();
        stockCount.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        stockCount.HasIndex(x => x.IdempotencyKey).IsUnique();
        stockCount.Property(x => x.ApprovalIdempotencyKey).HasMaxLength(64);
        stockCount.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);

        var stockCountItem = modelBuilder.Entity<StockCountItemEntity>();
        stockCountItem.ToTable("stock_count_items");
        stockCountItem.HasKey(x => x.Id);
        stockCountItem.Property(x => x.ExpectedQuantityBase).HasPrecision(20, 6);
        stockCountItem.Property(x => x.PhysicalQuantityBase).HasPrecision(20, 6);
        stockCountItem.Property(x => x.VarianceQuantityBase).HasPrecision(20, 6);
        stockCountItem.Property(x => x.CostAmount).HasPrecision(18, 4);

        var writeoff = modelBuilder.Entity<InventoryWriteoffEntity>();
        writeoff.ToTable("inventory_writeoffs");
        writeoff.HasKey(x => x.Id);
        writeoff.Property(x => x.Number).HasMaxLength(40).IsRequired();
        writeoff.HasIndex(x => x.Number).IsUnique();
        writeoff.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        writeoff.HasIndex(x => x.IdempotencyKey).IsUnique();
        writeoff.Property(x => x.TotalCost).HasPrecision(18, 4);
        writeoff.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.InventoryWriteoffId).OnDelete(DeleteBehavior.Cascade);

        var writeoffItem = modelBuilder.Entity<InventoryWriteoffItemEntity>();
        writeoffItem.ToTable("inventory_writeoff_items");
        writeoffItem.HasKey(x => x.Id);
        writeoffItem.Property(x => x.QuantityBase).HasPrecision(20, 6);
        writeoffItem.Property(x => x.CostAmount).HasPrecision(18, 4);

        var shift = modelBuilder.Entity<CashierShiftEntity>();
        shift.ToTable("cashier_shifts");
        shift.HasKey(x => x.Id);
        shift.Property(x => x.Status).HasMaxLength(20).IsRequired();
        shift.Property(x => x.OpeningCash).HasPrecision(18, 2);
        shift.HasIndex(x => new { x.UserId, x.Status });

        var sequence = modelBuilder.Entity<DocumentSequenceEntity>();
        sequence.ToTable("document_sequences");
        sequence.HasKey(x => x.Key);
        sequence.Property(x => x.Key).HasMaxLength(50);

        var sale = modelBuilder.Entity<SaleEntity>();
        sale.ToTable("sales");
        sale.HasKey(x => x.Id);
        sale.Property(x => x.Number).HasMaxLength(40).IsRequired();
        sale.HasIndex(x => x.Number).IsUnique();
        sale.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        sale.HasIndex(x => x.IdempotencyKey).IsUnique();
        sale.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        sale.Property(x => x.Subtotal).HasPrecision(18, 2);
        sale.Property(x => x.LineDiscountTotal).HasPrecision(18, 2);
        sale.Property(x => x.SaleDiscountAmount).HasPrecision(18, 2);
        sale.Property(x => x.NetTotal).HasPrecision(18, 2);
        sale.Property(x => x.CogsTotal).HasPrecision(18, 2);
        sale.Property(x => x.GrossProfit).HasPrecision(18, 2);
        sale.Property(x => x.PaidAmount).HasPrecision(18, 2);
        sale.Property(x => x.ChangeAmount).HasPrecision(18, 2);
        sale.Property(x => x.BalanceDue).HasPrecision(18, 2);
        sale.Property(x => x.ReturnedTotal).HasPrecision(18, 2);
        sale.Property(x => x.ReceivableReversedTotal).HasPrecision(18, 2);
        sale.Property(x => x.RefundedTotal).HasPrecision(18, 2);
        sale.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        sale.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);

        var saleItem = modelBuilder.Entity<SaleItemEntity>();
        saleItem.ToTable("sale_items");
        saleItem.HasKey(x => x.Id);
        saleItem.Property(x => x.Quantity).HasPrecision(20, 6);
        saleItem.Property(x => x.ConversionFactor).HasPrecision(20, 6);
        saleItem.Property(x => x.QuantityBase).HasPrecision(20, 6);
        saleItem.Property(x => x.UnitPrice).HasPrecision(18, 2);
        saleItem.Property(x => x.MinimumUnitPrice).HasPrecision(18, 2);
        saleItem.Property(x => x.LineSubtotal).HasPrecision(18, 2);
        saleItem.Property(x => x.LineDiscountAmount).HasPrecision(18, 2);
        saleItem.Property(x => x.AllocatedSaleDiscount).HasPrecision(18, 2);
        saleItem.Property(x => x.LineNetTotal).HasPrecision(18, 2);
        saleItem.Property(x => x.CogsAmount).HasPrecision(18, 2);
        saleItem.Property(x => x.GrossProfit).HasPrecision(18, 2);

        var salePayment = modelBuilder.Entity<SalePaymentEntity>();
        salePayment.ToTable("sale_payments");
        salePayment.HasKey(x => x.Id);
        salePayment.Property(x => x.Amount).HasPrecision(18, 2);
        salePayment.Property(x => x.TenderedAmount).HasPrecision(18, 2);
        salePayment.Property(x => x.ChangeAmount).HasPrecision(18, 2);
        salePayment.Property(x => x.IdempotencyKey).HasMaxLength(100);
        salePayment.HasIndex(x => x.IdempotencyKey).IsUnique();

        var held = modelBuilder.Entity<HeldSaleEntity>();
        held.ToTable("held_sales");
        held.HasKey(x => x.Id);
        held.Property(x => x.Number).HasMaxLength(40).IsRequired();
        held.HasIndex(x => x.Number).IsUnique();
        held.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        held.HasIndex(x => x.IdempotencyKey).IsUnique();
        held.Property(x => x.SaleDiscountAmount).HasPrecision(18, 2);
        held.Property(x => x.CustomerNameSnapshot).HasMaxLength(180);
        held.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.HeldSaleId).OnDelete(DeleteBehavior.Cascade);

        var heldItem = modelBuilder.Entity<HeldSaleItemEntity>();
        heldItem.ToTable("held_sale_items");
        heldItem.HasKey(x => x.Id);
        heldItem.Property(x => x.Quantity).HasPrecision(20, 6);
        heldItem.Property(x => x.LineDiscountAmount).HasPrecision(18, 2);
        heldItem.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 2);

        var saleReturn = modelBuilder.Entity<SaleReturnEntity>();
        saleReturn.ToTable("sale_returns");
        saleReturn.HasKey(x => x.Id);
        saleReturn.Property(x => x.Number).HasMaxLength(40).IsRequired();
        saleReturn.HasIndex(x => x.Number).IsUnique();
        saleReturn.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        saleReturn.HasIndex(x => x.IdempotencyKey).IsUnique();
        saleReturn.Property(x => x.ReturnTotal).HasPrecision(18, 2);
        saleReturn.Property(x => x.CogsReversed).HasPrecision(18, 2);
        saleReturn.Property(x => x.ReceivableReversed).HasPrecision(18, 2);
        saleReturn.Property(x => x.RefundTotal).HasPrecision(18, 2);
        saleReturn.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        saleReturn.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.SaleReturnId).OnDelete(DeleteBehavior.Cascade);

        var saleReturnItem = modelBuilder.Entity<SaleReturnItemEntity>();
        saleReturnItem.ToTable("sale_return_items");
        saleReturnItem.HasKey(x => x.Id);
        saleReturnItem.Property(x => x.Quantity).HasPrecision(20, 6);
        saleReturnItem.Property(x => x.QuantityBase).HasPrecision(20, 6);
        saleReturnItem.Property(x => x.ReturnAmount).HasPrecision(18, 2);
        saleReturnItem.Property(x => x.CogsAmount).HasPrecision(18, 2);

        var saleRefund = modelBuilder.Entity<SaleRefundEntity>();
        saleRefund.ToTable("sale_refunds");
        saleRefund.HasKey(x => x.Id);
        saleRefund.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        saleRefund.HasIndex(x => x.IdempotencyKey).IsUnique();
        saleRefund.Property(x => x.Amount).HasPrecision(18, 2);

        base.OnModelCreating(modelBuilder);
    }
}
