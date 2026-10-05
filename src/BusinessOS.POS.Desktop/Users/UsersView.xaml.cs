using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.POS.Desktop.Users;

public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm)
            await vm.InitializeAsync();
    }

    private void OnUserPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm && sender is PasswordBox box)
            vm.UserPassword = box.Password;
    }
}
