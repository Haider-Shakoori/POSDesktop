using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Cash;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Expenses;

public sealed partial class ExpensesViewModel : ObservableObject
{
    private readonly ICashManagementService _cash;
    private bool _loaded;

    public ExpensesViewModel(ICashManagementService cash)
    {
        _cash = cash;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RecordCommand = new AsyncRelayCommand(RecordAsync);
    }

    public ObservableCollection<ExpenseCategoryOption> Categories { get; } = [];
    public ObservableCollection<ExpenseCategoryOption> FilteredCategories { get; } = [];
    public ObservableCollection<CashPaymentMethodOption> PaymentMethods { get; } = [];
    public ObservableCollection<OperatingEntryRow> Entries { get; } = [];
    public string[] EntryTypes { get; } = ["expense", "income"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RecordCommand { get; }

    [ObservableProperty] private string selectedEntryType = "expense";
    [ObservableProperty] private ExpenseCategoryOption? selectedCategory;
    [ObservableProperty] private CashPaymentMethodOption? selectedPaymentMethod;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? reference;
    [ObservableProperty] private string? description;
    [ObservableProperty] private DateTime occurredDate = DateTime.Today;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public decimal ExpenseTotal => Entries.Where(x => x.EntryType == "expense").Sum(x => x.Amount);
    public decimal IncomeTotal => Entries.Where(x => x.EntryType == "income").Sum(x => x.Amount);
    public decimal NetOperating => IncomeTotal - ExpenseTotal;
    public int EntryCount => Entries.Count;

    partial void OnSelectedEntryTypeChanged(string value) => RefilterCategories();

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            Categories.Clear();
            foreach (var category in await _cash.GetExpenseCategoriesAsync())
                Categories.Add(category);

            PaymentMethods.Clear();
            foreach (var method in await _cash.GetPaymentMethodsAsync())
                PaymentMethods.Add(method);

            SelectedPaymentMethod = PaymentMethods.FirstOrDefault(x => x.IsCash) ?? PaymentMethods.FirstOrDefault();
            RefilterCategories();
            await RefreshCoreAsync();
            _loaded = true;
        });
    }

    private async Task RefreshAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await RefreshCoreAsync();
            StatusMessage = "Operating entries refreshed.";
        });

    private async Task RefreshCoreAsync()
    {
        Entries.Clear();
        foreach (var row in await _cash.GetOperatingEntriesAsync())
            Entries.Add(row);
        RaiseSummary();
    }

    private async Task RecordAsync()
    {
        if (SelectedCategory is null || SelectedPaymentMethod is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var at = new DateTimeOffset(OccurredDate.Date + DateTime.Now.TimeOfDay);
            var saved = await _cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), SelectedEntryType, SelectedCategory.Id,
                SelectedPaymentMethod.Id, Amount, Reference, Description, at));
            Amount = 0m;
            Reference = null;
            Description = null;
            await RefreshCoreAsync();
            StatusMessage = saved.Number + " recorded.";
        });
    }

    private void RefilterCategories()
    {
        FilteredCategories.Clear();
        foreach (var category in Categories.Where(x => x.EntryType == SelectedEntryType))
            FilteredCategories.Add(category);
        SelectedCategory = FilteredCategories.FirstOrDefault();
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ExpenseTotal));
        OnPropertyChanged(nameof(IncomeTotal));
        OnPropertyChanged(nameof(NetOperating));
        OnPropertyChanged(nameof(EntryCount));
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
