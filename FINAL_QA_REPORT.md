# LaserWorks Manager — Final QA Report (Master QA, reporting fix)

| | |
|---|---|
| **Version** | 1.1.0-rc1 (Version 1.1.0, AssemblyVersion/FileVersion 1.1.0.0, InformationalVersion 1.1.0-rc1) |
| **Build date** | 2026-09-26 |
| **Code commit** | `6faf305` (last application-code change; release artifacts built from it) |
| **Branch** | `claude/affectionate-hawking-i1ywf5` |
| **Toolchain** | .NET SDK 10.0.112 (Linux build), Avalonia 11.3, EF Core 10, SQLite; Windows CI: windows-latest (Windows Server 2025) |

The commit that adds this report also adds step 3 (reference attachment) to the serving-board end-to-end test and
updates documentation. It does not change application code. The full suite passed **75/75** again on that test code
(Release, Linux, 8 min 2 s).

---

## 1. Release status

# NOT READY FOR DELIVERY

Every blocker that automation can check is resolved, including the **System.Object** report blocker. One
blocker remains, and automation cannot clear it:

**Exact blocker list**

1. **Interactive Windows runtime validation is pending.** No person has installed and used this build on Windows 10
   or Windows 11. The following have not been done by hand on Windows:
   - install;
   - launch and login;
   - the end-to-end workflow through the screens;
   - reports on a physical display: screen, PDF, Excel, print;
   - backup → modify → restore → close → reopen;
   - uninstall.

   Windows is validated by automation only (§12). **Windows runtime validation pending.**

When that checklist passes on Windows 10/11, the status can change to READY FOR DELIVERY. No code change is known to
be needed.

---

## 2. Summary

| Area | Result | Evidence |
|---|---|---|
| **Build** | **PASS** | Release build 0 errors; self-contained win-x64 exe, NSIS installer, portable zip (`tools/release.sh`, exit 0) |
| **Tests — total** | **75 / 75 passed**, 0 failed, 0 skipped | Release configuration, `tools/release.sh` on `6faf305`, 7 min 19 s (`artifacts/test-results/results.trx`) |
| Unit | 24 / 24 | calculators, inventory math, pricing and estimate, passwords |
| Integration | 43 / 43 | real SQLite, production service registrations, nothing mocked |
| UI (headless Avalonia, real XAML) | 8 / 8 | every page, tab and dialog; layout matrix; report screen; 2 UI end-to-end workflows |
| Reporting (subset of the above) | 10 / 10 | `ReportTests` 2, `ReportOutputTests` 7, `UiReportScreenTests` 1 |
| End-to-end (subset of the above) | 9 / 9 | 32-step workflow (service + UI), restaurant sign (service + UI), serving board (39-step, §7), reference job × 3 |
| **Reports** | **41 total · 41 tested · 41 passed** | every report on the data (Integration) and on the real Reports screen (UI) |
| **System.Object occurrences** | **0** | 118 report tables / 9,064 cells (en + ar, data + PDF/Excel/CSV scans); 82 screen runs / 3,276 rendered cells |
| Accounting | **PASS** | every entry balanced; demo company 15/15 reconciliation checks |
| Inventory | **PASS** | roll-forward in quantity and value; GL = stock value for 5 stock accounts + remnants |
| Costing | **PASS** | reference job 1,990 agrees across 5 places at profit, at loss and with variance |
| Multi-component | **PASS** | 6 service tests + 2 UI tests; no double cost for purchased components |
| Sales return | **PASS** | `SalesReturnTests` |
| Backup / restore | **PASS (automated, Linux)** · Windows **PENDING** | `BackupRestoreTests` 3/3 + backup → modify → restore → close → reopen |
| UI layout (ar/en × RTL/LTR × light/dark × 3 resolutions) | **PASS** | 384 screenshots, 70,316 element checks, 0 problems (matrix) + every page/tab/dialog audited |
| Performance | **PASS** | large database: slowest screen 358 ms (dashboard) |
| Windows automated (CI) | **PASS** | run 13 on `6faf305`: 75/75 tests, packaged-exe smoke test, demo 15/15, silent install → start → uninstall (§12) |
| **Windows interactive runtime** | **PENDING** | not performed; see §1 |

