using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Customers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Customers;

public sealed partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public CustomersViewModel(ICustomerService customers, IPermissionAuthorizer authorizer)
    {
        _customers = customers;
        _authorizer = authorizer;

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewCustomerCommand = new RelayCommand(NewCustomer);
        SaveCustomerCommand = new AsyncRelayCommand(SaveCustomerAsync);
        CollectCommand = new AsyncRelayCommand(CollectAsync);
    }

    public ObservableCollection<CustomerSummary> Customers { get; } = [];
    public ObservableCollection<CustomerLedgerRow> Ledger { get; } = [];
    public ObservableCollection<CustomerOutstandingSale> OutstandingSales { get; } = [];
    public ObservableCollection<CustomerCollectionRow> Collections { get; } = [];
    public ObservableCollection<CustomerPaymentMethod> PaymentMethods { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewCustomerCommand { get; }
    public IAsyncRelayCommand SaveCustomerCommand { get; }
    public IAsyncRelayCommand CollectCommand { get; }

    public bool CanManage => _authorizer.HasPermission("customers.manage");
    public bool CanCollect => _authorizer.HasPermission("customers.collect");

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private CustomerSummary? selectedCustomer;
    [ObservableProperty] private long? editingCustomerId;
    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string? phone;
    [ObservableProperty] private string? alternatePhone;
    [ObservableProperty] private string? address;
    [ObservableProperty] private decimal creditLimit;
    [ObservableProperty] private decimal openingBalance;
    [ObservableProperty] private bool customerIsActive = true;

    [ObservableProperty] private CustomerPaymentMethod? selectedPaymentMethod;
    [ObservableProperty] private decimal collectionAmount;
    [ObservableProperty] private decimal collectionTenderedAmount;
    [ObservableProperty] private string? collectionReference;
    [ObservableProperty] private string? collectionNotes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Ready.";

    public string CustomerEditorTitle => EditingCustomerId is null ? "New customer" : "Edit customer";
    public decimal CurrentBalance => SelectedCustomer?.CurrentBalance ?? 0m;
    public decimal AvailableCredit => SelectedCustomer is null
        ? 0m
        : Math.Max(0m, SelectedCustomer.CreditLimit - SelectedCustomer.CurrentBalance);

    partial void OnEditingCustomerIdChanged(long? value) => OnPropertyChanged(nameof(CustomerEditorTitle));

    partial void OnSelectedCustomerChanged(CustomerSummary? value)
    {
        if (value is null)
        {
            Ledger.Clear();
            OutstandingSales.Clear();
            Collections.Clear();
            OnPropertyChanged(nameof(CurrentBalance));
            OnPropertyChanged(nameof(AvailableCredit));
            return;
        }

        EditingCustomerId = value.Id;
        CustomerName = value.Name;
        Phone = value.Phone;
        AlternatePhone = value.AlternatePhone;
        Address = value.Address;
        CreditLimit = value.CreditLimit;
        OpeningBalance = value.OpeningBalance;
        CustomerIsActive = value.IsActive;
        OnPropertyChanged(nameof(CurrentBalance));
        OnPropertyChanged(nameof(AvailableCredit));
        _ = LoadSelectedDetailAsync();
    }

    partial void OnSelectedPaymentMethodChanged(CustomerPaymentMethod? value)
    {
        if (value is not null && !value.IsCash)
            CollectionTenderedAmount = CollectionAmount;
    }

    partial void OnCollectionAmountChanged(decimal value)
    {
        if (SelectedPaymentMethod is null || !SelectedPaymentMethod.IsCash)
            CollectionTenderedAmount = value;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        await ExecuteBusyAsync(async () =>
        {
            PaymentMethods.Clear();
            foreach (var method in await _customers.GetPaymentMethodsAsync())
                PaymentMethods.Add(method);
            SelectedPaymentMethod = PaymentMethods.FirstOrDefault(x => x.IsCash) ?? PaymentMethods.FirstOrDefault();

            await LoadCustomersAsync();
            _loaded = true;
        });
    }

    private async Task SearchAsync() => await ExecuteBusyAsync(LoadCustomersAsync);

    private async Task RefreshAsync() =>
        await ExecuteBusyAsync(async () =>
        {
            await LoadCustomersAsync();
            StatusMessage = "Customers refreshed.";
        });

    private async Task LoadCustomersAsync()
    {
        var selectedId = SelectedCustomer?.Id;
        var rows = await _customers.GetCustomersAsync(SearchText);
        Customers.Clear();
        foreach (var row in rows) Customers.Add(row);
        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == selectedId) ?? Customers.FirstOrDefault();
        StatusMessage = rows.Count + " customer(s).";
    }

    private async Task LoadSelectedDetailAsync()
    {
        if (SelectedCustomer is null) return;
        try
        {
            var detail = await _customers.GetCustomerAsync(SelectedCustomer.Id);
            Ledger.Clear();
            OutstandingSales.Clear();
            Collections.Clear();
            if (detail is null) return;

            foreach (var row in detail.Ledger) Ledger.Add(row);
            foreach (var row in detail.OutstandingSales) OutstandingSales.Add(row);
            foreach (var row in detail.Collections) Collections.Add(row);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void NewCustomer()
    {
        SelectedCustomer = null;
        EditingCustomerId = null;
        CustomerName = string.Empty;
        Phone = null;
        AlternatePhone = null;
        Address = null;
        CreditLimit = 0m;
        OpeningBalance = 0m;
        CustomerIsActive = true;
        Ledger.Clear();
        OutstandingSales.Clear();
        Collections.Clear();
        StatusMessage = "New customer form ready.";
    }

    private async Task SaveCustomerAsync()
    {
        if (!CanManage) return;

        await ExecuteBusyAsync(async () =>
        {
            var saved = await _customers.SaveCustomerAsync(new CustomerSaveRequest(
                EditingCustomerId,
                CustomerName,
                Phone,
                AlternatePhone,
                Address,
                CreditLimit,
                OpeningBalance,
                CustomerIsActive));

            await LoadCustomersAsync();
            SelectedCustomer = Customers.FirstOrDefault(x => x.Id == saved.Id);
            StatusMessage = "Customer " + saved.Name + " saved.";
        });
    }

    private async Task CollectAsync()
    {
        if (!CanCollect || SelectedCustomer is null || SelectedPaymentMethod is null) return;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _customers.CollectAsync(new CustomerCollectionRequest(
                Guid.NewGuid().ToString(),
                SelectedCustomer.Id,
                SelectedPaymentMethod.Code,
                CollectionAmount,
                CollectionTenderedAmount == 0m ? null : CollectionTenderedAmount,
                CollectionReference,
                null,
                CollectionNotes));

            CollectionAmount = 0m;
            CollectionTenderedAmount = 0m;
            CollectionReference = null;
            CollectionNotes = null;
            await LoadCustomersAsync();
            SelectedCustomer = Customers.FirstOrDefault(x => x.Id == result.CustomerId);
            StatusMessage = result.Number + " recorded. Balance: AFN " + result.CustomerBalanceAfter.ToString("N2");
        });
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
