using System.Windows;

namespace BusinessOS.POS.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public event EventHandler? SignOutRequested;

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SignOutAsync();
        SignOutRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
