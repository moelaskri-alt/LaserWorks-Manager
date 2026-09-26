using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Domain.Common;
using LaserWorks.Domain.Entities;
using LaserWorks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Services;

/// <summary>
/// Generates a realistic demo company by driving the real services (so every stock movement, job cost and journal entry is genuine).
/// </summary>
public sealed class DemoDataService
{
    private readonly ServiceContext _ctx;
    private readonly IServiceProvider _sp;

    public DemoDataService(ServiceContext ctx, IServiceProvider sp)
    {
        _ctx = ctx;
        _sp = sp;
    }

    private T S<T>() where T : notnull => (T)(_sp.GetService(typeof(T)) ?? throw new InvalidOperationException(typeof(T).Name));

    private sealed record JobTemplate(string Description, int Customer, int Material, decimal PieceL, decimal PieceW, decimal PiecesPerUnit, decimal Qty, decimal MinutesPerUnit,
        decimal LaborMinutes, OperationType LaborOp, decimal DesignHours, decimal PackagingPerUnit, string Stage, int DayOffset, bool Engrave = false);

    public async Task<bool> HasBusinessDataAsync()
    {
        await using var db = _ctx.Factory.Create();
        return await db.Jobs.AnyAsync() || await db.Customers.AnyAsync();
    }

    public async Task LoadAsync(IProgress<string>? progress = null)
    {
        if (await HasBusinessDataAsync()) throw new DomainException("Err.DemoRequiresEmpty");
        var clock = _ctx.Clock as MutableClock;
        var today = DateTime.Today;
        var start = today.AddDays(-95);
        // opening balances on the first day of the first demo month, before any demo transaction
        var opening = new DateTime(start.Year, start.Month, 1);
        void At(DateTime d, int hour = 10) => clock?.Set(d.Date.AddHours(hour));
        var rnd = new Random(20260925);
        try
        {
            At(opening, 9);
            var settings = await _ctx.Settings.GetAsync();
            var wh = settings.DefaultWarehouseId!.Value;
            var units = await ReadUnitsAsync();
            long U(string code) => units.First(u => u.Code == code).Id;

            // ---------------------------------------------------------------- master data
            progress?.Report("Demo: master data");
            var customers = new List<long>();
            var customerSeeds = new (string Name, string Phone, string Email, decimal Limit, int Terms)[]
            {
                ("مؤسسة النور للدعاية والإعلان", "0551234501", "info@alnoor-ads.sa", 50000, 30),
                ("Gift Corner Trading", "0551234502", "orders@giftcorner.sa", 20000, 15),
                ("مطعم بيت الزيتون", "0551234503", "manager@zaitoon.sa", 10000, 0),
                ("Al Waha Events", "0551234504", "events@alwaha.sa", 30000, 30),
                ("شركة الإبداع للديكور", "0551234505", "design@ibdaa-decor.sa", 40000, 30),
                ("Leather Craft Studio", "0551234506", "hello@leathercraft.sa", 15000, 15),
                ("مدارس المستقبل الأهلية", "0551234507", "admin@future-schools.sa", 25000, 45),
                ("Horizon Hotel", "0551234508", "purchasing@horizonhotel.sa", 60000, 30),
            };
            foreach (var c in customerSeeds)
                customers.Add(await S<CustomerService>().SaveAsync(new Customer { Name = c.Name, Phone = c.Phone, Email = c.Email, CreditLimit = c.Limit, PaymentTermsDays = c.Terms, Address = "Riyadh", IsActive = true }));

            var suppliers = new List<long>
            {
                await S<SupplierService>().SaveAsync(new Supplier { Name = "Gulf Wood Panels Co.", Phone = "0114567801", PaymentTermsDays = 30, IsActive = true }),
                await S<SupplierService>().SaveAsync(new Supplier { Name = "مصنع الأكريليك الحديث", Phone = "0114567802", PaymentTermsDays = 30, IsActive = true }),
                await S<SupplierService>().SaveAsync(new Supplier { Name = "Premium Leather Supply", Phone = "0114567803", PaymentTermsDays = 15, IsActive = true }),
                await S<SupplierService>().SaveAsync(new Supplier { Name = "Bright LED Trading", Phone = "0114567804", PaymentTermsDays = 30, IsActive = true }),
                await S<SupplierService>().SaveAsync(new Supplier { Name = "Pack & Go Packaging", Phone = "0114567805", PaymentTermsDays = 30, IsActive = true }),
                await S<SupplierService>().SaveAsync(new Supplier { Name = "Riyadh UV Print House", Phone = "0114567806", PaymentTermsDays = 15, IsActive = true }),
            };

            var designer = await S<EmployeeService>().SaveAsync(new Employee { Name = "سارة المصممة", Role = "Designer", HourlyCost = 45, IsActive = true });
            var op1 = await S<EmployeeService>().SaveAsync(new Employee { Name = "Khalid Operator", Role = "Laser Operator", HourlyCost = 30, IsActive = true });
            var op2 = await S<EmployeeService>().SaveAsync(new Employee { Name = "محمد الفني", Role = "Laser Operator", HourlyCost = 28, IsActive = true });
            var finisher = await S<EmployeeService>().SaveAsync(new Employee { Name = "Omar Finishing", Role = "Finishing & Assembly", HourlyCost = 22, IsActive = true });
            var inspector = await S<EmployeeService>().SaveAsync(new Employee { Name = "ليلى الجودة", Role = "Quality Control", HourlyCost = 26, IsActive = true });

            var machineSvc = S<MachineService>();
            var existingMachines = await machineSvc.LookupAsync();
            var m1 = existingMachines.Count > 0 ? existingMachines[0].Id : await machineSvc.SaveAsync(new Machine
            {
                Name = "CO2 Laser 1390 — 130W", MachineType = "CO2 Flatbed 1300x900", LaserPowerWatts = 130, ElectricalLoadKw = 3.2m, PurchaseCost = 85000, UsefulLifeYears = 6,
                ResidualValue = 10000, AnnualWorkingHours = 2200, ElectricityPricePerKwh = settings.DefaultElectricityPricePerKwh, MaintenanceCostPerYear = 9000, OtherOverheadPerYear = 4000, IsActive = true
            });
            var m2 = existingMachines.Count > 1 ? existingMachines[1].Id : await machineSvc.SaveAsync(new Machine
            {
                Name = "CO2 Laser 6040 — 80W", MachineType = "CO2 Desktop 600x400", LaserPowerWatts = 80, ElectricalLoadKw = 1.6m, PurchaseCost = 32000, UsefulLifeYears = 5,
                ResidualValue = 3000, AnnualWorkingHours = 1800, ElectricityPricePerKwh = settings.DefaultElectricityPricePerKwh, MaintenanceCostPerYear = 3500, OtherOverheadPerYear = 1500, IsActive = true
            });

            var cats = await S<LookupService>().CategoriesAsync();
            long Cat(string n) => cats.First(c => c.Name == n).Id;
            var matSvc = S<MaterialService>();
            async Task<long> Mat(string name, string type, decimal t, decimal l, decimal w, string unit, decimal cost, string cat, long supplier, decimal reorder, MaterialKind kind = MaterialKind.RawMaterial, decimal price = 0)
                => await matSvc.SaveAsync(new Material { Name = name, MaterialType = type, Thickness = t, Length = l, Width = w, UnitId = U(unit), PurchaseCost = cost, CategoryId = Cat(cat), SupplierId = supplier, ReorderLevel = reorder, MinimumStock = reorder / 2, Kind = kind, SalesPrice = price, IsActive = true });
            var mats = new List<long>
            {
                await Mat("MDF 3mm 122×244", "MDF", 3, 244, 122, "SHEET", 28, "Wood", suppliers[0], 20),
                await Mat("MDF 6mm 122×244", "MDF", 6, 244, 122, "SHEET", 46, "Wood", suppliers[0], 10),
                await Mat("Plywood Birch 4mm 122×244", "Plywood", 4, 244, 122, "SHEET", 62, "Wood", suppliers[0], 10),
                await Mat("أكريليك شفاف 3mm 122×244", "Acrylic", 3, 244, 122, "SHEET", 145, "Acrylic & Plastic", suppliers[1], 8),
                await Mat("أكريليك أسود 5mm 122×244", "Acrylic", 5, 244, 122, "SHEET", 215, "Acrylic & Plastic", suppliers[1], 5),
                await Mat("Natural Leather 1.5mm 100×100", "Leather", 1.5m, 100, 100, "SHEET", 95, "Leather & Fabric", suppliers[2], 6),
                await Mat("Kraft Cardboard 2mm 100×70", "Cardboard", 2, 100, 70, "SHEET", 6.5m, "Paper & Cardboard", suppliers[0], 50),
                await Mat("Glass Coaster Blank 10cm", "Glass", 4, 10, 10, "PCS", 4.2m, "Glass", suppliers[1], 100),
            };
            var tape = await Mat("Masking Tape 50mm", "Tape", 0, 0, 0, "ROLL", 24, "Consumables", suppliers[0], 10, MaterialKind.Consumable);
            var glue = await Mat("Wood Glue", "Glue", 0, 0, 0, "L", 18, "Consumables", suppliers[0], 5, MaterialKind.Consumable);
            var coasterSet = await Mat("Wooden Coaster Set (4 pcs)", "Finished", 0, 0, 0, "PCS", 12, "Finished Goods", suppliers[0], 10, MaterialKind.FinishedGood, 35);
            // purchased components, packaging and an outsourced service — jobs use many items, not one material
            var led = await Mat("LED Strip 12V warm white", "LED", 0, 0, 0, "M", 9.5m, "Electrical & Lighting", suppliers[3], 20, MaterialKind.PurchasedComponent);
            var adapter = await Mat("Power Adapter 12V 2A", "Adapter", 0, 0, 0, "PCS", 38, "Electrical & Lighting", suppliers[3], 5, MaterialKind.PurchasedComponent);
            var toggle = await Mat("Rocker Switch", "Switch", 0, 0, 0, "PCS", 6, "Electrical & Lighting", suppliers[3], 10, MaterialKind.PurchasedComponent);
            var wire = await Mat("Electrical Wire 2×0.75mm", "Wire", 0, 0, 0, "M", 2.2m, "Electrical & Lighting", suppliers[3], 50, MaterialKind.PurchasedComponent);
            var screws = await Mat("Stainless Screws M4×20", "Screws", 0, 0, 0, "PCS", 0.35m, "Hardware & Fittings", suppliers[0], 200, MaterialKind.PurchasedComponent);
            var hook = await Mat("Brass Wall Hook", "Hook", 0, 0, 0, "PCS", 4.5m, "Hardware & Fittings", suppliers[0], 20, MaterialKind.PurchasedComponent);
            var giftBox = await Mat("Gift Box 30×20×8 cm", "Box", 0, 0, 0, "PCS", 7.5m, "Packaging", suppliers[4], 20, MaterialKind.Packaging);
            var bubble = await Mat("Bubble Wrap 100 cm", "Wrap", 0, 0, 0, "M", 1.8m, "Packaging", suppliers[4], 30, MaterialKind.Packaging);
            var uvPrint = await Mat("UV Colour Printing (outsourced)", "Service", 0, 0, 0, "PCS", 85, "Finished Goods", suppliers[5], 0, MaterialKind.Service);

            var lookup = S<LookupService>();
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[0], MachineId = m1, Thickness = 3, OperationType = OperationType.Cutting, PowerPercent = 55, SpeedMmPerSec = 25, PassCount = 1, Notes = "Air assist on" });
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[1], MachineId = m1, Thickness = 6, OperationType = OperationType.Cutting, PowerPercent = 75, SpeedMmPerSec = 12, PassCount = 1 });
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[3], MachineId = m1, Thickness = 3, OperationType = OperationType.Cutting, PowerPercent = 60, SpeedMmPerSec = 15, PassCount = 1, Notes = "Keep protective film" });
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[3], MachineId = m1, Thickness = 3, OperationType = OperationType.Engraving, PowerPercent = 20, SpeedMmPerSec = 300, FrequencyHz = 20000, PassCount = 1 });
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[5], MachineId = m2, Thickness = 1.5m, OperationType = OperationType.Engraving, PowerPercent = 18, SpeedMmPerSec = 250, PassCount = 1 });
            await lookup.SaveLaserParameterAsync(new LaserParameter { MaterialId = mats[7], MachineId = m2, Thickness = 4, OperationType = OperationType.Engraving, PowerPercent = 30, SpeedMmPerSec = 200, PassCount = 2, Notes = "Wet paper on glass" });

            // ---------------------------------------------------------------- opening balances & purchases
            progress?.Report("Demo: purchases");
            var acc = S<AccountingService>();
            var accounts = await acc.PostableAccountsAsync();
            var cashBal = await acc.SystemBalanceAsync(SystemAccounts.Cash);
            var bankBal = await acc.SystemBalanceAsync(SystemAccounts.Bank);
            if (cashBal == 0) await acc.PostAccountOpeningBalanceAsync(accounts.First(a => a.SystemKey == SystemAccounts.Cash).Id, 15000, opening);
            if (bankBal == 0) await acc.PostAccountOpeningBalanceAsync(accounts.First(a => a.SystemKey == SystemAccounts.Bank).Id, 180000, opening);
            await acc.PostAccountOpeningBalanceAsync(accounts.First(a => a.SystemKey == SystemAccounts.FixedAssets).Id, 117000, opening);
            await S<InventoryService>().OpeningBalanceAsync(coasterSet, wh, 40, 12, opening);
            await S<InventoryService>().OpeningBalanceAsync(tape, wh, 12, 24, opening);
            await S<InventoryService>().OpeningBalanceAsync(glue, wh, 6, 18, opening);
            foreach (var (item, qty, cost) in new[] { (led, 120m, 9.5m), (adapter, 30m, 38m), (toggle, 60m, 6m), (wire, 250m, 2.2m), (screws, 1500m, 0.35m), (hook, 120m, 4.5m),
                         (giftBox, 150m, 7.5m), (bubble, 200m, 1.8m) })
                await S<InventoryService>().OpeningBalanceAsync(item, wh, qty, cost, opening);

            var pur = S<PurchaseService>();
            async Task Buy(long supplier, DateTime date, params (long Mat, decimal Qty, decimal Cost)[] lines)
            {
                At(date, 9);
                var po = new PurchaseOrder { SupplierId = supplier, Date = date, ExpectedDate = date.AddDays(2) };
                foreach (var l in lines) po.Lines.Add(new PurchaseOrderLine { MaterialId = l.Mat, Quantity = l.Qty, UnitCost = l.Cost, TaxRate = settings.DefaultTaxRate });
                var poId = await pur.SaveOrderAsync(po);
                await pur.SetOrderStatusAsync(poId, PurchaseOrderStatus.Approved);
                At(date.AddDays(2), 11);
                var rec = await pur.ReceiptFromOrderAsync(poId);
                rec.WarehouseId = wh;
                rec.Date = date.AddDays(2);
                var recId = await pur.PostReceiptAsync(rec);
                At(date.AddDays(3), 12);
                var invId = await pur.PostSupplierInvoiceFromReceiptAsync(recId, $"SI-{rnd.Next(10000, 99999)}", date.AddDays(3));
                At(date.AddDays(20), 12);
                var inv = (await pur.ListSupplierInvoicesAsync(new PageRequest(PageSize: 500))).Items.First(i => i.Id == invId);
                await pur.PaySupplierAsync(supplier, invId, Math.Round(inv.Total * (rnd.Next(2) == 0 ? 1m : 0.6m), 2), PaymentMethod.Bank, date.AddDays(20), "TRF");
            }
            await Buy(suppliers[0], start, (mats[0], 60, 28), (mats[1], 30, 46), (mats[2], 25, 62), (mats[6], 200, 6.5m));
            await Buy(suppliers[1], start.AddDays(1), (mats[3], 20, 145), (mats[4], 10, 215), (mats[7], 400, 4.2m));
            await Buy(suppliers[2], start.AddDays(2), (mats[5], 15, 95));
            await Buy(suppliers[0], start.AddDays(55), (mats[0], 40, 29.5m), (mats[2], 15, 64));
            await Buy(suppliers[1], start.AddDays(60), (mats[3], 15, 150));

            // ---------------------------------------------------------------- monthly expenses
            progress?.Report("Demo: expenses");
            var expCats = await lookup.ExpenseCategoriesAsync();
            long EC(string n) => expCats.First(c => c.Name == n).Id;
            var ccs = await lookup.CostCentersAsync();
            var expSvc = S<ExpenseService>();
            async Task Expense(DateTime d, string cat, string desc, decimal amount, decimal taxRate, PaymentMethod pm, long? machine = null, long? job = null)
            {
                At(d, 15);
                var id = await expSvc.SaveAsync(new Expense
                {
                    Date = d, CategoryId = EC(cat), Description = desc, Amount = amount, TaxAmount = Math.Round(amount * taxRate / 100m, 2), PaymentMethod = pm, MachineId = machine, JobId = job,
                    CostCenterId = ccs.FirstOrDefault(c => c.Code == (cat == "Marketing" ? "SALES" : cat is "Rent" or "Salaries" ? "ADMIN" : "PROD"))?.Id
                });
                await expSvc.PostAsync(id);
            }
            for (var month = new DateTime(start.Year, start.Month, 1); month <= today; month = month.AddMonths(1))
            {
                var d = month < start ? start : month;
                await Expense(d.AddDays(1), "Rent", $"Workshop rent {month:MMM yyyy}", 6500, 0, PaymentMethod.Bank);
                if (month.AddDays(27) <= today)
                {
                    await Expense(month.AddDays(27), "Salaries", $"Salaries {month:MMM yyyy}", 21000, 0, PaymentMethod.Bank);
                    await Expense(month.AddDays(20), "Electricity", $"Electricity bill {month:MMM yyyy}", 1350 + rnd.Next(0, 400), settings.DefaultTaxRate, PaymentMethod.Bank);
                    await Expense(month.AddDays(12), "Marketing", "Social media ads", 900, settings.DefaultTaxRate, PaymentMethod.Cash);
                }
            }
            await Expense(start.AddDays(40), "Maintenance", "Laser tube alignment & mirror cleaning", 650, settings.DefaultTaxRate, PaymentMethod.Cash, m1);

            // ---------------------------------------------------------------- jobs through the full workflow
            progress?.Report("Demo: jobs");
            var templates = new List<JobTemplate>
            {
                new("لوحة إعلانية أكريليك مع شعار محفور 60×40 سم", 0, 3, 60, 40, 1, 4, 22, 20, OperationType.Assembly, 2, 6, "Closed", 2, true),
                new("Wedding invitations laser cut kraft 15×20 cm", 3, 6, 20, 15, 1, 150, 1.6m, 1, OperationType.Packaging, 1.5m, 0.4m, "Closed", 6),
                new("MDF name plates for offices 25×8 cm", 6, 0, 25, 8, 1, 60, 2.5m, 1.5m, OperationType.Finishing, 1, 0.5m, "Closed", 10),
                new("Engraved leather keychains with logo", 5, 5, 8, 4, 1, 200, 0.9m, 1.2m, OperationType.Finishing, 1, 0.3m, "Closed", 15, true),
                new("علب هدايا خشبية مع غطاء محفور", 1, 2, 30, 20, 6, 30, 9, 12, OperationType.Assembly, 2, 2, "Invoiced", 24),
                new("Acrylic menu stands for restaurant A5", 2, 3, 22, 16, 2, 25, 4, 4, OperationType.Assembly, 1, 1, "Invoiced", 32),
                new("Plywood wall art — city skyline 80×60 cm", 4, 2, 80, 60, 1, 6, 45, 25, OperationType.Finishing, 3, 8, "Closed", 38),
                new("Corporate glass coasters engraving", 7, 7, 10, 10, 1, 120, 2.2m, 0.8m, OperationType.Packaging, 1, 0.6m, "Invoiced", 45, true),
                new("حروف بارزة أكريليك أسود لواجهة محل", 0, 4, 40, 35, 3, 1, 95, 60, OperationType.Assembly, 2.5m, 15, "Delivered", 60),
                new("Room number signs acrylic 15×15", 7, 3, 15, 15, 1, 80, 3, 2, OperationType.Assembly, 1, 0.5m, "Closed", 66, true),
                new("School trophies MDF 6mm with engraving", 6, 1, 20, 25, 2, 40, 7, 6, OperationType.Assembly, 1.5m, 1.5m, "Completed", 78, true),
                new("Event table numbers acrylic", 3, 3, 12, 18, 1, 30, 3.5m, 2, OperationType.Assembly, 1, 0.5m, "InProduction", 88, true),
                new("Decorative MDF panels for villa 60×120", 4, 1, 120, 60, 1, 10, 60, 20, OperationType.Finishing, 4, 5, "Planned", 95),
                new("Leather notebook covers engraved", 5, 5, 30, 22, 1, 50, 6, 8, OperationType.Finishing, 2, 1, "Quoted", 100, true),
                new("Custom gift boxes for Ramadan", 1, 0, 25, 25, 5, 100, 5, 6, OperationType.Assembly, 2, 1.2m, "QuoteDraft", 104),
                new("Acrylic awards with UV print", 7, 3, 18, 24, 1, 15, 8, 5, OperationType.Assembly, 1, 2, "Rejected", 70),
                new("Laser cut stencil set", 2, 6, 50, 35, 1, 10, 6, 2, OperationType.Packaging, 1, 0.5m, "Request", 107),
            };

            // extra components per job template (index → item, quantity per finished unit)
            var extras = new Dictionary<int, (long Item, decimal PerUnit)[]>
            {
                [0] = new[] { (mats[1], 1m), (led, 2.2m), (adapter, 1m), (toggle, 1m), (wire, 1.5m), (screws, 8m), (bubble, 2m) },   // illuminated acrylic sign
                [4] = new[] { (glue, 0.02m), (giftBox, 1m) },                                                                          // wooden gift boxes
                [5] = new[] { (screws, 2m), (bubble, 0.3m) },                                                                          // acrylic menu stands
                [6] = new[] { (hook, 2m), (bubble, 1.5m), (tape, 0.1m) },                                                             // plywood wall art
                [7] = new[] { (giftBox, 0.25m) },                                                                                      // glass coasters, boxed in fours
                [8] = new[] { (led, 3m), (adapter, 1m), (wire, 6m), (screws, 12m), (uvPrint, 1m) },                                    // raised letters with UV print
                [9] = new[] { (screws, 4m) },                                                                                          // room number signs
                [10] = new[] { (screws, 2m), (giftBox, 1m) },                                                                          // school trophies
            };

            var reqSvc = S<RequestService>(); var designSvc = S<DesignService>(); var estSvc = S<EstimateService>(); var quoteSvc = S<QuotationService>();
            var jobSvc = S<JobService>(); var prodSvc = S<ProductionService>(); var invSvc = S<InventoryService>(); var salesSvc = S<SalesService>();
            var ops = new[] { op1, op2 };

            foreach (var t in templates)
            {
                var d0 = start.AddDays(t.DayOffset);
                if (d0 > today) d0 = today;
                DateTime D(int add) { var d = d0.AddDays(add); return d > today ? today : d; }
                var customerId = customers[t.Customer];
                At(D(0), 9);
                var matInfo = await matSvc.GetAsync(mats[t.Material]);
                var request = new CustomerRequest
                {
                    CustomerId = customerId, RequestDate = D(0), Description = t.Description, Dimensions = $"{t.PieceL:0.#} × {t.PieceW:0.#} cm",
                    Quantity = t.Qty, RequiredDate = D(14), Items = { new RequestItem { MaterialId = mats[t.Material], Quantity = 1 } }
                };
                var idx = templates.IndexOf(t);
                if (extras.TryGetValue(idx, out var more))
                    foreach (var (item, perUnit) in more) request.Items.Add(new RequestItem { MaterialId = item, Quantity = perUnit });
                var reqId = await reqSvc.SaveAsync(request);
                if (t.Stage == "Request") continue;

                At(D(1), 11);
                var rev1 = await designSvc.SaveAsync(new DesignRevision { RequestId = reqId, Date = D(1), DesignerId = designer, Width = t.PieceL, Height = t.PieceW, MaterialId = mats[t.Material], Thickness = matInfo!.Thickness,
                    CuttingLengthM = Math.Round((t.PieceL + t.PieceW) * 2 / 100m * t.PiecesPerUnit, 2), EngravingAreaCm2 = t.Engrave ? Math.Round(t.PieceL * t.PieceW * 0.3m, 0) : 0, EstimatedMachineMinutes = t.MinutesPerUnit, Notes = "First concept" });
                var approvedRev = rev1;
                if (rnd.Next(3) == 0)
                {
                    await designSvc.RejectAsync(rev1);
                    approvedRev = await designSvc.SaveAsync(new DesignRevision { RequestId = reqId, Date = D(1), DesignerId = designer, Width = t.PieceL, Height = t.PieceW, MaterialId = mats[t.Material], Thickness = matInfo.Thickness,
                        CuttingLengthM = Math.Round((t.PieceL + t.PieceW) * 2 / 100m * t.PiecesPerUnit, 2), EngravingAreaCm2 = t.Engrave ? Math.Round(t.PieceL * t.PieceW * 0.3m, 0) : 0, EstimatedMachineMinutes = t.MinutesPerUnit, Notes = "Customer changes applied" });
                }
                await designSvc.ApproveAsync(approvedRev);

                At(D(2), 10);
                var est = await estSvc.NewDraftAsync(customerId, reqId);
                est.Date = D(2);
                var ml = est.MaterialLines.First();
                if (ml.SheetBased) { ml.Pieces.Clear(); ml.Pieces.Add(new EstimatePiece { Name = "Part", Length = t.PieceL, Width = t.PieceW, QuantityPerUnit = t.PiecesPerUnit }); }
                else ml.QuantityPerUnit = t.PiecesPerUnit;
                var machineId = t.Material is 5 or 7 ? m2 : m1;
                foreach (var svc in est.MaterialLines.Where(l => l.Source == ComponentSource.ExternalService)) svc.UnitCost = 350; // supplier's quote
                est.MachineLines.Clear();
                est.MachineLines.Add(new EstimateMachineLine { MachineId = machineId, Operation = t.Engrave && t.Material is 5 or 7 ? OperationType.Engraving : OperationType.Cutting, MinutesPerUnit = t.MinutesPerUnit });
                est.LaborLines.Add(new EstimateLaborLine { EmployeeId = finisher, Operation = t.LaborOp, MinutesPerUnit = t.LaborMinutes, HourlyRate = 22 });
                est.DesignHours = t.DesignHours; est.DesignRate = 45; est.SetupHours = 0.5m; est.SetupRate = 30;
                est.PackagingPerUnit = t.PackagingPerUnit;
                est.SellingPrice = 0;
                var estId = await estSvc.SaveAsync(est);
                // the first multi-component product is kept as a reusable product template
                if (idx == extras.Keys.Min())
                    await S<ProductTemplateService>().SaveFromEstimateAsync(estId, t.Description);

                At(D(2), 14);
                var qId = await quoteSvc.CreateFromEstimateAsync(estId);
                if (t.Stage == "QuoteDraft") continue;
                await quoteSvc.MarkSentAsync(qId);
                if (t.Stage == "Quoted") continue;
                At(D(3), 10);
                if (t.Stage == "Rejected") { await quoteSvc.RejectAsync(qId); continue; }
                if (rnd.Next(4) == 0)
                {
                    // customer negotiated: new version with a small discount
                    qId = await quoteSvc.NewVersionAsync(qId);
                    var qv = await quoteSvc.GetAsync(qId);
                    qv!.DiscountAmount = Math.Round(qv.SellingPrice * 0.05m, 2);
                    await quoteSvc.SaveAsync(qv);
                    await quoteSvc.MarkSentAsync(qId);
                }
                await quoteSvc.ApproveAsync(qId);
                var jobId = await quoteSvc.CreateJobAsync(qId, new JobCreationOptions(D(12), rnd.Next(4) == 0 ? JobPriority.High : JobPriority.Normal, machineId, ops[rnd.Next(2)]));
                if (t.Stage == "Planned") { await jobSvc.SetStatusAsync(jobId, JobStatus.Planned); continue; }

                // issue every stocked component line (sometimes one extra sheet → material variance); charge direct and outsourced lines
                At(D(4), 8);
                var estSaved = await estSvc.GetAsync(estId);
                var line = estSaved!.MaterialLines.First();
                foreach (var comp in await S<JobComponentService>().ListAsync(jobId))
                {
                    if (comp.Source == ComponentSource.Inventory)
                    {
                        var isSheet = comp.Unit == "SHEET";
                        var issueQty = isSheet ? Math.Max(1, comp.PlannedQuantity) : comp.PlannedQuantity;
                        if (isSheet && comp.LineNo == 1 && rnd.Next(3) == 0) issueQty += 1;
                        if (issueQty > 0) await S<JobComponentService>().IssueAsync(comp.Id, wh, issueQty, D(4), "Demo issue");
                    }
                    else if (comp.Source is ComponentSource.ExternalService or ComponentSource.DirectPurchase)
                        await S<JobComponentService>().RecordDirectCostAsync(new DirectCostInput(comp.Id, D(4), comp.PlannedQuantity, Math.Round(comp.EstimatedCost * 1.08m, 2),
                            Math.Round(comp.EstimatedCost * 1.08m * settings.DefaultTaxRate / 100m, 2), PaymentMethod.OnCredit, suppliers[5], "UVP-" + rnd.Next(1000, 9999), null));
                }
                if (rnd.Next(3) == 0) await invSvc.IssueToJobAsync(jobId, tape, wh, 1, D(4), "Masking");

                // operations
                var jobOps = (await prodSvc.ListOperationsAsync(new PageRequest(PageSize: 100), jobId)).Items;
                var opDate = D(4);
                foreach (var o in jobOps)
                {
                    At(opDate, 13);
                    var factor = 0.85m + (decimal)rnd.NextDouble() * 0.45m;
                    var hours = Math.Max(0.1m, Math.Round(o.PlannedHours * factor, 2));
                    var isMachine = o.OperationType is OperationType.Cutting or OperationType.Engraving;
                    await prodSvc.CompleteOperationAsync(o.Id, new OperationCompletion(isMachine ? Math.Round(hours * 1.1m, 2) : hours, isMachine ? hours : 0, t.Qty,
                        StartTime: opDate.AddHours(8), EndTime: opDate.AddHours(8).AddHours((double)hours), EmployeeId: o.OperationType == OperationType.Design ? designer : isMachine ? ops[rnd.Next(2)] : finisher));
                    if (t.Stage == "InProduction") break;
                }
                if (t.Stage == "InProduction") continue;

                // scrap / rework / remnant
                if (rnd.Next(2) == 0 && line.SheetBased)
                    await prodSvc.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = D(5), Type = rnd.Next(3) == 0 ? ScrapType.AbnormalScrap : ScrapType.NormalScrap, MaterialId = line.MaterialId, Quantity = 0.25m, Reason = rnd.Next(2) == 0 ? "Burn marks on edges" : "Focus drift — parts out of tolerance" });
                if (rnd.Next(3) == 0)
                    await prodSvc.RecordScrapAsync(new ScrapRecord { JobId = jobId, Date = D(5), Type = ScrapType.Rework, MachineId = machineId, EmployeeId = ops[0], Quantity = Math.Max(1, Math.Round(t.Qty * 0.05m)), Hours = 0.75m, Reason = "Re-engrave faint logos" });
                if (line.SheetBased && line.UtilizationPercent < 60)
                {
                    try
                    {
                        await invSvc.CreateRemnantAsync(new RemnantInput(line.MaterialId!.Value, wh, Math.Round(line.SheetLength * 0.4m, 0), Math.Round(line.SheetWidth * 0.5m, 0), null, jobId, null, D(5), "Offcut kept for reuse"));
                    }
                    catch (DomainException) { /* remnant larger than remaining job material cost — skip */ }
                }

                At(D(6), 16);
                await prodSvc.CompleteProductionAsync(jobId);
                var rejected = rnd.Next(4) == 0 ? Math.Max(1, Math.Round(t.Qty * 0.02m)) : 0;
                await prodSvc.RecordQualityCheckAsync(new QualityCheck { JobId = jobId, Date = D(6), InspectorId = inspector, QuantityProduced = t.Qty + rejected, QuantityAccepted = t.Qty, QuantityRejected = rejected, Status = QualityStatus.Passed, Notes = "Checked dimensions and engraving depth" });
                if (t.Stage == "Completed") continue;
                At(D(7), 10);
                await jobSvc.DeliverAsync(jobId, D(7), "Delivered to customer site");
                if (t.Stage == "Delivered") continue;

                At(D(7), 12);
                var invId = await salesSvc.CreateInvoiceFromJobAsync(jobId);
                await salesSvc.PostInvoiceAsync(invId, ignoreCreditLimit: true);
                var invoice = await salesSvc.GetInvoiceAsync(invId);
                At(D(15 + rnd.Next(10)), 11);
                var payAll = t.Stage == "Closed";
                var amount = payAll ? invoice!.Total : Math.Round(invoice!.Total * 0.5m, 2);
                if (_ctx.Clock.Now.Date <= today)
                    await salesSvc.RecordPaymentAsync(customerId, invId, amount, rnd.Next(3) == 0 ? PaymentMethod.Cash : PaymentMethod.Bank, _ctx.Clock.Now.Date, $"REF-{rnd.Next(1000, 9999)}");
                if (t.Stage == "Closed") await jobSvc.CloseAsync(jobId);
            }

            // direct stock sale and a customer return (return restocked at original cost)
            progress?.Report("Demo: stock sales");
            At(start.AddDays(50), 12);
            var fg = new SalesInvoice { CustomerId = customers[1], Date = start.AddDays(50), DueDate = start.AddDays(65) };
            fg.Lines.Add(new SalesInvoiceLine { LineType = InvoiceLineType.StockItem, MaterialId = coasterSet, WarehouseId = wh, Description = "Wooden Coaster Set (4 pcs)", Quantity = 12, UnitPrice = 35, TaxRate = settings.DefaultTaxRate });
            var fgId = await salesSvc.SaveInvoiceAsync(fg);
            await salesSvc.PostInvoiceAsync(fgId, ignoreCreditLimit: true);
            At(start.AddDays(53), 12);
            var fgInv = await salesSvc.GetInvoiceAsync(fgId);
            await salesSvc.CreateReturnAsync(fgId, new[] { new ReturnLineInput(fgInv!.Lines[0].Id, 2, true, wh) }, start.AddDays(53), "Two sets returned — wrong colour", PaymentMethod.OnCredit);
            At(start.AddDays(60), 12);
            var fgBal = (await salesSvc.GetInvoiceAsync(fgId))!.Balance;
            if (fgBal > 0) await salesSvc.RecordPaymentAsync(customers[1], fgId, fgBal, PaymentMethod.Cash, start.AddDays(60), "Cash");

            // stock count adjustment and a remnant from the warehouse
            At(start.AddDays(70), 17);
            await invSvc.AdjustAsync(mats[6], wh, -3, null, start.AddDays(70), "Stock count difference — damaged by humidity");
            await invSvc.CreateRemnantAsync(new RemnantInput(mats[3], wh, 60, 40, null, null, 18, start.AddDays(70), "Leftover acrylic found in storage"));

            // a few more requests to show the pipeline
            At(today, 9);
            await reqSvc.SaveAsync(new CustomerRequest { CustomerId = customers[2], RequestDate = today, Description = "Engraved wooden serving boards with restaurant logo", Quantity = 20, Items = { new RequestItem { MaterialId = mats[2], Quantity = 1 } }, RequiredDate = today.AddDays(10), Dimensions = "35 × 20 cm" });
            await reqSvc.SaveAsync(new CustomerRequest { CustomerId = customers[4], RequestDate = today, Description = "لوحة جدارية أكريليك مضيئة", Quantity = 1, Items = { new RequestItem { MaterialId = mats[3], Quantity = 1 } }, RequiredDate = today.AddDays(21), Dimensions = "120 × 60 cm" });
        }
        finally
        {
            clock?.Reset();
        }
    }

    private async Task<List<UnitOfMeasure>> ReadUnitsAsync()
    {
        await using var db = _ctx.Factory.Create();
        return await db.Units.AsNoTracking().ToListAsync();
    }
}
