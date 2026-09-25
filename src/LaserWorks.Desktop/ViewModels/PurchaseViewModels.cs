using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using LaserWorks.Reporting.Core;
using K = LaserWorks.Reporting.Core.ColumnKind;

namespace LaserWorks.Desktop.ViewModels;

public sealed partial class SuppliersViewModel : ListPageViewModel<SupplierRow>
{
    public override string TitleKey => "Purchases.Suppliers";
    protected override AppModule Module => AppModule.Purchases;
    protected override Task<PagedResult<SupplierRow>> FetchAsync(PageRequest r) => Get<SupplierService>().ListAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<SupplierRow> rows)
    {
        var t = new ReportTable().Col("code", "Col.Code").Col("name", "Col.Name", width: 2).Col("phone", "Col.Phone").Col("email", "Col.Email", width: 1.5f).Col("terms", "Col.PaymentTerms", K.Integer).Col("balance", "Col.Balance", K.Money, total: true);
        foreach (var s in rows) t.Add(s.Code, s.Name, s.Phone, s.Email, s.PaymentTermsDays, s.Balance);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new SupplierEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(SupplierRow? r) { if (r != null && await Dialogs.ShowAsync(new SupplierEditorViewModel(r.Id))) await LoadAsync(); }

    [RelayCommand]
    private async Task Delete(SupplierRow? r)
    {
        if (r == null || !await Dialogs.ConfirmAsync(L.Format("Msg.ConfirmDelete", r.Name), danger: true)) return;
        if (await RunAsync(() => Get<SupplierService>().DeleteAsync(r.Id), "Msg.Deleted")) await LoadAsync();
    }

    [RelayCommand] private async Task Pay(SupplierRow? r) { if (r != null && await Dialogs.ShowAsync(new SupplierPaymentDialogViewModel(r.Id))) await LoadAsync(); }
}

public sealed partial class SupplierEditorViewModel : DialogViewModel
{
    private readonly long _id;
    public SupplierEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "Supplier.New" : "Supplier.Edit";
    [ObservableProperty] private string? _code;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _taxNumber;
    [ObservableProperty] private int _paymentTermsDays = 30;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private decimal _openingBalance;
    public bool IsNew => _id == 0;

    public override async Task InitializeAsync()
    {
        if (_id == 0) return;
        var s = await Get<SupplierService>().GetAsync(_id);
        if (s == null) return;
        Code = s.Code; Name = s.Name; Phone = s.Phone; Email = s.Email; Address = s.Address; TaxNumber = s.TaxNumber; PaymentTermsDays = s.PaymentTermsDays; Notes = s.Notes; IsActive = s.IsActive;
    }

    protected override async Task SaveAsync()
    {
        var id = await Get<SupplierService>().SaveAsync(new Supplier { Id = _id, Code = Code ?? "", Name = Name ?? "", Phone = Phone, Email = Email, Address = Address, TaxNumber = TaxNumber, PaymentTermsDays = PaymentTermsDays, Notes = Notes, IsActive = IsActive });
        if (IsNew && OpeningBalance != 0) await Get<AccountingService>().PostPartyOpeningBalanceAsync(null, id, OpeningBalance, Today);
    }
}

public sealed partial class PurchaseOrdersViewModel : ListPageViewModel<PurchaseOrderRow>
{
    public override string TitleKey => "Purchases.Orders";
    protected override AppModule Module => AppModule.Purchases;
    protected override Task<PagedResult<PurchaseOrderRow>> FetchAsync(PageRequest r) => Get<PurchaseService>().ListOrdersAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<PurchaseOrderRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("supplier", "Col.Supplier", width: 2).Col("status", "Col.Status").Col("sub", "Col.Subtotal", K.Money, total: true).Col("tax", "Col.Tax", K.Money, total: true).Col("total", "Col.Total", K.Money, total: true);
        foreach (var p in rows) t.Add(p.Number, p.Date, p.Supplier, p.Status, p.Subtotal, p.TaxAmount, p.Total);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new PurchaseOrderEditorViewModel(0))) await LoadAsync(); }
    [RelayCommand] private async Task Open(PurchaseOrderRow? r) { if (r != null) { await Dialogs.ShowAsync(new PurchaseOrderEditorViewModel(r.Id)); await LoadAsync(); } }
}

