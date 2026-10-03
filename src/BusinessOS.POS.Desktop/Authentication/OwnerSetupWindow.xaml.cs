using System.Windows;

namespace BusinessOS.POS.Desktop.Authentication;

public partial class OwnerSetupWindow : Window
{
    private readonly OwnerSetupViewModel _viewModel;

    public OwnerSetupWindow(OwnerSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.SetupSucceeded += OnSetupSucceeded;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateAsync(PasswordInput.Password, ConfirmPasswordInput.Password);

    private void OnSetupSucceeded(object? sender, EventArgs e)
    {
        _viewModel.SetupSucceeded -= OnSetupSucceeded;
        DialogResult = true;
    }
}
