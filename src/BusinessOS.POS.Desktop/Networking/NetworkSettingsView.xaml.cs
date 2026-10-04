using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Networking;

public partial class NetworkSettingsView : UserControl
{
    public NetworkSettingsView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is NetworkSettingsViewModel vm)
            await vm.InitializeAsync();
    }
}
