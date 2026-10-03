using System.Collections.ObjectModel;
using BusinessOS.POS.Desktop.Appearance;
using BusinessOS.POS.Desktop.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        NavigationItems =
        [
            new("dashboard", "\uE80F", "Dashboard", "Today at a glance"),
            new("pos", "\uE719", "Point of Sale", "Fast cashier checkout"),
            new("sales", "\uE8CB", "Sales", "Invoices, returns and held sales"),
            new("products", "\uE8F1", "Products", "Catalog, units and barcodes"),
            new("inventory", "\uE7C5", "Inventory", "Stock, counts and write-offs"),
            new("purchasing", "\uE7BF", "Purchasing", "Suppliers, orders and receipts"),
            new("customers", "\uE716", "Customers", "Balances and collections"),
            new("cash", "\uE8C7", "Cash & Shifts", "Drawer movements and cashier shifts"),
            new("closing", "\uE787", "Daily Closing", "Shift and business-day reconciliation"),
            new("expenses", "\uE8C8", "Expenses", "Operating expenses"),
            new("reports", "\uE9D2", "Reports", "Sales, stock and financial insight"),
            new("users", "\uE77B", "Users & Roles", "Access and permissions"),
            new("terminals", "\uE7F4", "Terminals", "LAN and workstation management"),
            new("settings", "\uE713", "Settings", "Business and application settings")
        ];

        SelectedNavigation = NavigationItems[0];
        Themes = Enum.GetValues<AppearanceTheme>();
        UiLanguages = ["English", "دری", "پښتو"];
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public AppearanceTheme[] Themes { get; }

    public string[] UiLanguages { get; }

    [ObservableProperty]
    private NavigationItemViewModel _selectedNavigation = null!;

    [ObservableProperty]
    private AppearanceTheme _selectedTheme = AppearanceTheme.Classic;

    [ObservableProperty]
    private string _selectedLanguage = "English";

    partial void OnSelectedThemeChanged(AppearanceTheme value) => ThemeManager.Apply(value);
}
