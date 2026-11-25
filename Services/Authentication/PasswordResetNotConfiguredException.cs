namespace VietVan.API.Services.Authentication;

public sealed class PasswordResetNotConfiguredException()
    : Exception("Email:PasswordResetUrl is invalid.")
{
}
