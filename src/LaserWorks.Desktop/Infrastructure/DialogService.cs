using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LaserWorks.Desktop.ViewModels;
using LaserWorks.Localization;
using Serilog;

namespace LaserWorks.Desktop;

/// <summary>Modal overlay dialogs, message boxes, file pickers and OS file actions.</summary>
public sealed class DialogService
{
    public ObservableCollection<ViewModelBase> Stack { get; } = new();

    /// <summary>Test hooks: when set, used instead of the OS file pickers.</summary>
    public Func<string?>? OpenFileOverride { get; set; }
    public Func<string, string?>? SaveFileOverride { get; set; }
    public Func<string, bool>? ConfirmOverride { get; set; }
    public List<string> OpenedFiles { get; } = new();

    public async Task<bool> ShowAsync(DialogViewModel vm)
    {
        await vm.InitializeAsync();
        Stack.Add(vm);
        return await vm.Completion;
    }

    public void Remove(ViewModelBase vm) => Stack.Remove(vm);

    public async Task<bool> ConfirmAsync(string message, string? titleKey = null, bool danger = false)
    {
        if (ConfirmOverride != null) return ConfirmOverride(message);
        var vm = new MessageDialogViewModel(titleKey ?? "Common.Confirm", message, confirm: true, danger);
        Stack.Add(vm);
        return await vm.Completion;
    }

    public async Task AlertAsync(string message, string? titleKey = null)
    {
        if (ConfirmOverride != null) return;
        var vm = new MessageDialogViewModel(titleKey ?? "Common.Information", message, confirm: false, false);
        Stack.Add(vm);
        await vm.Completion;
    }

    public async Task<string?> PromptAsync(string titleKey, string labelKey, string? initial = null)
    {
        if (ConfirmOverride != null) return initial ?? "test";
        var vm = new PromptDialogViewModel(titleKey, labelKey, initial);
        Stack.Add(vm);
        return await vm.Completion ? vm.Value : null;
    }

    private static TopLevel? Top => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> OpenFileAsync(string? filterName = null, params string[] patterns)
    {
        if (OpenFileOverride != null) return OpenFileOverride();
        if (Top?.StorageProvider is not { CanOpen: true } sp) return null;
        var types = patterns.Length > 0 ? new[] { new FilePickerFileType(filterName ?? "Files") { Patterns = patterns } } : null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = types });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string suggestedName, string extension)
    {
        var safe = new string(suggestedName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        if (safe.Length == 0) safe = "export";
        var fileName = $"{safe}_{DateTime.Now:yyyyMMdd_HHmm}.{extension}";
        if (SaveFileOverride != null) return SaveFileOverride(fileName);
        if (Top?.StorageProvider is not { CanSave: true } sp) return null;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = fileName,
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } } }
        });
        return file?.TryGetLocalPath();
    }

    public void OpenFile(string path)
    {
        OpenedFiles.Add(path);
        if (OpenFileOverride != null || SaveFileOverride != null) return; // tests
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open {Path}", path);
            _ = AlertAsync(Loc.Instance.Format("Msg.FileSavedAt", path));
        }
    }

    /// <summary>Sends a PDF to the default printer (Windows "print" verb); falls back to opening it.</summary>
    public void PrintFile(string path)
    {
        OpenedFiles.Add(path);
        if (OpenFileOverride != null || SaveFileOverride != null) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Print verb failed for {Path}; opening instead", path);
        }
        OpenFile(path);
    }
}
