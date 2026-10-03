using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Catalog;

public sealed partial class ProductCatalogViewModel : ObservableObject
{
    private readonly IProductCatalogService _catalog;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public ProductCatalogViewModel(
        IProductCatalogService catalog,
        IPermissionAuthorizer authorizer)
    {
        _catalog = catalog;
        _authorizer = authorizer;

        RefreshProductsCommand = new AsyncRelayCommand(RefreshProductsAsync);
        NewProductCommand = new RelayCommand(NewProduct);
        EditSelectedProductCommand = new AsyncRelayCommand(EditSelectedProductAsync);
        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync);
        AddExtraUnitCommand = new RelayCommand(AddExtraUnit);
        RemoveExtraUnitCommand = new RelayCommand<ProductUnitEditorRow>(RemoveExtraUnit);
        AddBarcodeCommand = new RelayCommand(AddBarcode);
        RemoveBarcodeCommand = new RelayCommand<BarcodeEditorRow>(RemoveBarcode);

        NewCategoryCommand = new RelayCommand(NewCategory);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync);
        NewBrandCommand = new RelayCommand(NewBrand);
        SaveBrandCommand = new AsyncRelayCommand(SaveBrandAsync);
        NewUnitCommand = new RelayCommand(NewUnit);
        SaveUnitCommand = new AsyncRelayCommand(SaveUnitAsync);
    }

    public ObservableCollection<CatalogProductSummary> Products { get; } = [];
    public ObservableCollection<CatalogCategoryItem> Categories { get; } = [];
    public ObservableCollection<CatalogBrandItem> Brands { get; } = [];
    public ObservableCollection<CatalogUnitItem> Units { get; } = [];
    public ObservableCollection<ProductUnitEditorRow> ExtraUnits { get; } = [];
    public ObservableCollection<BarcodeEditorRow> Barcodes { get; } = [];
    public ObservableCollection<BarcodeUnitOption> BarcodeUnitOptions { get; } = [];

    public string[] StatusOptions { get; } = ["All", "Active", "Inactive"];

    public IAsyncRelayCommand RefreshProductsCommand { get; }
    public IRelayCommand NewProductCommand { get; }
    public IAsyncRelayCommand EditSelectedProductCommand { get; }
    public IAsyncRelayCommand SaveProductCommand { get; }
    public IRelayCommand AddExtraUnitCommand { get; }
    public IRelayCommand<ProductUnitEditorRow> RemoveExtraUnitCommand { get; }
    public IRelayCommand AddBarcodeCommand { get; }
    public IRelayCommand<BarcodeEditorRow> RemoveBarcodeCommand { get; }
    public IRelayCommand NewCategoryCommand { get; }
    public IAsyncRelayCommand SaveCategoryCommand { get; }
    public IRelayCommand NewBrandCommand { get; }
    public IAsyncRelayCommand SaveBrandCommand { get; }
    public IRelayCommand NewUnitCommand { get; }
    public IAsyncRelayCommand SaveUnitCommand { get; }

    public bool CanManageProducts => _authorizer.HasPermission("inventory.products.manage");
    public bool CanManageCatalog => _authorizer.HasPermission("inventory.catalog.manage");

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedStatus = "All";
    [ObservableProperty] private CatalogCategoryItem? selectedFilterCategory;
    [ObservableProperty] private CatalogProductSummary? selectedProduct;

    [ObservableProperty] private long? editingProductId;
    [ObservableProperty] private string productSku = string.Empty;
    [ObservableProperty] private string productNameEn = string.Empty;
    [ObservableProperty] private string? productNameFa;
    [ObservableProperty] private string? productNamePs;
    [ObservableProperty] private CatalogCategoryItem? selectedProductCategory;
    [ObservableProperty] private CatalogBrandItem? selectedProductBrand;
    [ObservableProperty] private CatalogUnitItem? selectedBaseUnit;
    [ObservableProperty] private string? descriptionEn;
    [ObservableProperty] private string? descriptionFa;
    [ObservableProperty] private string? descriptionPs;
    [ObservableProperty] private string? shelfLocation;
    [ObservableProperty] private decimal purchaseCost;
    [ObservableProperty] private decimal sellingPrice;
    [ObservableProperty] private decimal? minimumSellingPrice;
    [ObservableProperty] private decimal? wholesalePrice;
    [ObservableProperty] private decimal stockOnHand;
    [ObservableProperty] private decimal minimumStock;
    [ObservableProperty] private decimal reorderQuantity;
    [ObservableProperty] private bool trackStock = true;
    [ObservableProperty] private bool trackExpiry;
    [ObservableProperty] private bool productIsActive = true;

    [ObservableProperty] private CatalogUnitItem? selectedExtraUnitToAdd;
    [ObservableProperty] private decimal newExtraUnitFactor = 1m;
    [ObservableProperty] private bool newExtraCanPurchase;
    [ObservableProperty] private bool newExtraCanSell = true;
    [ObservableProperty] private decimal? newExtraSellingPrice;
    [ObservableProperty] private decimal? newExtraMinimumPrice;
    [ObservableProperty] private decimal? newExtraWholesalePrice;

    [ObservableProperty] private string newBarcode = string.Empty;
    [ObservableProperty] private BarcodeUnitOption? selectedBarcodeUnit;
    [ObservableProperty] private bool newBarcodePrimary;

    [ObservableProperty] private CatalogCategoryItem? selectedCategoryRow;
    [ObservableProperty] private long? editingCategoryId;
    [ObservableProperty] private CatalogCategoryItem? selectedParentCategory;
    [ObservableProperty] private string categoryNameEn = string.Empty;
    [ObservableProperty] private string? categoryNameFa;
    [ObservableProperty] private string? categoryNamePs;
    [ObservableProperty] private int categorySortOrder;
    [ObservableProperty] private bool categoryIsActive = true;

    [ObservableProperty] private CatalogBrandItem? selectedBrandRow;
    [ObservableProperty] private long? editingBrandId;
    [ObservableProperty] private string brandNameEn = string.Empty;
    [ObservableProperty] private string? brandNameFa;
    [ObservableProperty] private string? brandNamePs;
    [ObservableProperty] private bool brandIsActive = true;

    [ObservableProperty] private CatalogUnitItem? selectedUnitRow;
    [ObservableProperty] private long? editingUnitId;
    [ObservableProperty] private string unitCode = string.Empty;
    [ObservableProperty] private string unitNameEn = string.Empty;
    [ObservableProperty] private string? unitNameFa;
    [ObservableProperty] private string? unitNamePs;
    [ObservableProperty] private string? unitSymbol;
    [ObservableProperty] private int unitDecimalPlaces;
    [ObservableProperty] private bool unitIsActive = true;

    public string ProductEditorTitle => EditingProductId is null ? "New product" : "Edit product";

    partial void OnEditingProductIdChanged(long? value) => OnPropertyChanged(nameof(ProductEditorTitle));
    partial void OnSelectedBaseUnitChanged(CatalogUnitItem? value) => RebuildBarcodeUnits();

    partial void OnSelectedCategoryRowChanged(CatalogCategoryItem? value)
    {
        if (value is null) return;
        EditingCategoryId = value.Id;
        CategoryNameEn = value.NameEn;
        CategoryNameFa = value.NameFa;
        CategoryNamePs = value.NamePs;
        CategorySortOrder = value.SortOrder;
        CategoryIsActive = value.IsActive;
        SelectedParentCategory = Categories.FirstOrDefault(x => x.Id == value.ParentId);
    }

    partial void OnSelectedBrandRowChanged(CatalogBrandItem? value)
    {
        if (value is null) return;
        EditingBrandId = value.Id;
        BrandNameEn = value.NameEn;
        BrandNameFa = value.NameFa;
        BrandNamePs = value.NamePs;
        BrandIsActive = value.IsActive;
    }

    partial void OnSelectedUnitRowChanged(CatalogUnitItem? value)
    {
        if (value is null) return;
        EditingUnitId = value.Id;
        UnitCode = value.Code;
        UnitNameEn = value.NameEn;
        UnitNameFa = value.NameFa;
        UnitNamePs = value.NamePs;
        UnitSymbol = value.Symbol;
        UnitDecimalPlaces = value.DecimalPlaces;
        UnitIsActive = value.IsActive;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;

        await ExecuteBusyAsync(async () =>
        {
            await ReloadReferencesAsync();
            NewProduct();
            NewCategory();
            NewBrand();
            NewUnit();
            await RefreshProductsCoreAsync();
            _loaded = true;
        });
    }

    private async Task RefreshProductsAsync() => await ExecuteBusyAsync(RefreshProductsCoreAsync);

    private async Task RefreshProductsCoreAsync()
    {
        bool? active = SelectedStatus switch
        {
            "Active" => true,
            "Inactive" => false,
            _ => null,
        };

        var rows = await _catalog.GetProductsAsync(SearchText, SelectedFilterCategory?.Id, active);
        Products.Clear();
        foreach (var row in rows) Products.Add(row);
        StatusMessage = rows.Count + " product(s).";
    }

    private void NewProduct()
    {
        EditingProductId = null;
        ProductSku = string.Empty;
        ProductNameEn = string.Empty;
        ProductNameFa = null;
        ProductNamePs = null;
        SelectedProductCategory = null;
        SelectedProductBrand = null;
        SelectedBaseUnit = Units.FirstOrDefault(x => x.IsActive);
        DescriptionEn = null;
        DescriptionFa = null;
        DescriptionPs = null;
        ShelfLocation = null;
        PurchaseCost = 0m;
        SellingPrice = 0m;
        MinimumSellingPrice = null;
        WholesalePrice = null;
        StockOnHand = 0m;
        MinimumStock = 0m;
        ReorderQuantity = 0m;
        TrackStock = true;
        TrackExpiry = false;
        ProductIsActive = true;
        ExtraUnits.Clear();
        Barcodes.Clear();
        ResetExtraUnitEntry();
        NewBarcode = string.Empty;
        NewBarcodePrimary = false;
        RebuildBarcodeUnits();
        StatusMessage = "New product form ready.";
    }

    private async Task EditSelectedProductAsync()
    {
        if (SelectedProduct is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var detail = await _catalog.GetProductAsync(SelectedProduct.Id)
                ?? throw new InvalidOperationException("Product was not found.");

            EditingProductId = detail.Id;
            ProductSku = detail.Sku;
            ProductNameEn = detail.NameEn;
            ProductNameFa = detail.NameFa;
            ProductNamePs = detail.NamePs;
            SelectedProductCategory = Categories.FirstOrDefault(x => x.Id == detail.CategoryId);
            SelectedProductBrand = Brands.FirstOrDefault(x => x.Id == detail.BrandId);
            SelectedBaseUnit = Units.FirstOrDefault(x => x.Id == detail.BaseUnitId);
            DescriptionEn = detail.DescriptionEn;
            DescriptionFa = detail.DescriptionFa;
            DescriptionPs = detail.DescriptionPs;
            ShelfLocation = detail.ShelfLocation;
            PurchaseCost = detail.PurchaseCost;
            SellingPrice = detail.SellingPrice;
            MinimumSellingPrice = detail.MinimumSellingPrice;
            WholesalePrice = detail.WholesalePrice;
            StockOnHand = detail.StockOnHand;
            MinimumStock = detail.MinimumStock;
            ReorderQuantity = detail.ReorderQuantity;
            TrackStock = detail.TrackStock;
            TrackExpiry = detail.TrackExpiry;
            ProductIsActive = detail.IsActive;

            ExtraUnits.Clear();
            foreach (var unit in detail.Units.Where(x => x.UnitId != detail.BaseUnitId))
            {
                ExtraUnits.Add(new ProductUnitEditorRow
                {
                    UnitId = unit.UnitId,
                    UnitName = unit.UnitName,
                    UnitCode = unit.UnitCode,
                    ConversionFactor = unit.ConversionFactor,
                    CanPurchase = unit.CanPurchase,
                    CanSell = unit.CanSell,
                    SellingPrice = unit.SellingPrice,
                    MinimumSellingPrice = unit.MinimumSellingPrice,
                    WholesalePrice = unit.WholesalePrice,
                });
            }

            Barcodes.Clear();
            foreach (var barcode in detail.Barcodes)
            {
                Barcodes.Add(new BarcodeEditorRow
                {
                    UnitId = barcode.UnitId,
                    UnitName = barcode.UnitName,
                    Barcode = barcode.Barcode,
                    IsPrimary = barcode.IsPrimary,
                });
            }

            RebuildBarcodeUnits();
            StatusMessage = "Editing " + detail.Sku + ".";
        });
    }

    private async Task SaveProductAsync()
    {
        if (!CanManageProducts) return;

        await ExecuteBusyAsync(async () =>
        {
            if (SelectedBaseUnit is null)
            {
                throw new InvalidOperationException("Select a base unit.");
            }

            var saved = await _catalog.SaveProductAsync(new CatalogProductSaveRequest(
                EditingProductId,
                ProductSku,
                ProductNameEn,
                ProductNameFa,
                ProductNamePs,
                SelectedProductCategory?.Id,
                SelectedProductBrand?.Id,
                SelectedBaseUnit.Id,
                DescriptionEn,
                DescriptionFa,
                DescriptionPs,
                ShelfLocation,
                PurchaseCost,
                SellingPrice,
                MinimumSellingPrice,
                WholesalePrice,
                MinimumStock,
                ReorderQuantity,
                TrackStock,
                TrackExpiry,
                ProductIsActive,
                ExtraUnits.Select(x => new CatalogProductUnitInput(
                    x.UnitId, x.ConversionFactor, x.CanPurchase, x.CanSell,
                    x.SellingPrice, x.MinimumSellingPrice, x.WholesalePrice)).ToList(),
                Barcodes.Select(x => new CatalogBarcodeInput(
                    x.Barcode, x.UnitId, x.IsPrimary)).ToList()));

            EditingProductId = saved.Id;
            StockOnHand = saved.StockOnHand;
            await RefreshProductsCoreAsync();
            SelectedProduct = Products.FirstOrDefault(x => x.Id == saved.Id);
            StatusMessage = "Product " + saved.Sku + " saved.";
        });
    }

    private void AddExtraUnit()
    {
        if (SelectedExtraUnitToAdd is null)
        {
            StatusMessage = "Select a unit to add.";
            return;
        }

        if (SelectedBaseUnit?.Id == SelectedExtraUnitToAdd.Id ||
            ExtraUnits.Any(x => x.UnitId == SelectedExtraUnitToAdd.Id))
        {
            StatusMessage = "That unit is already configured.";
            return;
        }

        ExtraUnits.Add(new ProductUnitEditorRow
        {
            UnitId = SelectedExtraUnitToAdd.Id,
            UnitName = SelectedExtraUnitToAdd.Name,
            UnitCode = SelectedExtraUnitToAdd.Code,
            ConversionFactor = NewExtraUnitFactor,
            CanPurchase = NewExtraCanPurchase,
            CanSell = NewExtraCanSell,
            SellingPrice = NewExtraSellingPrice,
            MinimumSellingPrice = NewExtraMinimumPrice,
            WholesalePrice = NewExtraWholesalePrice,
        });
        ResetExtraUnitEntry();
        RebuildBarcodeUnits();
    }

    private void RemoveExtraUnit(ProductUnitEditorRow? row)
    {
        if (row is null) return;
        ExtraUnits.Remove(row);
        foreach (var barcode in Barcodes.Where(x => x.UnitId == row.UnitId).ToList())
        {
            Barcodes.Remove(barcode);
        }
        RebuildBarcodeUnits();
    }

    private void AddBarcode()
    {
        var barcode = NewBarcode.Trim();
        if (string.IsNullOrWhiteSpace(barcode) || SelectedBarcodeUnit is null)
        {
            StatusMessage = "Enter a barcode and select its unit.";
            return;
        }

        if (Barcodes.Any(x => string.Equals(x.Barcode, barcode, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "That barcode is already in this product.";
            return;
        }

        if (NewBarcodePrimary)
        {
            foreach (var row in Barcodes) row.IsPrimary = false;
        }

        Barcodes.Add(new BarcodeEditorRow
        {
            Barcode = barcode,
            UnitId = SelectedBarcodeUnit.UnitId,
            UnitName = SelectedBarcodeUnit.Name,
            IsPrimary = NewBarcodePrimary || Barcodes.Count == 0,
        });
        NewBarcode = string.Empty;
        NewBarcodePrimary = false;
    }

    private void RemoveBarcode(BarcodeEditorRow? row)
    {
        if (row is null) return;
        var wasPrimary = row.IsPrimary;
        Barcodes.Remove(row);
        if (wasPrimary && Barcodes.Count > 0 && Barcodes.All(x => !x.IsPrimary))
        {
            Barcodes[0].IsPrimary = true;
        }
    }

    private void RebuildBarcodeUnits()
    {
        var selectedId = SelectedBarcodeUnit?.UnitId;
        BarcodeUnitOptions.Clear();

        if (SelectedBaseUnit is not null)
        {
            BarcodeUnitOptions.Add(new BarcodeUnitOption(
                SelectedBaseUnit.Id,
                SelectedBaseUnit.Name + " · " + SelectedBaseUnit.Code));
        }

        foreach (var unit in ExtraUnits)
        {
            BarcodeUnitOptions.Add(new BarcodeUnitOption(unit.UnitId, unit.UnitName + " · " + unit.UnitCode));
        }

        SelectedBarcodeUnit = BarcodeUnitOptions.FirstOrDefault(x => x.UnitId == selectedId)
            ?? BarcodeUnitOptions.FirstOrDefault();
    }

    private void ResetExtraUnitEntry()
    {
        SelectedExtraUnitToAdd = Units.FirstOrDefault(x =>
            x.IsActive && x.Id != SelectedBaseUnit?.Id && ExtraUnits.All(e => e.UnitId != x.Id));
        NewExtraUnitFactor = 1m;
        NewExtraCanPurchase = false;
        NewExtraCanSell = true;
        NewExtraSellingPrice = null;
        NewExtraMinimumPrice = null;
        NewExtraWholesalePrice = null;
    }

    private void NewCategory()
    {
        SelectedCategoryRow = null;
        EditingCategoryId = null;
        SelectedParentCategory = null;
        CategoryNameEn = string.Empty;
        CategoryNameFa = null;
        CategoryNamePs = null;
        CategorySortOrder = 0;
        CategoryIsActive = true;
    }

    private async Task SaveCategoryAsync()
    {
        if (!CanManageCatalog) return;
        await ExecuteBusyAsync(async () =>
        {
            var saved = await _catalog.SaveCategoryAsync(new CatalogCategorySaveRequest(
                EditingCategoryId, SelectedParentCategory?.Id, CategoryNameEn,
                CategoryNameFa, CategoryNamePs, CategorySortOrder, CategoryIsActive));
            await ReloadReferencesAsync();
            SelectedCategoryRow = Categories.FirstOrDefault(x => x.Id == saved.Id);
            await RefreshProductsCoreAsync();
            StatusMessage = "Category saved.";
        });
    }

    private void NewBrand()
    {
        SelectedBrandRow = null;
        EditingBrandId = null;
        BrandNameEn = string.Empty;
        BrandNameFa = null;
        BrandNamePs = null;
        BrandIsActive = true;
    }

    private async Task SaveBrandAsync()
    {
        if (!CanManageCatalog) return;
        await ExecuteBusyAsync(async () =>
        {
            var saved = await _catalog.SaveBrandAsync(new CatalogBrandSaveRequest(
                EditingBrandId, BrandNameEn, BrandNameFa, BrandNamePs, BrandIsActive));
            await ReloadReferencesAsync();
            SelectedBrandRow = Brands.FirstOrDefault(x => x.Id == saved.Id);
            await RefreshProductsCoreAsync();
            StatusMessage = "Brand saved.";
        });
    }

    private void NewUnit()
    {
        SelectedUnitRow = null;
        EditingUnitId = null;
        UnitCode = string.Empty;
        UnitNameEn = string.Empty;
        UnitNameFa = null;
        UnitNamePs = null;
        UnitSymbol = null;
        UnitDecimalPlaces = 0;
        UnitIsActive = true;
    }

    private async Task SaveUnitAsync()
    {
        if (!CanManageCatalog) return;
        await ExecuteBusyAsync(async () =>
        {
            var saved = await _catalog.SaveUnitAsync(new CatalogUnitSaveRequest(
                EditingUnitId, UnitCode, UnitNameEn, UnitNameFa, UnitNamePs,
                UnitSymbol, UnitDecimalPlaces, UnitIsActive));
            await ReloadReferencesAsync();
            SelectedUnitRow = Units.FirstOrDefault(x => x.Id == saved.Id);
            StatusMessage = "Unit saved.";
        });
    }

    private async Task ReloadReferencesAsync()
    {
        var data = await _catalog.GetReferenceDataAsync();

        var filterCategoryId = SelectedFilterCategory?.Id;
        var productCategoryId = SelectedProductCategory?.Id;
        var productBrandId = SelectedProductBrand?.Id;
        var baseUnitId = SelectedBaseUnit?.Id;

        Categories.Clear();
        foreach (var row in data.Categories) Categories.Add(row);
        Brands.Clear();
        foreach (var row in data.Brands) Brands.Add(row);
        Units.Clear();
        foreach (var row in data.Units) Units.Add(row);

        SelectedFilterCategory = Categories.FirstOrDefault(x => x.Id == filterCategoryId);
        SelectedProductCategory = Categories.FirstOrDefault(x => x.Id == productCategoryId);
        SelectedProductBrand = Brands.FirstOrDefault(x => x.Id == productBrandId);
        SelectedBaseUnit = Units.FirstOrDefault(x => x.Id == baseUnitId) ?? Units.FirstOrDefault(x => x.IsActive);
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
