using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Closing;

public partial class DailyClosingView : UserControl
{
    public DailyClosingView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DailyClosingViewModel vm)
            await vm.InitializeAsync();
    }
}
