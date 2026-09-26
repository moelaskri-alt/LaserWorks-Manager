# Final QA report — LaserWorks Manager 1.0.0-rc1

**Status: RELEASE CANDIDATE.** Automated Windows validation passed; interactive Windows validation is pending.

Build version: 1.0.0 (display 1.0.0-rc1), .NET 10.0.112, Avalonia 11.3, EF Core 10 / SQLite.
Environment: Linux x64 (4 vCPU, 15 GB), headless Avalonia with Skia rendering, Xvfb for process-level smoke tests.

## Quality gate (spec §52)

| Gate | Result | Evidence |
|---|---|---|
| Application builds | ✅ | solution builds in Debug and Release; win-x64 single-file publish succeeds |
| Database works | ✅ | migrations, WAL, integrity check; every integration test uses real SQLite |
| Login works | ✅ | SecurityTests; UI session logs in through the login screen |
| CRUD works | ✅ | UiDialogTests opens 62 editors/detail pages; workflow tests create and edit records |
| Costing works | ✅ | unit tests; estimate numbers asserted in CriticalWorkflowTests |
| Inventory works | ✅ | InventoryCostingTests; reconciliation after every scenario |
| Jobs / Production work | ✅ | 32-step workflow (services and UI) |
| Quotations work | ✅ | workflow: create from estimate, send, approve, create job |
| Invoicing works | ✅ | workflow + SalesReturnTests |
| Accounting works | ✅ | AccountingIntegrityTests; 13 reconciliation checks pass on every scenario and the demo company |
| Reports work | ✅ | 40 reports × PDF/Excel/CSV in Arabic and English |
| Backup / Restore work | ✅ | round trip, safety backup, demo restore into a fresh install, corrupted files rejected |
| Arabic / English, RTL / LTR | ✅ | every page and tab rendered in ar (RTL) and en (LTR); 1,246 keys, none missing |
| Dark mode | ✅ | every page rendered in the dark theme |
| Critical workflow passes | ✅ | services and UI |
| Sales return passes | ✅ | restock at original cost, all ledgers reconcile |
| Estimated vs actual passes | ✅ | variance per component and main driver asserted |
| Job profitability passes | ✅ | gross profit and margin asserted against the cost sheet |
| No broken buttons | ✅ (tested scope) | every New/Edit/Open command on every module; workflow buttons |
| No fake functionality | ✅ | every screen reads and writes the real database; no placeholder data |
| No critical errors | ✅ | no failing tests; no binding errors in UI tests |

## Tests

- **Linux: Passed 51, Failed 0, Skipped 0** (Release; repeated full runs clean). Details in [TESTING.md](TESTING.md).
- **Windows (Server 2025 runner, run [36207448127](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36207448127)): Passed 51, Failed 0, Skipped 0**; packaged-exe smoke
  test passed; installer silent install → start → uninstall passed.
- Performance with 3,000 customers, 10,000 jobs, 10,000 invoices, 20,000 stock movements and 50,000 journal
  lines: every screen and report measured under 0.5 s.
- Packaged application: `--smoke-test` of a single-file build (same packaging mode as the Windows exe, built for
  linux-x64) passed under Xvfb, including database creation, first screen, portable data folder, PDF and Excel export.

## Issues found and fixed during QA

- Grid columns collapsed at 1366×768 → minimum widths, horizontal scrolling, smaller cell padding
- Filters overlapped toolbar buttons on narrow windows → toolbar wraps
- Shell texts and navigation groups did not change language at runtime → per-key change notification
- Date pickers bound to the wrong type (dates not shown in filters) → view models use `DateTime?`
- Formatted grid cells logged conversion errors (read-only converters) → one-way converters
- Account names in English on the Arabic trial balance and journal → names follow the UI language
- Donut chart legend garbled in Arabic → label and value drawn separately
- Permission matrix check boxes needed two clicks → live check boxes
- New estimates kept a stale selling price after the inputs changed (could quote below cost) → price follows the
  suggestion until the user types one
- Portable mode would not find `portable.flag` in the single-file exe → resolved from the exe location
- Profitability defaulted to unfinished jobs (margins of 100 % on jobs without cost yet) → completed jobs by default
- Demo data had no sales in the current month → demo timeline shortened
- Two help texts described supplier-invoice price differences that the system does not post → corrected
- Intermittent test-host crash (native SIGSEGV, about 1 run in 3). A runtime crash report located it in
  `sk_image_encode`, called by the **test harness's** screenshot helper while encoding the headless window surface;
  application code was not on the stack. Fixed by rendering each screenshot into an owned `RenderTargetBitmap` and
  encoding the PNG in managed code; 8 consecutive UI runs and repeated full runs were then clean. Language-change
  notifications are now also marshalled to the UI thread (reports switch language from background code)
- Demo company: opening cash was dated after the first expenses, so the cash ledger briefly went negative → opening
  balances dated at the start of the first demo month

## Known limitations

- Single company, single currency; no payroll, fixed-asset register, CRM, nesting engine or machine control (by design, spec §50).
- Supplier invoices post at goods-receipt cost; price differences need a receipt correction or journal entry.
- The utilisation calculator is an estimate (area/grid), not true nesting.
- Printing uses the Windows "print" verb for PDFs (falls back to opening the PDF).
- Some long texts are truncated in grid cells at 1366×768 (full text in the record and in exports).

## Known risks

- **Interactive Windows validation is pending.** On Windows the app was exercised only by automation on a Windows
  Server 2025 CI runner (tests, start-up smoke test, silent install/uninstall). Hands-on use on Windows 10/11 —
  native file dialogs, printing, high-DPI scaling, SmartScreen, the interactive installer pages — has not happened.
- Unsigned executable and installer (SmartScreen warning).
- QuestPDF Community License applies to companies under USD 1M revenue.
- SQLite is single-machine; sharing the data folder over a network drive is not supported.

## Release artifacts

Built natively on Windows by the CI workflow and published as the build artifact **LaserWorksManager-windows**
of run [36207448127](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36207448127) (recommended for distribution; attached to a GitHub release for `v*` tags).
The same set can be built locally with `tools/release.sh` into `artifacts/release/` (not committed to git):

| File | Size |
|---|---|
| LaserWorksManager.exe | 65 MB |
| LaserWorksManagerSetup.exe | 59 MB |
| LaserWorksManager-1.0.0-rc1-win-x64-portable.zip | 60 MB |
| LaserWorksDemo.lwbak | 0.1 MB |
| LaserWorksDemo-database.zip | 0.1 MB |
| RELEASE_NOTES.md, SHA256SUMS.txt | — |
