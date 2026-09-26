# Testing

All automated tests live in `tests/LaserWorks.Tests` (xUnit). Integration tests use a real SQLite
database in a private temporary folder, created through the production service registrations and the
first-run setup; nothing is mocked. UI tests start the real Avalonia application headlessly with the Skia
renderer, so the actual XAML views, bindings, styles and fonts are exercised and screenshots are rendered.

```bash
dotnet test tests/LaserWorks.Tests                                   # everything (~3 minutes)
dotnet test tests/LaserWorks.Tests --filter "FullyQualifiedName~Unit"
dotnet test tests/LaserWorks.Tests --filter "Category=Performance"
LASERWORKS_SCREENSHOTS=./shots dotnet test tests/LaserWorks.Tests --filter "FullyQualifiedName~Ui"
```

Test classes run one at a time (the language setting and the desktop app's service provider are process-wide).

## Suites

| Suite | What it proves |
|---|---|
| `Unit/CostingCalculatorTests` | machine hourly cost and override; sheet utilisation (area, grid, efficiency, consumed fraction, invalid layouts); remnant value; moving weighted average; margin vs markup; all estimate components, manual override, overhead methods; variance and main driver; password hashing and policy; rounding |
| `Integration/CriticalWorkflowTests` | spec §43 — all 32 steps through the services, with exact checks on the estimate, WIP, COGS, revenue, VAT, receivable, bank, stock and the reconciliation |
| `Ui/UiCriticalWorkflowTests` | the same 32 steps performed through the desktop view models and dialogs (buttons' commands), with screenshots |
| `Integration/SalesReturnTests` | spec §44 — sale → COGS → return → restock at the **original** cost although the average moved → COGS reversal; inventory, GL, COGS, revenue, VAT and receivable reconcile |
| `Integration/AccountingIntegrityTests` | unbalanced entries rejected; posted lines cannot be edited or deleted; reversal; closed periods; stock ledger and audit log immutable; demo company trial balance balances |
| `Integration/InventoryCostingTests` | receipts at a new price move the average; issues and returns to jobs at average; over-issue and over-return rejected; remnant value leaves one job and enters another |
| `Integration/SecurityTests` | hashed passwords; lockout after five failures and unlock after 15 minutes; permissions enforced inside services; audit trail with user and changed fields |
| `Integration/BackupRestoreTests` | backup → change → restore returns the earlier data, safety backup created, integrity OK; demo backup restores into a fresh installation; corrupted and foreign files rejected without touching data |
| `Integration/ReportTests` | all 40 reports run on the demo company and export to PDF, Excel and CSV in Arabic and English; quotation and invoice PDFs |
| `Integration/LocalizationTests` | Arabic and English have identical keys and placeholders, every Arabic text is translated, every enum value and every key used in code/XAML exists |
| `Integration/PerformanceTests` | 3,000 customers, 10,000 jobs, 10,000 invoices, 20,000 stock movements, 50,000 journal lines; times real screens and reports |
| `Ui/UiSmokeTests` | every module and every tab rendered at 1366×768 in Arabic/Light (RTL) and English/Dark (LTR); fails on any binding error, error banner or missing translation |
| `Ui/UiDialogTests` | every *New*, *Edit* and *Open* command on every module opened (62 dialogs and detail pages, Arabic and English) without binding errors |

## Latest results (Linux, .NET 10.0.112, Release)

**51 passed, 0 failed, 0 skipped** — total 2.6 minutes (4 vCPU, 15 GB RAM).

Performance on the large database (second run of each query, after warm-up):

| Screen / report | Time |
|---|---|
| Customers page with balances | 5 ms |
| Customer search | 6 ms |
| Jobs page sorted by customer | 6 ms |
| Jobs last page (of 200) | 2 ms |
| Open invoices page | 119 ms |
| Stock movements page | 2 ms |
| Journal entries page | 2 ms |
| Trial balance (1 year) | 138 ms |
| Dashboard | 439 ms |
| AR aging report | 173 ms |
| Sales register (1 year) | 108 ms |

## Not covered by automated tests

- **Windows runtime validation is pending.** Everything above ran on Linux. The Windows build is produced
  by cross-compiling; `.github/workflows/windows-release.yml` runs the same tests on `windows-latest`, starts
  the packaged exe (`--smoke-test`), and installs, starts and uninstalls the installer silently. Its results
  were not available when this release candidate was prepared.
- Native file pickers and printing: tests replace the picker with fixed paths; the Windows "print" verb is untested.
- Visual review was done on headless screenshots at 1366×768; other resolutions and high-DPI scaling were not reviewed.
