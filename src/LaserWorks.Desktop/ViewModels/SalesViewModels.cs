using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class InvoicesViewModel : ListPageViewModel<InvoiceRow>
{
    public override string TitleKey => "Sales.Invoices";
    protected override AppModule Module => AppModule.Sales;
    public List<Option<DocumentStatus>> Statuses { get; } = Options.ForEnum<DocumentStatus>();
    [ObservableProperty] private Option<DocumentStatus>? _status;
    [ObservableProperty] private bool _openOnly;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;

    public InvoicesViewModel(DateRange? range = null, bool openOnly = false)
    {
        _status = Statuses[0];
        _openOnly = openOnly;
        _from = range?.From;
        _to = range?.To;
    }

    partial void OnStatusChanged(Option<DocumentStatus>? value) => _ = LoadAsync();
    partial void OnOpenOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToChanged(DateTime? value) => _ = LoadAsync();

    private DateRange? Range => From == null && To == null ? null : new DateRange(From?.Date ?? new DateTime(2000, 1, 1), To?.Date ?? new DateTime(2999, 12, 31));

    protected override Task<PagedResult<InvoiceRow>> FetchAsync(PageRequest r) => Get<SalesService>().ListInvoicesAsync(r, Status?.Value, range: Range, openOnly: OpenOnly);

    public override string? RowClass(object row) => row switch
    {
        InvoiceRow i when i.IsOverdue(Today) => "negative",
        InvoiceRow { Status: DocumentStatus.Draft } => "warning",
        _ => null
    };

    protected override ReportTable BuildExport(IReadOnlyList<InvoiceRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Invoice").Col("date", "Col.Date", K.Date).Col("due", "Col.DueDate", K.Date).Col("customer", "Col.Customer", width: 1.6f).Col("job", "Col.Job").Col("status", "Col.Status")
            .Col("net", "Col.NetRevenue", K.Money, total: true).Col("tax", "Col.Tax", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true).Col("paid", "Col.Paid", K.Money, total: true).Col("balance", "Col.Balance", K.Money, total: true);
        foreach (var i in rows) t.Add(i.Number, i.Date, i.DueDate, i.Customer, i.JobNumber, i.Status, i.NetRevenue, i.TaxAmount, i.Total, i.PaidAmount, i.Balance);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new InvoiceEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(InvoiceRow? row) { if (row != null) { await Dialogs.ShowAsync(new InvoiceEditorViewModel(row.Id)); await LoadAsync(); } }
    [RelayCommand] private async Task Pay(InvoiceRow? row) { if (row != null && await Dialogs.ShowAsync(new PaymentEditorViewModel(row.CustomerId, row.Id))) await LoadAsync(); }
    [RelayCommand] private async Task Return(InvoiceRow? row) { if (row != null && await Dialogs.ShowAsync(new SalesReturnDialogViewModel(row.Id))) await LoadAsync(); }
}

public sealed partial class PaymentsViewModel : ListPageViewModel<PaymentRow>
{
    public override string TitleKey => "Sales.Payments";
    protected override AppModule Module => AppModule.Sales;
    protected override Task<PagedResult<PaymentRow>> FetchAsync(PageRequest r) => Get<SalesService>().ListPaymentsAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<PaymentRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.8f).Col("invoice", "Col.Invoice").Col("method", "Col.Method").Col("ref", "Col.Reference").Col("amount", "Col.Amount", K.Money, total: true);
        foreach (var p in rows) t.Add(p.Number, p.Date, p.Customer, p.InvoiceNumber, p.Method, p.Reference, p.Amount);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new PaymentEditorViewModel(null, null))) await LoadAsync(); }
}

public sealed partial class SalesReturnsViewModel : ListPageViewModel<ReturnRow>
{
    public override string TitleKey => "Sales.Returns";
    protected override AppModule Module => AppModule.Sales;
    protected override Task<PagedResult<ReturnRow>> FetchAsync(PageRequest r) => Get<SalesService>().ListReturnsAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<ReturnRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("customer", "Col.Customer", width: 1.6f).Col("invoice", "Col.Invoice").Col("net", "Col.NetAmount", K.Money, total: true)
            .Col("tax", "Col.Tax", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true).Col("cogs", "Col.CogsReversed", K.Money, total: true).Col("reason", "Col.Reason", width: 1.6f);
        foreach (var r in rows) t.Add(r.Number, r.Date, r.Customer, r.InvoiceNumber, r.Subtotal, r.TaxAmount, r.Total, r.CogsReversed, r.Reason);
        return t;
    }
}

