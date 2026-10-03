using BusinessOS.POS.Application.Abstractions.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Authentication;

public sealed partial class OwnerSetupViewModel(IOwnerBootstrapService bootstrap) : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string selectedLanguage = "English";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isError;

    [ObservableProperty]
    private string statusMessage = "Create the first owner account for this POS installation.";

    public string[] Languages { get; } = ["English", "دری", "پښتو"];

    public event EventHandler? SetupSucceeded;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public async Task CreateAsync(string password, string confirmation)
    {
        if (IsBusy)
        {
            return;
        }

        if (password != confirmation)
        {
            SetStatus("Password confirmation does not match.", true);
            return;
        }

        IsBusy = true;
        try
        {
            await bootstrap.CreateOwnerAsync(
                Name,
                Username,
                password,
                ToLocale(SelectedLanguage));

            SetStatus("Owner account created.");
            SetupSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            SetStatus(exception.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ToLocale(string display) => display switch
    {
        "دری" => "fa",
        "پښتو" => "ps",
        _ => "en",
    };

    private void SetStatus(string message, bool error = false)
    {
        IsError = error;
        StatusMessage = message;
    }
}
