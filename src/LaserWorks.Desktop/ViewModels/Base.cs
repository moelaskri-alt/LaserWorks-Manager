using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;
using LaserWorks.Localization;
using LaserWorks.Reporting.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LaserWorks.Desktop.ViewModels;

public static class Errors
{
    /// <summary>Turns an exception into a friendly localized message.</summary>
    public static string Describe(Exception ex)
    {
        var L = Loc.Instance;
        if (ex is DomainException de)
        {
            var args = de.Args.Select(a => a switch
            {
                Enum e => (object)L.Enum(e),
                decimal d => L.Number(d),
                string s when L.Has("Field." + s) => L["Field." + s],
                _ => a
            }).ToArray();
            if (L.Has(de.Code)) return L.Format(de.Code, args);
            return de.Message;
        }
        if (ex is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Microsoft.Data.Sqlite.SqliteException se })
        {
            if (se.SqliteErrorCode == 19) return L["Err.ConstraintViolation"];
            return L["Err.Database"] + ": " + se.Message;
        }
        return L["Err.Unexpected"] + ": " + ex.Message;
    }
}

public abstract partial class ViewModelBase : ObservableObject
{
    protected static Loc L => Loc.Instance;
    public static T Get<T>() where T : notnull => App.Services.GetRequiredService<T>();
    protected static DialogService Dialogs => Get<DialogService>();
    protected static Navigator Nav => Get<Navigator>();
    protected static int Decimals => Get<SettingsService>().Current.DecimalPlaces;
    protected static DateTime Today => Get<LaserWorks.Application.Abstractions.IClock>().Now.Date;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public bool Can(AppModule module, Permission permission) => Get<PermissionService>().Has(module, permission);

    /// <summary>Runs an action, showing a friendly error instead of crashing; returns true on success.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successKey = null)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;
            await action();
            if (successKey != null) InfoMessage = L[successKey];
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not DomainException) Log.Error(ex, "Operation failed in {Vm}", GetType().Name);
            ErrorMessage = Errors.Describe(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }
}

public abstract class PageViewModel : ViewModelBase
{
    public abstract string TitleKey { get; }
    public string Title => L[TitleKey];
    public virtual Task LoadAsync() => Task.CompletedTask;
    /// <summary>Height below which the page scrolls as a whole instead of shrinking its grids (see ViewportFill).</summary>
    public virtual double MinPageHeight => 520;
}

public interface ISortableList
{
    void Sort(string? memberPath);
}

public interface IRowClassifier
{
    string? RowClass(object row);
}

/// <summary>Shared surface of list pages used by the toolbar and pager controls.</summary>
public interface IListPage
{
    string? Search { get; set; }
    string PageInfo { get; }
    int PageSize { get; set; }
    IReadOnlyList<int> PageSizes { get; }
    bool CanExport { get; }
    bool CanPrint { get; }
    bool IsBusy { get; }
    string? ErrorMessage { get; }
    string? InfoMessage { get; }
    System.Windows.Input.ICommand RefreshCommand { get; }
    System.Windows.Input.ICommand NextPageCommand { get; }
    System.Windows.Input.ICommand PrevPageCommand { get; }
    System.Windows.Input.ICommand FirstPageCommand { get; }
    System.Windows.Input.ICommand LastPageCommand { get; }
    System.Windows.Input.ICommand ExportCommand { get; }
    System.Windows.Input.ICommand PrintCommand { get; }
    System.Windows.Input.ICommand DismissMessagesCommand { get; }
}

/// <summary>Server-side paged, searchable, sortable list with export and print.</summary>
public abstract partial class ListPageViewModel<TRow> : PageViewModel, ISortableList, IRowClassifier, IListPage where TRow : class
{
    System.Windows.Input.ICommand IListPage.RefreshCommand => RefreshCommand;
    System.Windows.Input.ICommand IListPage.NextPageCommand => NextPageCommand;
    System.Windows.Input.ICommand IListPage.PrevPageCommand => PrevPageCommand;
    System.Windows.Input.ICommand IListPage.FirstPageCommand => FirstPageCommand;
    System.Windows.Input.ICommand IListPage.LastPageCommand => LastPageCommand;
    System.Windows.Input.ICommand IListPage.ExportCommand => ExportCommand;
    System.Windows.Input.ICommand IListPage.PrintCommand => PrintCommand;
    System.Windows.Input.ICommand IListPage.DismissMessagesCommand => DismissMessagesCommand;

    private CancellationTokenSource? _searchDelay;

    public ObservableCollection<TRow> Items { get; } = new();
    public IReadOnlyList<int> PageSizes { get; } = new[] { 25, 50, 100, 200 };

    [ObservableProperty] private TRow? _selected;
    [ObservableProperty] private string? _search;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageSize = 50;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _pageCount = 1;
    private string? _sortBy;
    private bool _sortDesc;

