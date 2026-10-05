using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Terminals;

public partial class TerminalsView : UserControl
{
    public TerminalsView() => InitializeComponent();
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalsViewModel vm) await vm.LoadAsync();
    }
}