---

## 3. The System.Object blocker (root cause and fix)

**Symptom.** Report data cells on the Reports screen showed `System.Object` instead of values. Exports were
correct.

**Root cause.** The screen bound each column to the report row's mutable `object?[] Cells`. Avalonia's
`DataGridTextColumn` binds **TwoWay** by default. When a cell was realised, the binding wrote its value back into the
array, and the next read displayed the object's type name. The data layer was correct; the defect was in the
display binding.

**Fix, at the root.**
- The report screen now binds to a strongly typed read-only display row, `ReportGridRow`.
  - `Text` is an `IReadOnlyList<string>`, formatted once by `CellFormatter` with the company's decimal places.
  - `Source` keeps the original `ReportRow` for drill-down.
  - `Style` carries the row style.
- Every column binding is explicitly **OneWay** and `IsReadOnly`. No `ToString()` is used to hide objects.
- `ReportRow.Cells` is now `IReadOnlyList<object?>`: a cloned array wrapped by `Array.AsReadOnly`. Nothing can write
  into report data.
- The same scan found raw internal codes, and these were fixed too:
  - source codes (`JobOperation`, `DirectCost`, …) in the job cost sheet, general ledger, journals and ledger
    screens, and their exports;
  - entity class names in the audit log;
  - sequence keys on the numbering screen;
  - raw JSON and status tokens (`QualityCheck`) in the audit details.

  All are now translated through `Loc.Source`, `Loc.EntityName`, `Loc.SequenceName` and `AuditText`.

**Regression tests that fail on the defect**

| Test | What it checks |
|---|---|
| `Ui/UiReportScreenTests.Every_report_renders_business_values_on_screen` | Opens all 41 reports on the **real Reports screen** (Arabic/Light and English/Dark). It reads the rendered cell text, checks that the report data is unchanged after display, checks the headers, and checks that a row's drill-down opens the source job. **Reproduced the defect before the fix** (every data cell `System.Object`); 0 problems after. |
| `Integration/ReportOutputTests.No_report_shows_object_names_or_internal_codes` (en, ar) | Every report runs unfiltered, filtered, and per job for the job reports. Every cell must be a display type (string, number, date, bool). Its formatted text, the Excel file and the CSV file must not contain `System.`, `Microsoft.`, `LaserWorks.`, anonymous types, `null`, exception text, `{x=` or an untranslated internal code. |
| `ReportOutputTests.The_check_catches_object_artifacts` | Proves the check itself flags `new object()`, a list, an anonymous type and a raw code. |
| `ReportOutputTests.Every_source_code_has_a_translated_name`, `Every_audited_entity_and_sequence_has_a_translated_name`, `Audit_trail_details_are_readable` (en, ar) | Every code the services write has a translation. All 443 distinct audit details of the demo company display without codes or JSON. |
| Layout audit (smoke, dialog, matrix) | Fails if any visible text on any page, tab or dialog shows an object name, internal code or serialized JSON. |

**Pipeline checked:** service → `ReportTable` → `ReportGridRow` (screen) / `PdfExporter` (PDF) / Excel / CSV. The
serving-board test also renders the job cost report to PDF and Excel and reads the Excel file back (totals, translated
sources).

---

## 4. Reports

| | |
|---|---|
| Total reports | **41** |
| Reports tested | **41** (data + PDF/Excel/CSV + real screen) |
| Reports passed | **41** |
| System.Object occurrences | **0** |
| Other artifacts (type names, codes, JSON) | **0** |

Coverage by group: customers, quotations, jobs, costing and profitability, production and machines, inventory,
purchasing and sales, and accounting.

