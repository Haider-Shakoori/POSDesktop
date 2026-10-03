using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Catalog;

public partial class ProductCatalogView : UserControl
{
    public ProductCatalogView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ProductCatalogViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
