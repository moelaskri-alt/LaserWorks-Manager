using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class MessageDialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public MessageDialogViewModel(string titleKey, string message, bool confirm, bool danger)
    {
        Title = L[titleKey];
        Message = message;
        IsConfirm = confirm;
        IsDanger = danger;
    }

    public string Title { get; }
    public string Message { get; }
    public bool IsConfirm { get; }
    public bool IsDanger { get; }
    public Task<bool> Completion => _tcs.Task;

    [RelayCommand]
    private void Yes()
    {
        Get<DialogService>().Remove(this);
        _tcs.TrySetResult(true);
    }

    [RelayCommand]
    private void No()
    {
        Get<DialogService>().Remove(this);
        _tcs.TrySetResult(false);
    }
}

public sealed partial class PromptDialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public PromptDialogViewModel(string titleKey, string labelKey, string? initial)
    {
        Title = L[titleKey];
        Label = L[labelKey];
        Value = initial;
    }

    public string Title { get; }
    public string Label { get; }
    [ObservableProperty] private string? _value;
    public Task<bool> Completion => _tcs.Task;

    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(Value)) { ErrorMessage = L.Format("Err.Required", Label); return; }
        Get<DialogService>().Remove(this);
        _tcs.TrySetResult(true);
    }

    [RelayCommand]
    private void CancelPrompt()
    {
        Get<DialogService>().Remove(this);
        _tcs.TrySetResult(false);
    }
}
