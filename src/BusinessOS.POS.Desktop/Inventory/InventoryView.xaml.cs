using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Inventory;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is InventoryViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