public sealed partial class PoLineVm : ObservableObject
{
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private decimal _unitCost;
    [ObservableProperty] private decimal _taxRate;
    [ObservableProperty] private decimal _receivedQuantity;
    public long Id { get; set; }

    partial void OnMaterialChanged(MaterialLookup? value) { if (value != null && UnitCost == 0) UnitCost = value.AverageCost; }
}

public sealed partial class PurchaseOrderEditorViewModel : DialogViewModel
{
    private long _id;
    public PurchaseOrderEditorViewModel(long id) => _id = id;
    public override string TitleKey => _id == 0 ? "PO.New" : "PO.Title";
    public override string Title => _id == 0 ? L[TitleKey] : $"{L[TitleKey]} {Number}";
    public override double DialogWidth => 900;
    public override bool ShowSave => IsDraft;
    [ObservableProperty] private string? _number;
    [ObservableProperty] private PurchaseOrderStatus _status;
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private DateTime? _expectedDate;
    [ObservableProperty] private string? _notes;
    public ObservableCollection<PoLineVm> Lines { get; } = new();
    public List<Lookup> Suppliers { get; private set; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public bool IsDraft => Status == PurchaseOrderStatus.Draft;
    public bool CanApprove => _id != 0 && IsDraft;
    public bool CanReceive => Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived;
    public bool CanCancel => _id != 0 && Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved;

    partial void OnStatusChanged(PurchaseOrderStatus value)
    {
        foreach (var p in new[] { nameof(IsDraft), nameof(CanApprove), nameof(CanReceive), nameof(CanCancel), nameof(ShowSave) }) OnPropertyChanged(p);
    }

    public override async Task InitializeAsync()
    {
        Suppliers = await Get<SupplierService>().LookupAsync();
        Materials = await Get<MaterialService>().LookupAsync();
        OnPropertyChanged(nameof(Suppliers)); OnPropertyChanged(nameof(Materials));
        if (_id == 0) { AddLine(); return; }
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var po = await Get<PurchaseService>().GetOrderAsync(_id);
        if (po == null) return;
        Number = po.Number; Status = po.Status; Supplier = Suppliers.FirstOrDefault(s => s.Id == po.SupplierId) ?? new Lookup(po.SupplierId, po.Supplier!.Code, po.Supplier.Name);
        Date = po.Date; ExpectedDate = po.ExpectedDate; Notes = po.Notes;
        Lines.Clear();
        foreach (var l in po.Lines) Lines.Add(new PoLineVm { Id = l.Id, Material = Materials.FirstOrDefault(m => m.Id == l.MaterialId), Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate, ReceivedQuantity = l.ReceivedQuantity });
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanCancel));
    }

    [RelayCommand] private void AddLine() => Lines.Add(new PoLineVm { TaxRate = Get<SettingsService>().Current.DefaultTaxRate });
    [RelayCommand] private void RemoveLine(PoLineVm? l) { if (l != null) Lines.Remove(l); }

    protected override async Task SaveAsync()
    {
        var po = new PurchaseOrder { Id = _id, SupplierId = Supplier?.Id ?? 0, Date = Date?.Date ?? Today, ExpectedDate = ExpectedDate?.Date, Notes = Notes };
        foreach (var l in Lines) po.Lines.Add(new PurchaseOrderLine { MaterialId = l.Material?.Id ?? 0, Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate });
        _id = await Get<PurchaseService>().SaveOrderAsync(po);
    }

    [RelayCommand]
    private async Task Approve()
    {
        if (!await RunAsync(SaveAsync)) return;
        if (await RunAsync(() => Get<PurchaseService>().SetOrderStatusAsync(_id, PurchaseOrderStatus.Approved), "Msg.Approved")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task CancelOrder()
    {
        if (await RunAsync(() => Get<PurchaseService>().SetOrderStatusAsync(_id, PurchaseOrderStatus.Cancelled), "Msg.Saved")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task Receive()
    {
        if (await Dialogs.ShowAsync(new ReceiptDialogViewModel(_id))) await ReloadAsync();
    }
}

public sealed partial class ReceiptsViewModel : ListPageViewModel<ReceiptRow>
{
    public override string TitleKey => "Purchases.Receipts";
    protected override AppModule Module => AppModule.Purchases;
    protected override Task<PagedResult<ReceiptRow>> FetchAsync(PageRequest r) => Get<PurchaseService>().ListReceiptsAsync(r);
    public override string? RowClass(object row) => row is ReceiptRow { IsInvoiced: false } ? "warning" : null;

    protected override ReportTable BuildExport(IReadOnlyList<ReceiptRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("supplier", "Col.Supplier", width: 2).Col("po", "Col.PurchaseOrder").Col("wh", "Col.Warehouse").Col("value", "Col.Value", K.Money, total: true).Col("inv", "Col.Invoiced");
        foreach (var r in rows) t.Add(r.Number, r.Date, r.Supplier, r.PurchaseOrder, r.Warehouse, r.Subtotal, r.IsInvoiced);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new ReceiptDialogViewModel(null))) await LoadAsync(); }
    [RelayCommand] private async Task Invoice(ReceiptRow? r) { if (r != null && await Dialogs.ShowAsync(new SupplierInvoiceDialogViewModel(r.Id))) await LoadAsync(); }
    [RelayCommand] private async Task Return(ReceiptRow? r) { if (r != null && await Dialogs.ShowAsync(new PurchaseReturnDialogViewModel(r.Id))) await LoadAsync(); }
}

public sealed partial class ReceiptLineVm : ObservableObject
{
    public long? PurchaseOrderLineId { get; init; }
    [ObservableProperty] private MaterialLookup? _material;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _unitCost;
    [ObservableProperty] private decimal _taxRate;
}

public sealed partial class ReceiptDialogViewModel : DialogViewModel
{
    private readonly long? _orderId;
    public ReceiptDialogViewModel(long? orderId) => _orderId = orderId;
    public override string TitleKey => "Receipt.Title";
    public override double DialogWidth => 900;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private Warehouse? _warehouse;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _deliveryRef;
    [ObservableProperty] private string? _notes;
    public ObservableCollection<ReceiptLineVm> Lines { get; } = new();
    public List<Lookup> Suppliers { get; private set; } = new();
    public List<Warehouse> Warehouses { get; private set; } = new();
    public List<MaterialLookup> Materials { get; private set; } = new();
    public bool FromOrder => _orderId != null;

    public override async Task InitializeAsync()
    {
        Suppliers = await Get<SupplierService>().LookupAsync();
        Warehouses = await Get<LookupService>().WarehousesAsync(activeOnly: true);
        Materials = await Get<MaterialService>().LookupAsync();
        foreach (var p in new[] { nameof(Suppliers), nameof(Warehouses), nameof(Materials) }) OnPropertyChanged(p);
        var s = await Get<SettingsService>().GetAsync();
        Warehouse = Warehouses.FirstOrDefault(w => w.Id == s.DefaultWarehouseId) ?? Warehouses.FirstOrDefault();
        if (_orderId is { } oid)
        {
            var r = await Get<PurchaseService>().ReceiptFromOrderAsync(oid);
            Supplier = Suppliers.FirstOrDefault(x => x.Id == r.SupplierId);
            foreach (var l in r.Lines) Lines.Add(new ReceiptLineVm { PurchaseOrderLineId = l.PurchaseOrderLineId, Material = Materials.FirstOrDefault(m => m.Id == l.MaterialId), Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate });
        }
        else AddLine();
    }

    [RelayCommand] private void AddLine() => Lines.Add(new ReceiptLineVm { TaxRate = Get<SettingsService>().Current.DefaultTaxRate });

    protected override Task SaveAsync()
    {
        var r = new PurchaseReceipt { SupplierId = Supplier?.Id ?? 0, PurchaseOrderId = _orderId, WarehouseId = Warehouse?.Id ?? 0, Date = Date?.Date ?? Today, SupplierDeliveryRef = DeliveryRef, Notes = Notes };
        foreach (var l in Lines)
        {
            if (l.Material == null) throw new DomainException("Err.Required", L["Col.Material"]);
            r.Lines.Add(new PurchaseReceiptLine { PurchaseOrderLineId = l.PurchaseOrderLineId, MaterialId = l.Material.Id, Quantity = l.Quantity, UnitCost = l.UnitCost, TaxRate = l.TaxRate });
        }
        return Get<PurchaseService>().PostReceiptAsync(r);
    }
}

public sealed partial class SupplierInvoiceDialogViewModel : DialogViewModel
{
    private readonly long _receiptId;
    public SupplierInvoiceDialogViewModel(long receiptId) => _receiptId = receiptId;
    public override string TitleKey => "SupplierInvoice.Title";
    public override double DialogWidth => 520;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private string? _supplierInvoiceNo;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private string? _receiptInfo;

    public override async Task InitializeAsync()
    {
        var r = await Get<PurchaseService>().GetReceiptAsync(_receiptId);
        if (r != null) ReceiptInfo = $"{r.Number} — {r.Supplier?.Name} — {L.Money(r.Subtotal)}";
    }

    protected override Task SaveAsync() => Get<PurchaseService>().PostSupplierInvoiceFromReceiptAsync(_receiptId, SupplierInvoiceNo, Date?.Date ?? Today, DueDate?.Date);
}

public sealed partial class PurchaseReturnLineVm : ObservableObject
{
    public long ReceiptLineId { get; init; }
    public string Material { get; init; } = "";
    public decimal Received { get; init; }
    public decimal AlreadyReturned { get; init; }
    public decimal UnitCost { get; init; }
    [ObservableProperty] private decimal _quantity;
}

public sealed partial class PurchaseReturnDialogViewModel : DialogViewModel
{
    private readonly long _receiptId;
    public PurchaseReturnDialogViewModel(long receiptId) => _receiptId = receiptId;
    public override string TitleKey => "PurchaseReturn.Title";
    public override double DialogWidth => 800;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _reason;
    public ObservableCollection<PurchaseReturnLineVm> Lines { get; } = new();

    public override async Task InitializeAsync()
    {
        var r = await Get<PurchaseService>().GetReceiptAsync(_receiptId);
        if (r == null) return;
        foreach (var l in r.Lines) Lines.Add(new PurchaseReturnLineVm { ReceiptLineId = l.Id, Material = l.Material?.Name ?? "", Received = l.Quantity, AlreadyReturned = l.ReturnedQuantity, UnitCost = l.UnitCost });
    }

    protected override Task SaveAsync() => Get<PurchaseService>().ReturnToSupplierAsync(_receiptId, Lines.Where(l => l.Quantity > 0).Select(l => new PurchaseReturnLineInput(l.ReceiptLineId, l.Quantity)).ToList(), Date?.Date ?? Today, Reason);
}

public sealed partial class SupplierInvoicesViewModel : ListPageViewModel<SupplierInvoiceRow>
{
    public override string TitleKey => "Purchases.Invoices";
    protected override AppModule Module => AppModule.Purchases;
    [ObservableProperty] private bool _openOnly;
    partial void OnOpenOnlyChanged(bool value) => _ = LoadAsync();
    protected override Task<PagedResult<SupplierInvoiceRow>> FetchAsync(PageRequest r) => Get<PurchaseService>().ListSupplierInvoicesAsync(r, openOnly: OpenOnly);
    public override string? RowClass(object row) => row is SupplierInvoiceRow { Balance: > 0 } s && s.DueDate < Today ? "negative" : null;

    protected override ReportTable BuildExport(IReadOnlyList<SupplierInvoiceRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("sino", "Col.SupplierInvoiceNo").Col("date", "Col.Date", K.Date).Col("due", "Col.DueDate", K.Date).Col("supplier", "Col.Supplier", width: 1.8f)
            .Col("total", "Col.Total", K.Money, total: true).Col("paid", "Col.Paid", K.Money, total: true).Col("balance", "Col.Balance", K.Money, total: true);
        foreach (var i in rows) t.Add(i.Number, i.SupplierInvoiceNo, i.Date, i.DueDate, i.Supplier, i.Total, i.PaidAmount, i.Balance);
        return t;
    }
}

public sealed partial class SupplierPaymentsViewModel : ListPageViewModel<SupplierPaymentRow>
{
    public override string TitleKey => "Purchases.Payments";
    protected override AppModule Module => AppModule.Purchases;
    protected override Task<PagedResult<SupplierPaymentRow>> FetchAsync(PageRequest r) => Get<PurchaseService>().ListSupplierPaymentsAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<SupplierPaymentRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("supplier", "Col.Supplier", width: 1.8f).Col("inv", "Col.Invoice").Col("method", "Col.Method").Col("amount", "Col.Amount", K.Money, total: true);
        foreach (var p in rows) t.Add(p.Number, p.Date, p.Supplier, p.Invoice, p.Method, p.Amount);
        return t;
    }

    [RelayCommand] private async Task New() { if (await Dialogs.ShowAsync(new SupplierPaymentDialogViewModel(null))) await LoadAsync(); }
}

