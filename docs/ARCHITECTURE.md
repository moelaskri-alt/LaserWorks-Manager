# Architecture

## Projects

```
src/
  LaserWorks.Domain          entities, enums, pure calculators (no I/O)
  LaserWorks.Localization    Arabic/English string tables, number/date formatting
  LaserWorks.Application     use cases: services, posting engines, IAppDb abstraction
  LaserWorks.Infrastructure  EF Core + SQLite, integrity rules, migrations, backup, file paths
  LaserWorks.Reporting       40 reports, PDF (QuestPDF) / Excel (ClosedXML) / CSV exporters, documents
  LaserWorks.Desktop         Avalonia UI (MVVM), produces LaserWorksManager.exe
  LaserWorks.Tools           command line: demo database, bulk report export
tests/
  LaserWorks.Tests           xUnit: unit, SQLite integration, performance, headless Avalonia UI
installer/                   NSIS script
tools/                       release script, string table sources, view generator
```

Dependencies point inwards: Desktop → Reporting/Infrastructure → Application → Domain.
Business logic never references SQLite; it works against `IAppDb` (a set of EF Core `DbSet`s),
so another EF provider could replace SQLite without touching the services.

## Domain

`LaserWorks.Domain/Costing` holds the formulas as pure, unit-tested functions:

| Calculator | Purpose |
|---|---|
| `MachineCostCalculator` | hourly rate = depreciation + electricity + maintenance + operator + overhead, or a manual override |
| `MaterialUtilizationCalculator` | sheets needed (area and grid methods), utilisation, waste, material cost, remnant value |
| `EstimateCalculator` | the eleven estimate components, overhead, total and unit cost |
| `PricingCalculator` | price from margin or markup, profit analysis |
| `InventoryMath` | moving weighted average receipts and issues |
| `VarianceCalculator` | estimated vs actual per component, main variance driver |

## Application

Each module has a service (`CustomerService`, `RequestService`, `EstimateService`,
`QuotationService`, `JobService`, `ProductionService`, `InventoryService`, `SalesService`,
`PurchaseService`, `ExpenseService`, `AccountingService`, `JobCostingService`, …). A service method
is one unit of work: it checks the permission, validates, changes the data and posts accounting in
one transaction (`ServiceBase.TxAsync`).

Shared engines (`Services/Engines.cs`):

- `AccountingEngine` — builds and posts journal entries, rejects unbalanced entries and closed periods, creates fiscal years
- `InventoryEngine` — receipts, issues, transfers and remnant movements with moving average cost; writes the stock ledger
- `JobCostEngine` — appends actual cost entries to a job by cost component
- `Numbering` — document numbers from configurable sequences

## Persistence and integrity

SQLite in WAL mode, schema managed by EF Core migrations (`Persistence/Migrations`), applied
automatically at start-up. `AppDbContext.SaveChangesAsync` enforces rules no caller can bypass:

- stock transactions, job cost entries and audit log rows are **immutable** (insert only)
- a posted journal entry must balance; its lines can never change; only its status can move to *Reversed*
- audited entities write an audit row (who, when, which fields) on create, update and delete
- foreign keys restrict deletes (document lines cascade with their document)
- money is stored as decimal(18,6) and rounded half away from zero to the currency's decimals

## Desktop

Avalonia 11 with compiled bindings. `ShellViewModel` hosts the navigation and pages;
`DialogService` shows modal dialogs as overlays and wraps file pickers (overridable for tests);
`Navigator` opens documents from anywhere (drill-down from reports and charts).
List pages derive from `ListPageViewModel<T>`: server-side paging, search, sorting, column chooser,
export and print. Texts come from `{l:T Key}` bindings to the string tables, which update live when
the language changes; layout mirrors for Arabic.

## Data location

`%LOCALAPPDATA%\LaserWorksManager` (database `laserworks.db`, `attachments`, `backups`, `exports`,
`logs`). A `portable.flag` next to the exe switches to `.\data`; `LASERWORKS_DATA` overrides both.
