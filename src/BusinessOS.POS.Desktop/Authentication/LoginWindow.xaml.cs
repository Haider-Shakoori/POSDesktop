using System.Windows;

namespace BusinessOS.POS.Desktop.Authentication;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e) =>
        await _viewModel.SignInAsync(PasswordInput.Password);

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        DialogResult = true;
    }
}
