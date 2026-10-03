using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Customers;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm)
            await vm.InitializeAsync();
    }
}