public sealed partial class SalesViewModel : PageViewModel
{
    public SalesViewModel(DateRange? range = null, bool openOnly = false) => Invoices = new InvoicesViewModel(range, openOnly);
    public override string TitleKey => "Nav.Sales";
    public InvoicesViewModel Invoices { get; }
    public PaymentsViewModel Payments { get; } = new();
    public SalesReturnsViewModel Returns { get; } = new();
    [ObservableProperty] private int _selectedTab;

    public override async Task LoadAsync()
    {
        await Invoices.LoadAsync();
        await Payments.LoadAsync();
        await Returns.LoadAsync();
    }
}

public sealed partial class InvoiceLineVm : ObservableObject
{
    public InvoiceLineVm(InvoiceEditorViewModel owner) => Owner = owner;
    public InvoiceEditorViewModel Owner { get; }
    public long Id { get; set; }
    [ObservableProperty] private InvoiceLineType _lineType = InvoiceLineType.Service;
    [ObservableProperty] private Lookup? _job;
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private decimal _discountAmount;
    [ObservableProperty] private decimal _taxRate;
    [ObservableProperty] private decimal _lineTotal;
    [ObservableProperty] private decimal _returnedQuantity;
    [ObservableProperty] private decimal _unitCost;
    public bool IsStock => LineType == InvoiceLineType.StockItem;
    public bool IsJob => LineType == InvoiceLineType.Job;

    partial void OnLineTypeChanged(InvoiceLineType value) { OnPropertyChanged(nameof(IsStock)); OnPropertyChanged(nameof(IsJob)); }

    partial void OnMaterialChanged(MaterialLookup? value)
    {
        if (value == null) return;
        Description ??= value.Name;
        if (UnitPrice == 0) UnitPrice = value.SalesPrice;
    }

    partial void OnQuantityChanged(decimal value) => Owner.Recalc();
    partial void OnUnitPriceChanged(decimal value) => Owner.Recalc();
    partial void OnDiscountAmountChanged(decimal value) => Owner.Recalc();
    partial void OnTaxRateChanged(decimal value) => Owner.Recalc();

    [RelayCommand] private void Remove() => Owner.RemoveLine(this);
}

public sealed partial class InvoiceEditorViewModel : DialogViewModel
{
    private long _id;
    public InvoiceEditorViewModel(long id) => _id = id;

