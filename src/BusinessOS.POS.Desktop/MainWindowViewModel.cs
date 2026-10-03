using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Desktop.Appearance;
using BusinessOS.POS.Desktop.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace BusinessOS.POS.Desktop;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IUserSessionService _sessions;
    private bool _initializing = true;

    public MainWindowViewModel(
        IUserSessionService sessions,
        IPermissionAuthorizer authorizer)
    {
        _sessions = sessions;

        var items = new[]
        {
            new NavigationItemViewModel("dashboard", "\uE80F", "Dashboard", "Today at a glance"),
            new("pos", "\uE719", "Point of Sale", "Fast cashier checkout", "pos.access"),
            new("sales", "\uE8CB", "Sales", "Invoices, returns and held sales", "sales.view"),
            new("products", "\uE8F1", "Products", "Catalog, units and barcodes", "inventory.view"),
            new("inventory", "\uE7C5", "Inventory", "Stock, counts and write-offs", "inventory.view"),
            new("purchasing", "\uE7BF", "Purchasing", "Suppliers, orders and receipts", "purchases.view"),
            new("customers", "\uE716", "Customers", "Balances and collections", "customers.view"),
            new("cash", "\uE8C7", "Cash & Shifts", "Drawer movements and cashier shifts", "cash.view"),
            new("closing", "\uE787", "Daily Closing", "Shift and business-day reconciliation", "business_days.view"),
            new("expenses", "\uE8C8", "Expenses", "Operating expenses", "expenses.view"),
            new("reports", "\uE9D2", "Reports", "Sales, stock and financial insight", "reports.view"),
            new("users", "\uE77B", "Users & Roles", "Access and permissions", "users.manage"),
            new("terminals", "\uE7F4", "Terminals", "LAN and workstation management", "users.manage"),
            new("settings", "\uE713", "Settings", "Business and application settings", "settings.manage"),
        };

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            items.Where(x => x.RequiredPermission is null || authorizer.HasPermission(x.RequiredPermission)));

        SelectedNavigation = NavigationItems[0];
        Themes = Enum.GetValues<AppearanceTheme>();
        UiLanguages = ["English", "دری", "پښتو"];

        var current = sessions.Current;
        CurrentUserDisplay = current is null
            ? string.Empty
            : current.Name + " · @" + current.Username;

        SelectedLanguage = current?.PreferredLocale switch
        {
            "fa" => "دری",
            "ps" => "پښتو",
            _ => "English",
        };

        ApplyFlowDirection(SelectedLanguage);
        _initializing = false;
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public AppearanceTheme[] Themes { get; }

    public string[] UiLanguages { get; }

    public string CurrentUserDisplay { get; }

    [ObservableProperty]
    private NavigationItemViewModel _selectedNavigation = null!;

    [ObservableProperty]
    private AppearanceTheme _selectedTheme = AppearanceTheme.Classic;

    [ObservableProperty]
    private string _selectedLanguage = "English";

    [ObservableProperty]
    private WpfFlowDirection _flowDirection = WpfFlowDirection.LeftToRight;

    partial void OnSelectedThemeChanged(AppearanceTheme value) => ThemeManager.Apply(value);

    partial void OnSelectedLanguageChanged(string value)
    {
        ApplyFlowDirection(value);

        if (!_initializing)
        {
            _ = _sessions.UpdatePreferredLocaleAsync(ToLocale(value));
        }
    }

    public Task SignOutAsync() => _sessions.LogoutAsync();

    private void ApplyFlowDirection(string language) =>
        FlowDirection = language is "دری" or "پښتو"
            ? WpfFlowDirection.RightToLeft
            : WpfFlowDirection.LeftToRight;

    private static string ToLocale(string language) => language switch
    {
        "دری" => "fa",
        "پښتو" => "ps",
        _ => "en",
    };
}
