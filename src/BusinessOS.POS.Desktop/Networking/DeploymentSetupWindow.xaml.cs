using System.Windows;

namespace BusinessOS.POS.Desktop.Networking;

public partial class DeploymentSetupWindow : Window
{
    private DeploymentSetupViewModel Vm => (DeploymentSetupViewModel)DataContext;

    public DeploymentSetupWindow(DeploymentSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnStandaloneClick(object sender, RoutedEventArgs e)
    {
        try { await Vm.ConfigureStandaloneAsync(); DialogResult = true; }
        catch { }
    }

    private async void OnServerClick(object sender, RoutedEventArgs e)
    {
        try { await Vm.ConfigureServerAsync(); DialogResult = true; }
        catch { }
    }

    private async void OnDiscoverClick(object sender, RoutedEventArgs e)
    {
        try { await Vm.DiscoverAsync(); }
        catch { }
    }

    private void OnUseManualServerClick(object sender, RoutedEventArgs e)
    {
        try { Vm.UseManualServer(); }
        catch (Exception ex) { Vm.StatusMessage = ex.Message; }
    }

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        try { await Vm.PairClientAsync(); DialogResult = true; }
        catch { }
    }
}
