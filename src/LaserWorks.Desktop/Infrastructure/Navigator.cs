using LaserWorks.Desktop.ViewModels;

namespace LaserWorks.Desktop;

/// <summary>Page navigation and drill-down to source documents.</summary>
public sealed class Navigator
{
    public ShellViewModel? Shell { get; set; }

    public void Navigate(PageViewModel page) => Shell?.Show(page);

    public void Go(string key) => Shell?.NavigateTo(key);

    /// <summary>Opens the source document of a report row / chart point / ledger line.</summary>
    public async Task OpenAsync(string? sourceType, long? id)
    {
        if (Shell == null || sourceType == null || id is not { } key || key == 0) return;
        var dialogs = ViewModelBase.Get<DialogService>();
        switch (sourceType)
        {
            case "Job": Navigate(new JobDetailViewModel(key)); break;
            case "Customer": Navigate(new CustomerProfileViewModel(key)); break;
            case "Request": await dialogs.ShowAsync(new RequestEditorViewModel(key)); break;
            case "Quotation": await dialogs.ShowAsync(new QuotationEditorViewModel(key)); break;
            case "Invoice": case "SalesInvoice": await dialogs.ShowAsync(new InvoiceEditorViewModel(key)); break;
            case "Material": await dialogs.ShowAsync(new MaterialEditorViewModel(key)); break;
            case "Machine": await dialogs.ShowAsync(new MachineEditorViewModel(key)); break;
            case "Estimate": Navigate(new EstimateEditorViewModel(key)); break;
            case "Journal": await dialogs.ShowAsync(new JournalEditorViewModel(key)); break;
        }
    }
}