    protected abstract AppModule Module { get; }
    public bool CanCreate => Can(Module, Permission.Create);
    public bool CanEdit => Can(Module, Permission.Edit);
    public bool CanDelete => Can(Module, Permission.Delete);
    public bool CanExport => Can(Module, Permission.Export) || Can(AppModule.Reports, Permission.Export);
    public bool CanPrint => Can(Module, Permission.Print) || Can(AppModule.Reports, Permission.Print);
    public string PageInfo => L.Format("List.PageInfo", Page, PageCount, TotalCount);

    protected abstract Task<PagedResult<TRow>> FetchAsync(PageRequest request);

    /// <summary>Export/print layout for the current filter (all pages). Null disables export.</summary>
    protected virtual ReportTable? BuildExport(IReadOnlyList<TRow> rows) => null;

    public virtual string? RowClass(object row) => null;

    partial void OnSearchChanged(string? value)
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        _ = Task.Delay(350, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => { Page = 1; _ = LoadAsync(); });
        }, TaskScheduler.Default);
    }

    partial void OnPageSizeChanged(int value)
    {
        Page = 1;
        _ = LoadAsync();
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var result = await FetchAsync(new PageRequest(Search, Page, PageSize, _sortBy, _sortDesc));
            Items.Clear();
            foreach (var r in result.Items) Items.Add(r);
            TotalCount = result.TotalCount;
            PageCount = result.PageCount;
            if (Page > PageCount) Page = PageCount;
            OnPropertyChanged(nameof(PageInfo));
            await AfterLoadAsync();
        });
    }

    protected virtual Task AfterLoadAsync() => Task.CompletedTask;

    public void Sort(string? memberPath)
    {
        if (string.IsNullOrEmpty(memberPath)) return;
        _sortDesc = _sortBy == memberPath && !_sortDesc;
        _sortBy = memberPath;
        _ = LoadAsync();
    }

    [RelayCommand] private Task Refresh() => LoadAsync();
    [RelayCommand] private Task NextPage() { if (Page < PageCount) { Page++; return LoadAsync(); } return Task.CompletedTask; }
    [RelayCommand] private Task PrevPage() { if (Page > 1) { Page--; return LoadAsync(); } return Task.CompletedTask; }
    [RelayCommand] private Task FirstPage() { Page = 1; return LoadAsync(); }
    [RelayCommand] private Task LastPage() { Page = PageCount; return LoadAsync(); }

    protected async Task<ReportTable?> ExportTableAsync()
    {
        var all = await FetchAsync(new PageRequest(Search, 1, 100_000, _sortBy, _sortDesc));
        var table = BuildExport(all.Items);
        if (table != null) table.Title = Title;
        return table;
    }

    [RelayCommand]
    private async Task Export(string format)
    {
        await RunAsync(async () =>
        {
            var table = await ExportTableAsync() ?? throw new DomainException("Err.ExportNotAvailable");
            var path = await Dialogs.SaveFileAsync(Title, format);
            if (path == null) return;
            await ReportFiles.SaveAsync(table, format, path);
            InfoMessage = L.Format("Msg.Exported", Path.GetFileName(path));
        });
    }

    [RelayCommand]
    private async Task Print()
    {
        await RunAsync(async () =>
        {
            var table = await ExportTableAsync() ?? throw new DomainException("Err.ExportNotAvailable");
            await ReportFiles.PrintAsync(table);
        });
    }
}

/// <summary>Base for modal editors shown in the dialog overlay.</summary>
public abstract partial class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public abstract string TitleKey { get; }
    public virtual string Title => L[TitleKey];
    public virtual double DialogWidth => 640;
    public virtual bool ShowSave => true;
    public virtual string SaveKey => "Common.Save";
    public Task<bool> Completion => _tcs.Task;

    protected abstract Task SaveAsync();

    [RelayCommand]
    private async Task Save()
    {
        if (await RunAsync(SaveAsync)) Close(true);
    }

    [RelayCommand]
    public void Cancel() => Close(false);

    protected void Close(bool result)
    {
        Get<DialogService>().Remove(this);
        _tcs.TrySetResult(result);
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;
}

public static class ReportFiles
{
    public static async Task SaveAsync(ReportTable table, string format, string path)
    {
        var company = await ViewModelBase.Get<SettingsService>().GetAsync();
        await Task.Run(() =>
        {
            switch (format)
            {
                case "pdf": PdfExporter.Export(table, company, path); break;
                case "xlsx": ExcelExporter.Export(table, company, path); break;
                default: CsvExporter.Export(table, path); break;
            }
        });
    }

    public static async Task PrintAsync(ReportTable table)
    {
        var company = await ViewModelBase.Get<SettingsService>().GetAsync();
        var path = Path.Combine(ViewModelBase.Get<LaserWorks.Application.Abstractions.IAppPaths>().ExportsDirectory, $"print_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        await Task.Run(() => PdfExporter.Export(table, company, path));
        ViewModelBase.Get<DialogService>().PrintFile(path);
    }

    public static async Task PrintBytesAsync(byte[] pdf, string name)
    {
        var path = Path.Combine(ViewModelBase.Get<LaserWorks.Application.Abstractions.IAppPaths>().ExportsDirectory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        ViewModelBase.Get<DialogService>().PrintFile(path);
    }
}