public sealed partial class PurchaseReturnsViewModel : ListPageViewModel<PurchaseReturnRow>
{
    public override string TitleKey => "Purchases.Returns";
    protected override AppModule Module => AppModule.Purchases;
    protected override Task<PagedResult<PurchaseReturnRow>> FetchAsync(PageRequest r) => Get<PurchaseService>().ListReturnsAsync(r);

    protected override ReportTable BuildExport(IReadOnlyList<PurchaseReturnRow> rows)
    {
        var t = new ReportTable().Col("no", "Col.Number").Col("date", "Col.Date", K.Date).Col("supplier", "Col.Supplier", width: 1.8f).Col("receipt", "Col.Receipt").Col("total", "Col.Total", K.Money, total: true).Col("reason", "Col.Reason", width: 1.5f);
        foreach (var p in rows) t.Add(p.Number, p.Date, p.Supplier, p.Receipt, p.Total, p.Reason);
        return t;
    }
}

public sealed partial class SupplierPaymentDialogViewModel : DialogViewModel
{
    private readonly long? _supplierId;
    public SupplierPaymentDialogViewModel(long? supplierId) => _supplierId = supplierId;
    public override string TitleKey => "SupplierPayment.Title";
    public override double DialogWidth => 540;
    public override string SaveKey => "Common.Post";
    [ObservableProperty] private Lookup? _supplier;
    [ObservableProperty] private Lookup? _invoice;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private PaymentMethod _method = PaymentMethod.Bank;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private string? _reference;
    public List<Lookup> Suppliers { get; private set; } = new();
    public ObservableCollection<Lookup> Invoices { get; } = new();
    public IReadOnlyList<PaymentMethod> Methods { get; } = new[] { PaymentMethod.Cash, PaymentMethod.Bank };

