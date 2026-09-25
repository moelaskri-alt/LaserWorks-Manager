using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;

    public LoginViewModel(MainWindowViewModel main) => _main = main;

    [ObservableProperty] private string? _username;
    [ObservableProperty] private string? _password;

    public string CompanyName => Get<SettingsService>().Current.CompanyName;
    public string Version => "v" + (typeof(LoginViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    [RelayCommand]
    private async Task Login()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = L["Login.Required"];
            return;
        }
        await RunAsync(async () =>
        {
            var (result, until) = await Get<AuthService>().LoginAsync(Username!, Password!);
            switch (result)
            {
                case LoginResult.Success:
                    Password = null;
                    _main.ShowShell();
                    if (await MustChangePasswordAsync()) await Dialogs.ShowAsync(new ChangePasswordViewModel(forced: true));
                    break;
                case LoginResult.LockedOut:
                    ErrorMessage = L.Format("Login.LockedOut", until?.ToString("HH:mm") ?? "");
                    break;
                case LoginResult.Inactive:
                    ErrorMessage = L["Login.Inactive"];
                    break;
                default:
                    ErrorMessage = L["Login.Invalid"];
                    break;
            }
        });
    }

    private static async Task<bool> MustChangePasswordAsync()
    {
        var session = Get<UserSession>();
        var users = await Get<AuthService>().ListUsersAsync();
        await using var db = Get<IAppDbFactory>().Create();
        var u = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Users, x => x.Id == session.UserId);
        return u?.MustChangePassword == true && users.Count > 0;
    }

    [RelayCommand]
    private void ToggleLanguage() => App.ApplyLanguage(L.IsRightToLeft ? "en" : "ar");
}

public sealed partial class ChangePasswordViewModel : DialogViewModel
{
    public ChangePasswordViewModel(bool forced = false) => Forced = forced;

    public bool Forced { get; }
    public override string TitleKey => "Account.ChangePassword";
    public override double DialogWidth => 420;

    [ObservableProperty] private string? _currentPassword;
    [ObservableProperty] private string? _newPassword;
    [ObservableProperty] private string? _confirmPassword;

    protected override async Task SaveAsync()
    {
        if (NewPassword != ConfirmPassword) throw new LaserWorks.Domain.Common.DomainException("Err.PasswordMismatch");
        if (!PasswordHasher.MeetsPolicy(NewPassword ?? "")) throw new LaserWorks.Domain.Common.DomainException("Err.PasswordPolicy");
        await Get<AuthService>().ChangePasswordAsync(CurrentPassword ?? "", NewPassword!);
    }
}