The job reports (JobCostSheet, EstimatedVsActual, JobComponents) run for every job of the demo company. Each report
runs in Arabic and English.

---

## 5. Costing — the reference job (PASS)

`BusinessValidationTests.Reference_job_costs_1990_and_profit_is_the_same_everywhere` builds the job through the real
services. The rates are set so each cost equals the specification:
- machine rate override 45/h, 10 h → 450;
- operator 30/h, 10 h → 300;
- overhead rate 0.

| Component | Amount |
|---|---|
| MDF | 500 |
| Acrylic | 350 |
| LED | 180 |
| Wire | 75 |
| Glue | 25 |
| Packaging | 60 |
| Labor | 300 |
| Machine | 450 |
| Scrap | 50 |
| **Total actual cost** | **1,990** |

| Case | Price | Actual cost | Gross profit | Margin | Checked in |
|---|---|---|---|---|---|
| Profit | 3,000 | 1,990 | **1,010** | **33.67 %** | job, job profitability report, job cost report, income statement, dashboard |
| Loss | 1,500 | 1,990 | **−490** | **−32.67 %** | same five places; loss flagged and row styled *Negative* |
| Variance | 3,000 | 2,140 | **860** | **28.67 %** | MDF 2.44 sheets (560) and 12 machine hours (540); variance shown per line; main driver = machine time |

The same figures are used in the commercial demo script.

---

## 6. Other validations

| Validation | Result | Test |
|---|---|---|
| **Multi-component / purchased components** | PASS | `MultiComponentTests` (6).<br>A purchased component is either received into stock and issued, or recorded as a direct purchase on its line, never both; a direct cost on a stocked line is refused (`Err.ComponentIsStocked`) and a stock issue on a direct line is refused (`Err.ComponentNotStocked`).<br>Ledger checked per account. 20-line stress. |
| **No duplicate cost** | PASS | Recording a direct cost on a stock line is rejected. Line actuals plus non-line rows = actual cost = WIP. |
| **Remnants** | PASS | `InventoryCostingTests.Remnant_takes_area_proportional_value_out_of_the_job_and_can_be_reused` checks dimensions, thickness, warehouse, source and consuming job, value and *Consumed* status.<br>The value leaves remnant inventory and enters the consuming job's line cost. |
| **Inventory roll-forward** | PASS | `Inventory_roll_forward_and_moving_average`.<br>Receipts at different prices, issue, return, adjustments (+/−), scrap from stock, transfer.<br>Quantity and value roll forward; moving average checked against hand calculation (stock value 4,036.76). |
| **Accounting** | PASS | `AccountingIntegrityTests` (5): unbalanced entries rejected, posted lines immutable, reversal, closed periods.<br>Demo company: ledger debits = credits 504,514.49; every posted entry balanced; AR, AP, WIP and inventory reconcile (15/15). |
| **Sales return** | PASS | `SalesReturnTests`: return at the original cost although the average moved; COGS, inventory, revenue, VAT and receivable reconcile. |
| **Profitability (profit and loss jobs)** | PASS | §5 |
| **Backup → modify → restore → close → reopen** | PASS (Linux automation) | `Backup_restore_then_close_and_reopen` reopens the same database folder in a new service provider after restore.<br>`BackupRestoreTests` (3): safety backup; corrupt and foreign files rejected. |
| **Security / data integrity** | PASS | `SecurityTests` (4): hashing, lockout, permissions in services, audit.<br>`Duplicates_required_fields_and_records_in_use_are_refused`. |
| **Migration from 1.0.0-rc1** | PASS | `MigrationTests`: no rows lost, no amount re-priced. |
| **Performance** | PASS | 3,000 customers, 10,000 jobs, 10,000 invoices, 50,000 journal lines.<br>Dashboard 358 ms, trial balance 134 ms, AR aging 200 ms, sales register 140 ms, open invoices 100 ms, list pages ≤ 7 ms. |

---

## 7. The 39-step end-to-end workflow

