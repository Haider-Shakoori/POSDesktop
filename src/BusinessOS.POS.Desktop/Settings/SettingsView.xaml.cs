using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.LoadAsync();
    }
}
