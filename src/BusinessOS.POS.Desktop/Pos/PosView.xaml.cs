using System.Windows.Controls;
using System.Windows.Input;

namespace BusinessOS.POS.Desktop.Pos;

public partial class PosView : UserControl
{
    public PosView()
    {
        InitializeComponent();
    }

    private PosViewModel? ViewModel => DataContext as PosViewModel;

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.InitializeAsync();
            SearchInput.Focus();
        }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is not null)
        {
            e.Handled = true;
            await ViewModel.SearchAndMaybeAddExactAsync();
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedProduct is not null)
        {
            ViewModel.AddProduct(ViewModel.SelectedProduct);
            SearchInput.Focus();
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            e.Handled = true;
            SearchInput.Focus();
            SearchInput.SelectAll();
            return;
        }

        if (e.Key == Key.F8)
        {
            e.Handled = true;
            await ViewModel.HoldAsync();
            return;
        }

        if (e.Key == Key.F9 || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            e.Handled = true;
            await ViewModel.CheckoutAsync();
        }
    }
}