    partial void OnSupplierChanged(Lookup? value) => _ = LoadInvoicesAsync();

    partial void OnInvoiceChanged(Lookup? value)
    {
        if (value is { Id: > 0 } && decimal.TryParse(value.Name, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b)) Amount = b;
    }

    private async Task LoadInvoicesAsync()
    {
        Invoices.Clear();
        if (Supplier == null) return;
        Invoices.Add(new Lookup(0, "", L["Payment.OnAccount"]));
        foreach (var i in await Get<PurchaseService>().OpenSupplierInvoicesAsync(Supplier.Id)) Invoices.Add(i);
        Invoice = Invoices.Skip(1).FirstOrDefault() ?? Invoices.First();
    }

    public override async Task InitializeAsync()
    {
        Suppliers = await Get<SupplierService>().LookupAsync();
        OnPropertyChanged(nameof(Suppliers));
        Supplier = Suppliers.FirstOrDefault(s => s.Id == _supplierId) ?? Suppliers.FirstOrDefault();
    }

    protected override Task SaveAsync() => Get<PurchaseService>().PaySupplierAsync(Supplier?.Id ?? 0, Invoice is { Id: > 0 } i ? i.Id : null, Amount, Method, Date?.Date ?? Today, Reference);
}

public sealed partial class PurchasesViewModel : PageViewModel
{
    public override string TitleKey => "Nav.Purchases";
    public SuppliersViewModel Suppliers { get; } = new();
    public PurchaseOrdersViewModel Orders { get; } = new();
    public ReceiptsViewModel Receipts { get; } = new();
    public SupplierInvoicesViewModel Invoices { get; } = new();
    public SupplierPaymentsViewModel Payments { get; } = new();
    public PurchaseReturnsViewModel Returns { get; } = new();
    [ObservableProperty] private int _selectedTab;

    public override async Task LoadAsync()
    {
        await Suppliers.LoadAsync();
        await Orders.LoadAsync();
        await Receipts.LoadAsync();
        await Invoices.LoadAsync();
        await Payments.LoadAsync();
        await Returns.LoadAsync();
    }
}
