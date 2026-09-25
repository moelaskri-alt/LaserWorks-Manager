using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaserWorks.Application.Services;
using LaserWorks.Infrastructure;
using LaserWorks.Localization;
using Serilog;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase? _content;
    [ObservableProperty] private string? _startupError;

    public MainWindowViewModel()
    {
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(IsRightToLeft));
    }

    public ObservableCollection<ViewModelBase> DialogStack => Get<DialogService>().Stack;
    public bool IsRightToLeft => Loc.Instance.IsRightToLeft;
    public string AppTitle => "LaserWorks Manager";

    public async Task StartAsync()
    {
        try
        {
            IsBusy = true;
            await Task.Run(async () => await App.Services.InitializeDatabaseAsync());
            var settings = await Get<SettingsService>().GetAsync();
            App.ApplyLanguage(settings.Language);
            App.ApplyTheme(settings.Theme);
            await ShowStartScreenAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            StartupError = Errors.Describe(ex) + Environment.NewLine + ex;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ShowStartScreenAsync()
    {
        var setup = Get<SetupService>();
        if (!await setup.IsSetupCompletedAsync())
            Content = new SetupWizardViewModel(this);
        else
            Content = new LoginViewModel(this);
    }

    public void ShowShell()
    {
        var shell = new ShellViewModel(this);
        Get<Navigator>().Shell = shell;
        Content = shell;
        _ = shell.InitializeAsync();
    }
}
