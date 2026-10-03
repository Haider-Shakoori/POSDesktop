using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm)
            await vm.InitializeAsync();
    }
}
