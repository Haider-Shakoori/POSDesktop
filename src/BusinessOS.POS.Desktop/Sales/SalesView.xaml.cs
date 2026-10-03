using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Sales;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SalesViewModel viewModel)
            await viewModel.InitializeAsync();
    }
}
