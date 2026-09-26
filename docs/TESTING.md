# Testing

All automated tests live in `tests/LaserWorks.Tests` (xUnit). Integration tests use a real SQLite
database in a private temporary folder, created through the production service registrations and the
first-run setup; nothing is mocked. UI tests start the real Avalonia application headlessly with the Skia
renderer, so the actual XAML views, bindings, styles and fonts are exercised and screenshots are rendered.

```bash
dotnet test tests/LaserWorks.Tests                                   # everything (~6 minutes)
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
| `Integration/MultiComponentTests` | restaurant sign (9–10 component lines of every type and source, V1/V2, remnant, direct purchase, service, scrap, rework, QC, delivery, invoice, payment); serving board (purchased accessory through order → receipt → supplier invoice → issue; ledger checked per account and every entry balanced); job with zero / one / added lines, unplanned lines, return, duplicate, locked identity; component rules (services never stocked, remnants raw only, credit needs supplier, reversal); 20 lines; product templates from estimate and job |
| `Integration/MigrationTests` | the real 1.0.0-rc1 demo database is upgraded: no row lost, no amount re-priced, requests → items, estimates and jobs → component lines, every job stock movement linked to a line, reconciliation and reports pass |
| `Integration/ReportTests` | all 41 reports run on the demo company and export to PDF, Excel and CSV in Arabic and English; quotation and invoice PDFs |
| `Integration/LocalizationTests` | Arabic and English have identical keys and placeholders, every Arabic text is translated, every enum value and every key used in code/XAML exists |
| `Integration/PerformanceTests` | 3,000 customers, 10,000 jobs, 10,000 invoices, 20,000 stock movements, 50,000 journal lines; times real screens and reports |
| `Ui/UiSmokeTests` | every module and every tab rendered at 1366×768 in Arabic/Light (RTL) and English/Dark (LTR); fails on any binding error, error banner, missing translation or layout-audit problem |
| `Ui/UiDialogTests` | every *New*, *Edit* and *Open* command on every module opened (dialogs and detail pages, Arabic and English) without binding errors or layout-audit problems |
| `Ui/UiLayoutMatrixTests` | the spec §33 critical screens and the request, estimate, job (every tab), component, direct-cost, template, issue, customer, quotation and invoice screens at 1366×768, 1600×900 and 1920×1080 × Arabic/English × light/dark (384 screenshots). The audit fails on: buttons outside the window or cut off, dialog Save/Cancel off screen, grids wider than the window or squeezed below 60 px, headers that can trim or break inside a word, and wrapped text cut off by any clipping parent |
| `Ui/UiMultiComponentTests` | the restaurant sign through the screens (request items, V1 rejected / V2 approved, estimate component grid, job component lines with issue per line, direct purchase, external service, manual cost and reversal, added line, save job as template, estimate from template); 20 lines entered in the estimate grid, saved, reopened and scrolled in English and Arabic |

## Latest results

1.1.0-rc1, commit `28a735f`, Release configuration (`tools/release.sh`), Linux .NET 10.0.112:
**61 passed, 0 failed, 0 skipped** (unit 24, integration 30, UI 7) in 5 min 36 s.
Windows results are in [FINAL_QA_REPORT.md](../FINAL_QA_REPORT.md) §14.

Performance on the large database (3,000 customers, 10,000 jobs, 10,000 invoices, 20,000 stock movements,
50,000 journal lines; second run of each query):

| Screen / report | Time |
|---|---|
| Customers page with balances | 3 ms |
| Customer search | 5 ms |
| Jobs page sorted by customer | 6 ms |
| Jobs last page | 1 ms |
| Open invoices page | 96 ms |
| Stock movements page | 1 ms |
| Journal entries page | 2 ms |
| Trial balance (1 year) | 112 ms |
| Dashboard | 328 ms |
| AR aging report | 197 ms |
| Sales register (1 year) | 176 ms |

## Not covered by automated tests

- **Interactive Windows validation is pending.** Windows was validated by automation only (tests, smoke tests and
  a silent install on a Windows Server 2025 runner). No person has used the app by hand on Windows 10/11.
- Native file pickers and printing: tests replace the picker with fixed paths; the Windows "print" verb is untested.
- Visual review was done on headless screenshots (1366×768, 1600×900, 1920×1080); high-DPI scaling was not reviewed.