    public override string TitleKey => _id == 0 ? "Invoice.New" : "Invoice.Title";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} {Number}";
    public override double DialogWidth => 1080;
    public override bool ShowSave => IsDraft;

    [ObservableProperty] private string? _number;
    [ObservableProperty] private DocumentStatus _status;
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private DateTime? _dueDate = DateTime.Today;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _discount;
    [ObservableProperty] private decimal _tax;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private decimal _paid;
    [ObservableProperty] private decimal _returned;
    [ObservableProperty] private decimal _cogs;
    [ObservableProperty] private bool _ignoreCreditLimit;
    public long? JobId { get; private set; }
    public long? QuotationId { get; private set; }
    public ObservableCollection<InvoiceLineVm> Lines { get; } = new();
    public List<Lookup> Customers { get; private set; } = new();
    public List<Lookup> Jobs { get; private set; } = new();
    public List<MaterialLookup> StockItems { get; private set; } = new();
    public IReadOnlyList<InvoiceLineType> LineTypes { get; } = Enum.GetValues<InvoiceLineType>();
    public bool IsDraft => Status == DocumentStatus.Draft;
    public bool IsPosted => Status == DocumentStatus.Posted;
    public decimal Balance => Total - Paid - Returned;
    public decimal GrossProfit => Subtotal - Discount - Cogs;

    partial void OnStatusChanged(DocumentStatus value) { OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsPosted)); OnPropertyChanged(nameof(ShowSave)); }

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync(activeOnly: false);
        Jobs = await Get<JobService>().LookupAsync(openOnly: false);
        StockItems = await Get<MaterialService>().LookupAsync();
        foreach (var p in new[] { nameof(Customers), nameof(Jobs), nameof(StockItems) }) OnPropertyChanged(p);
        if (_id == 0)
        {
            AddLine();
            return;
        }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var inv = await Get<SalesService>().GetInvoiceAsync(_id);
        if (inv == null) return;
        Number = inv.Number; Status = inv.Status; Customer = Customers.FirstOrDefault(c => c.Id == inv.CustomerId); Date = inv.Date; DueDate = inv.DueDate; Notes = inv.Notes;
        JobId = inv.JobId; QuotationId = inv.QuotationId; Paid = inv.PaidAmount; Returned = inv.ReturnedAmount; Cogs = inv.CogsAmount;
        Lines.Clear();
        foreach (var l in inv.Lines)
            Lines.Add(new InvoiceLineVm(this)
            {
                Id = l.Id, LineType = l.LineType, Job = Jobs.FirstOrDefault(j => j.Id == l.JobId), Material = StockItems.FirstOrDefault(m => m.Id == l.MaterialId), Description = l.Description,
                Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, TaxRate = l.TaxRate, ReturnedQuantity = l.ReturnedQuantity, UnitCost = l.UnitCost
            });
        Recalc();
        OnPropertyChanged(nameof(Title));
    }

    public void Recalc()
    {
        foreach (var l in Lines)
        {
            var gross = Math.Round(l.Quantity * l.UnitPrice, Decimals, MidpointRounding.AwayFromZero);
            var net = gross - l.DiscountAmount;
            l.LineTotal = net + Math.Round(net * l.TaxRate / 100m, Decimals, MidpointRounding.AwayFromZero);
        }
        Subtotal = Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, Decimals, MidpointRounding.AwayFromZero));
        Discount = Lines.Sum(l => l.DiscountAmount);
        Tax = Lines.Sum(l => Math.Round((Math.Round(l.Quantity * l.UnitPrice, Decimals, MidpointRounding.AwayFromZero) - l.DiscountAmount) * l.TaxRate / 100m, Decimals, MidpointRounding.AwayFromZero));
        Total = Subtotal - Discount + Tax;
        OnPropertyChanged(nameof(Balance));
        OnPropertyChanged(nameof(GrossProfit));
    }

    [RelayCommand]
    private void AddLine()
    {
        var rate = Get<SettingsService>().Current.DefaultTaxRate;
        Lines.Add(new InvoiceLineVm(this) { TaxRate = rate });
    }

    public void RemoveLine(InvoiceLineVm l) { Lines.Remove(l); Recalc(); }

    private SalesInvoice ToEntity()
    {
        var inv = new SalesInvoice { Id = _id, CustomerId = Customer?.Id ?? 0, Date = Date?.Date ?? Today, DueDate = DueDate?.Date ?? Today, Notes = Notes, JobId = JobId, QuotationId = QuotationId };
        foreach (var l in Lines)
            inv.Lines.Add(new SalesInvoiceLine
            {
                LineType = l.LineType, JobId = l.IsJob ? l.Job?.Id : null, MaterialId = l.IsStock ? l.Material?.Id : null, Description = l.Description ?? "", Quantity = l.Quantity,
                UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount, TaxRate = l.TaxRate
            });
        return inv;
    }

    protected override async Task SaveAsync() => _id = await Get<SalesService>().SaveInvoiceAsync(ToEntity());

    [RelayCommand]
    private async Task Post()
    {
        if (IsDraft && !await RunAsync(SaveAsync)) return;
        if (!await Dialogs.ConfirmAsync(L["Invoice.ConfirmPost"])) return;
        if (await RunAsync(() => Get<SalesService>().PostInvoiceAsync(_id, IgnoreCreditLimit), "Msg.Posted")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task RecordPayment()
    {
        if (Customer == null) return;
        if (await Dialogs.ShowAsync(new PaymentEditorViewModel(Customer.Id, _id))) await ReloadAsync();
    }

    [RelayCommand]
    private async Task CreateReturn()
    {
        if (await Dialogs.ShowAsync(new SalesReturnDialogViewModel(_id))) await ReloadAsync();
    }

    [RelayCommand]
    private async Task Pdf(string mode)
    {
        await RunAsync(async () =>
        {
            if (IsDraft) await SaveAsync();
            var inv = await Get<SalesService>().GetInvoiceAsync(_id) ?? throw new DomainException("Err.NotFound");
            var company = await Get<SettingsService>().GetAsync();
            var bytes = await Task.Run(() => DocumentRenderer.Invoice(inv, company));
            if (mode == "print") { await ReportFiles.PrintBytesAsync(bytes, $"Invoice_{inv.Number}"); return; }
            var path = await Dialogs.SaveFileAsync($"{L["Doc.Invoice"]}_{inv.Number}", "pdf");
            if (path == null) return;
            await File.WriteAllBytesAsync(path, bytes);
            InfoMessage = L.Format("Msg.Exported", Path.GetFileName(path));
            Dialogs.OpenFile(path);
        });
    }
}

public sealed partial class PaymentEditorViewModel : DialogViewModel
{
    private readonly long? _customerId;
    private readonly long? _invoiceId;

    public PaymentEditorViewModel(long? customerId, long? invoiceId) { _customerId = customerId; _invoiceId = invoiceId; }

    public override string TitleKey => "Payment.Record";
    public override double DialogWidth => 560;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private Lookup? _customer;
    [ObservableProperty] private Lookup? _invoice;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private PaymentMethod _method = PaymentMethod.Bank;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _reference;
    [ObservableProperty] private decimal _customerBalance;
    public List<Lookup> Customers { get; private set; } = new();
    public ObservableCollection<Lookup> Invoices { get; } = new();
    public IReadOnlyList<PaymentMethod> Methods { get; } = new[] { PaymentMethod.Cash, PaymentMethod.Bank };

    partial void OnCustomerChanged(Lookup? value) => _ = LoadInvoicesAsync();

    partial void OnInvoiceChanged(Lookup? value)
    {
        if (value != null && value.Id != 0 && decimal.TryParse(value.Name, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var bal)) Amount = bal;
    }

    private async Task LoadInvoicesAsync()
    {
        Invoices.Clear();
        if (Customer == null) return;
        Invoices.Add(new Lookup(0, "", L["Payment.OnAccount"]));
        foreach (var i in await Get<SalesService>().OpenInvoicesAsync(Customer.Id)) Invoices.Add(i);
        CustomerBalance = await Get<CustomerService>().BalanceAsync(Customer.Id);
        Invoice = Invoices.FirstOrDefault(i => i.Id == _invoiceId) ?? Invoices.Skip(1).FirstOrDefault() ?? Invoices.First();
    }

    public override async Task InitializeAsync()
    {
        Customers = await Get<CustomerService>().LookupAsync(activeOnly: false);
        OnPropertyChanged(nameof(Customers));
        Customer = Customers.FirstOrDefault(c => c.Id == _customerId);
    }

    protected override Task SaveAsync() => Get<SalesService>().RecordPaymentAsync(Customer?.Id ?? 0, Invoice is { Id: > 0 } i ? i.Id : null, Amount, Method, Date?.Date ?? Today, Reference);
}

public sealed partial class ReturnLineVm : ObservableObject
{
    public long InvoiceLineId { get; init; }
    public string Description { get; init; } = "";
    public decimal Sold { get; init; }
    public decimal AlreadyReturned { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal UnitCost { get; init; }
    public bool IsStock { get; init; }
    public decimal Remaining => Sold - AlreadyReturned;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private bool _restock = true;
}

public sealed partial class SalesReturnDialogViewModel : DialogViewModel
{
    private readonly long _invoiceId;
    public SalesReturnDialogViewModel(long invoiceId) => _invoiceId = invoiceId;
    public override string TitleKey => "Return.Title";
    public override double DialogWidth => 900;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private string? _invoiceNumber;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _reason;
    [ObservableProperty] private PaymentMethod _refundMethod = PaymentMethod.OnCredit;
    public ObservableCollection<ReturnLineVm> Lines { get; } = new();
    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>();

    public override async Task InitializeAsync()
    {
        var inv = await Get<SalesService>().GetInvoiceAsync(_invoiceId);
        if (inv == null) return;
        InvoiceNumber = inv.Number;
        foreach (var l in inv.Lines)
            Lines.Add(new ReturnLineVm { InvoiceLineId = l.Id, Description = l.Description, Sold = l.Quantity, AlreadyReturned = l.ReturnedQuantity, UnitPrice = l.UnitPrice, UnitCost = l.UnitCost, IsStock = l.LineType == InvoiceLineType.StockItem, Restock = l.LineType == InvoiceLineType.StockItem });
    }

    protected override Task SaveAsync() => Get<SalesService>().CreateReturnAsync(_invoiceId,
        Lines.Where(l => l.Quantity > 0).Select(l => new ReturnLineInput(l.InvoiceLineId, l.Quantity, l.Restock && l.IsStock, null)).ToList(), Date?.Date ?? Today, Reason, RefundMethod);
}
