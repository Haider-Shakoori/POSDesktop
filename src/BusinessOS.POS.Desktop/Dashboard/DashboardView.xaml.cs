using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
            await vm.InitializeAsync();
    }
}
