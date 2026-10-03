using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Expenses;

public partial class ExpensesView : UserControl
{
    public ExpensesView() => InitializeComponent();
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ExpensesViewModel vm) await vm.InitializeAsync();
    }
}
