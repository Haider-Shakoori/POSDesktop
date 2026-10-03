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
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<ProductUnitEntity> ProductUnits => Set<ProductUnitEntity>();
    public DbSet<ProductBarcodeEntity> ProductBarcodes => Set<ProductBarcodeEntity>();
    public DbSet<InventoryCostLayerEntity> InventoryCostLayers => Set<InventoryCostLayerEntity>();
    public DbSet<StockMovementEntity> StockMovements => Set<StockMovementEntity>();
    public DbSet<CashierShiftEntity> CashierShifts => Set<CashierShiftEntity>();
    public DbSet<DocumentSequenceEntity> DocumentSequences => Set<DocumentSequenceEntity>();
    public DbSet<SaleEntity> Sales => Set<SaleEntity>();
    public DbSet<SaleItemEntity> SaleItems => Set<SaleItemEntity>();
    public DbSet<SalePaymentEntity> SalePayments => Set<SalePaymentEntity>();
    public DbSet<HeldSaleEntity> HeldSales => Set<HeldSaleEntity>();
    public DbSet<HeldSaleItemEntity> HeldSaleItems => Set<HeldSaleItemEntity>();

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

        var layer = modelBuilder.Entity<InventoryCostLayerEntity>();
        layer.ToTable("inventory_cost_layers");
        layer.HasKey(x => x.Id);
        layer.Property(x => x.InitialQuantityBase).HasPrecision(20, 6);
        layer.Property(x => x.RemainingQuantityBase).HasPrecision(20, 6);
        layer.Property(x => x.UnitCostBase).HasPrecision(18, 4);
        layer.HasIndex(x => new { x.ProductId, x.ReceivedAt });

        var move = modelBuilder.Entity<StockMovementEntity>();
        move.ToTable("stock_movements");
        move.HasKey(x => x.Id);
        move.Property(x => x.QuantityBase).HasPrecision(20, 6);
        move.Property(x => x.BalanceAfter).HasPrecision(20, 6);
        move.Property(x => x.UnitCostBase).HasPrecision(18, 4);
        move.HasIndex(x => new { x.ProductId, x.OccurredAt });

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

        var held = modelBuilder.Entity<HeldSaleEntity>();
        held.ToTable("held_sales");
        held.HasKey(x => x.Id);
        held.Property(x => x.Number).HasMaxLength(40).IsRequired();
        held.HasIndex(x => x.Number).IsUnique();
        held.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        held.HasIndex(x => x.IdempotencyKey).IsUnique();
        held.Property(x => x.SaleDiscountAmount).HasPrecision(18, 2);
        held.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.HeldSaleId).OnDelete(DeleteBehavior.Cascade);

        var heldItem = modelBuilder.Entity<HeldSaleItemEntity>();
        heldItem.ToTable("held_sale_items");
        heldItem.HasKey(x => x.Id);
        heldItem.Property(x => x.Quantity).HasPrecision(20, 6);
        heldItem.Property(x => x.LineDiscountAmount).HasPrecision(18, 2);
        heldItem.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 2);

        base.OnModelCreating(modelBuilder);
    }
}