`MultiComponentTests.Serving_board_with_purchased_accessory_end_to_end` runs all 39 steps in one test, through the
production services on a real database:

| Steps | What the test does |
|---|---|
| 1–3 | Customer, request with 5 requested items, reference file attached (SVG) |
| 4–6 | Revision V1 rejected, V2 created and approved |
| 7–10 | Plywood + acrylic (raw), brass handle (purchased: PO → receipt → supplier invoice), glue/tape (consumable), gift box (packaging) |
| 11–15 | Sheet usage, machine line, labor line, overhead, scrap and rework allowances, estimate |
| 16–18 | Quotation, approved, converted to job |
| 19–21 | Materials and purchased component issued per line, acrylic logo from a remnant, off-cut saved as a remnant |
| 22–27 | Machine time and labor on operations, scrap, rework, production completed, quality check passed |
| 28–30 | Delivered, invoiced, paid |
| 31–33 | Actual cost, estimated vs actual per line, profitability = cost sheet |
| 34–35 | Stock movements linked to job and line; ledger per account; every journal entry balanced on its own |
| 36–39 | Job cost report run; exported to **PDF** (valid) and **Excel**; the Excel file read back: amounts total, sources translated |

Also passing:
- the 32-step workflow at service level (`CriticalWorkflowTests`) and through the desktop view models and dialogs
  (`UiCriticalWorkflowTests`);
- the restaurant-sign multi-component workflow at service level and through the screens (`UiMultiComponentTests`).

---

## 8. UI and DataGrid audit

- **Matrix:** critical screens and dialogs at 1366×768, 1600×900 and 1920×1080 × Arabic/English × RTL/LTR ×
  light/dark. Result: **384 screenshots, 70,316 element checks, 0 problems.**
- **Smoke:** every page and every tab in 4 combinations:
  - 1366 ar/Light;
  - 1366 en/Dark;
  - 1600 en/Light;
  - 1920 ar/Dark.
- **Dialogs:** 62 dialogs and detail pages opened in Arabic and English.
- **What the audit fails on:**
  - buttons unreachable;
  - dialog footer off screen;
  - grid wider than the window or squeezed;
  - header trimmed or broken inside a word;
  - wrapped text clipped by any clipping ancestor;
  - an object name, internal code or JSON shown.
- **Report columns** are sized to their content (up to 360 px) with horizontal scrolling. Before this change, job
  numbers were cut ("JOB-00…").
- The codebase audit found no TODO, `NotImplementedException`, placeholder text or swallowed exceptions. It found no
  button without a command; the dialog test executes every New/Edit/Open command.

---

## 9. Defects found and fixed in this phase

| # | Defect | Fix | Test |
|---|---|---|---|
| 1 | **System.Object in every report data cell on screen** (release blocker) | Read-only `ReportGridRow`, OneWay bindings, read-only `Cells` | `UiReportScreenTests` (failed before) |
| 2 | Raw source codes in job cost sheet, GL, journals, ledger and exports | `Loc.Source` + 20 translations | `ReportOutputTests` |
| 3 | Report columns truncated | Content-measured minimum widths + horizontal scroll | report screen and matrix |
| 4 | Numbering screen showed sequence keys | `Conv.SequenceName` | smoke audit (Windows CI run 9 caught it) |
| 5 | Audit log showed entity class names and raw JSON | `Conv.EntityName`, readable change sets | smoke audit |
| 6 | Audit details showed status tokens (`QualityCheck`) | `AuditText` translates status values; also in the audit export | `Audit_trail_details_are_readable`; Windows CI run 11 caught it |
| 7 | Test harness deleted the data folder, so reopen could not be tested | `TestDb.KeepFolder` | backup/close/reopen test |

---

## 10. Known issues

None open in application code.

---

## 11. Known limitations

- **Windows interactive validation is pending** (§1).
- Visual review used headless Skia screenshots, not a physical Windows display. High-DPI scaling (125–200 %) was not
  reviewed.
