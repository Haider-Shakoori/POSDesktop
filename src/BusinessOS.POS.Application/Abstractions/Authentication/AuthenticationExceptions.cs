namespace BusinessOS.POS.Application.Abstractions.Authentication;

public sealed class InvalidCredentialsException()
    : Exception("The username or password is incorrect, or the account is inactive.");

public sealed class LoginThrottledException(int secondsRemaining)
    : Exception("Too many sign-in attempts. Try again in " + secondsRemaining + " seconds.")
{
    public int SecondsRemaining { get; } = secondsRemaining;
}

public sealed class PermissionDeniedException(string permission)
    : Exception("Permission '" + permission + "' is required.")
{
    public string Permission { get; } = permission;
}
