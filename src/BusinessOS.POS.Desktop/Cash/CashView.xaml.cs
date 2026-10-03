using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Cash;

public partial class CashView : UserControl
{
    public CashView() => InitializeComponent();
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CashViewModel vm) await vm.InitializeAsync();
    }
}
