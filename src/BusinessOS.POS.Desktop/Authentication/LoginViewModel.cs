using BusinessOS.POS.Application.Abstractions.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Authentication;

public sealed partial class LoginViewModel(IUserSessionService sessions) : ObservableObject
{
    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isError;

    [ObservableProperty]
    private string statusMessage = "Sign in with your local POS account.";

    public event EventHandler? LoginSucceeded;

    public bool IsNotBusy => !IsBusy;
    public string SignInButtonText => IsBusy ? "Signing in…" : "Sign In";

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(SignInButtonText));
    }

    public async Task SignInAsync(string password)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Username and password are required.", true);
            return;
        }

        IsBusy = true;
        SetStatus("Signing in…");

        try
        {
            var user = await sessions.LoginAsync(Username, password);
            SetStatus("Welcome, " + user.Name + ".");
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (LoginThrottledException exception)
        {
            SetStatus(exception.Message, true);
        }
        catch (InvalidCredentialsException exception)
        {
            SetStatus(exception.Message, true);
        }
        catch
        {
            SetStatus("Sign-in could not be completed.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        IsError = error;
        StatusMessage = message;
    }
}