- Grids have minimum widths but no global maximum width. A maximum on an unmeasured column crashes inside the
  Avalonia DataGrid. Report columns are capped at 360 px by content measurement.
- Single PC, single company, single currency per database. Not a network multi-user system.
- The sheet utilisation calculator is an estimate, not a nesting engine.
- No payroll and no fixed-asset register. Machine depreciation is inside the machine hourly rate.
- The executable is not code-signed, so Windows SmartScreen may warn on first run.
- Native file pickers and printing through the Windows print verb are not covered by automation.
- Migration was proven on the 1.0.0-rc1 demo database. Back up a real database before upgrading.

---

## 12. Windows validation

**Automated (GitHub Actions, windows-latest / Windows Server 2025).** Each run does:
- build;
- the full test suite;
- `LaserWorksManager.exe --smoke-test` on the published exe;
- demo company reconciliation;
- NSIS installer build;
- silent install, start of the installed exe, silent uninstall;
- artifact upload.

| Run | Commit | Result |
|---|---|---|
| 9 | `7fa9f4e` | failure: numbering keys and audit entity names shown raw (defects 4, 5) |
| 10 | `d80dd06` | success |
| 11 | `79da25b` | failure: audit details showed `QualityCheck` (defect 6) |
| 12 | `416cf74` | success |
| **13** | [`6faf305`](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36226746918) | **success**:<br>• build 0 warnings / 0 errors;<br>• tests **75 passed, 0 failed** (7 min 6 s);<br>• packaged exe smoke test passed;<br>• demo company 15/15 PASS;<br>• NSIS installer built;<br>• silent install → start → uninstall passed;<br>• artifact `LaserWorksManager-windows` uploaded |

**Backup/restore on Windows: PENDING.** It is tested by automation on Linux. The Windows CI suite runs the same
tests, but restore has not been performed by hand on Windows.

**Windows runtime: PENDING.** Windows runtime validation pending.

---

## 13. Release artifacts

Built by `tools/release.sh` from `6faf305` into `artifacts/release/`:

| File | Size | SHA-256 |
|---|---|---|
| `LaserWorksManager.exe` (self-contained win-x64) | 65.5 MB | `0d4acaeb635dd9be1b064f5dd5193ccad814a56f54b673d3ed209ec5c30bfbe4` |
| `LaserWorksManagerSetup.exe` (NSIS) | 59.2 MB | `721cce022553ad0bc40168d0ba0dda9d8e794747680fb485a614f3f3718b3b19` |
| `LaserWorksManager-1.1.0-rc1-win-x64-portable.zip` | 59.9 MB | `424762a4617bcee9a204494f3bc2457a7cdf53f7d7914117d0d2e163db867b4b` |
| `LaserWorksDemo.lwbak` | 135 KB | `fe2e44309f6ec5440e566544f36ddb85fc43e9504ee71144a133a545f908d5db` |
| `LaserWorksDemo-database.zip` | 134 KB | `9839eb8c260904d3d6768e5412c6048f6d00a0548d49c4e25ed93865e547c1e1` |
| `RELEASE_NOTES.md`, `SHA256SUMS.txt` | | |

The demo company in these artifacts passes 15/15 reconciliation checks.

---

## 14. Commercial package

Written in Egyptian Arabic with English software terms. Every figure and screen in it is covered by the tests above.
The documents make no claims beyond tested behaviour and list the limitations openly.

| File | Content |
|---|---|
| `COMMERCIAL_DEMO_SCRIPT_AR.md` | 15 scenes (~14 min): screen, click, say, why it matters, key message, transition, and VIDEO PRODUCTION NOTES |
| `COMMERCIAL_FAQ_AR.md` | 26 questions, including honest "no" answers |
| `PRODUCT_DESCRIPTION_AR.md` | short, medium, long descriptions, requirements, limitations |
| `docs/USER_GUIDE_AR.md` | Arabic user guide (16 sections) |
| `docs/RELEASE_NOTES.md` | release notes including the report fix |
