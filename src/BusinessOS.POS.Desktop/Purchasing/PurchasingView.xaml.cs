using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Purchasing;

public partial class PurchasingView : UserControl
{
    public PurchasingView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PurchasingViewModel vm)
            await vm.InitializeAsync();
    }
}
