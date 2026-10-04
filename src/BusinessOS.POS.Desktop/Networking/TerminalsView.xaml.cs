using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Networking;

public partial class TerminalsView : UserControl
{
    public TerminalsView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TerminalsViewModel vm)
            await vm.InitializeAsync();
    }
}
