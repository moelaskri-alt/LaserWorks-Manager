# LaserWorks Manager — Final QA Report

**Build:** 1.1.0-rc1 (assembly 1.1.0.0) · commit `28a735f` · branch `claude/affectionate-hawking-i1ywf5` · 2026-09-26
(the commit adding this report changes documentation only; all code and test evidence is from `28a735f`)

## Summary of results

| Area | Result | Evidence |
|---|---|---|
| **Tests (whole suite)** | **61/61 passed**, 0 failed, 0 skipped | Release configuration, `tools/release.sh` run on commit `28a735f` (Linux, .NET 10.0.112) |
| Unit | **24/24 passed** | calculators, inventory math, pricing/estimate (incl. the new cost components), passwords |
| Integration | **30/30 passed** | real SQLite database, production services, nothing mocked |
| UI (headless Avalonia, real XAML) | **7/7 passed** | all pages, all dialogs, layout matrix, two UI end-to-end workflows, 20-line stress |
| End-to-end | **Passed** | 3 service-level E2Es + 2 UI E2Es (see §8) |
| Multi-component | **Passed** | 6 service tests + 2 UI tests |
| Accounting reconciliation | **Passed** | every test scenario + demo company: 15/15 checks, every posted entry balanced individually |
| Inventory reconciliation | **Passed** | GL = stock value for all 5 stock accounts + remnants, quantities = warehouse balances = stock ledger |
| Costing reconciliation | **Passed** | component lines + non-line rows = estimate total and = actual job cost = WIP per job |
| Backup/Restore | **Passed** | `BackupRestoreTests` 3/3 |
| Data migration (rc1 → 1.1) | **Passed** | real 1.0.0-rc1 demo database upgraded: no rows lost, no amount re-priced |
| UI layout audit | **Passed** | 384 screenshots / 15,948 element checks / 0 problems (matrix) + every page, tab and dialog audited |
| Performance | **Passed** | 3,000 customers, 10,000 jobs, 10,000 invoices, 50,000 journal lines: slowest screen 328 ms (dashboard) |
| Windows automated (CI) | **Passed** | run 36222354588 on the final commit: 61/61 tests, packaged-exe smoke test, demo 15/15, silent install → start → uninstall on Windows Server 2025 |
| **Windows interactive runtime** | **PENDING** | No person has installed and used this build by hand on Windows 10/11. **Windows runtime validation pending.** |

The same suite also passed 61/61 in Debug configuration on Linux before the release build; three full regression
runs were made after the last code change set (Debug run, then two `release.sh` runs), all 61/61.

---

## 1. What was found

Business model:
- A request, estimate and job could carry **only one material**. Purchased components (LEDs, adapters, screws,
  handles), consumables, packaging and outsourced services could not be planned or costed per job.
- The item master had only raw material / consumable / finished good. Purchased components had to be faked as raw
  materials, and there were no separate inventory accounts for components or packaging.
- There was no way to charge a direct purchase or subcontractor bill to a job line. Nothing stopped a cost from
  being charged both through stock and directly.
- Estimated vs actual worked only by cost component, not by material/component line.

UI:
- Several grids truncated headers ("المراجعة…", "الأبع…", "Qty per…") and important columns.
- Grids and panels were squeezed on 1366×768 instead of scrolling. On the job screen the grids showed about four rows.
- **Every dialog cut off wrapped text on the right.** The dialog ScrollViewer had padding, so content was measured
  36 px wider than it was arranged. This was found by the new layout audit.
- The request screen had one material dropdown, no attachment details, and a narrow revision list.

Found during this work (all fixed, each with a test that fails without the fix):
- The old estimate editor saved every line with `MaterialId = 0` when no item was chosen, and dropped the category,
  source and remnant.
- A free-text purchased item on a request became a "manual cost" line instead of a direct purchase.
- A product template re-priced external services from the item master, losing the job's quoted price.
- Remnants saved from a job wrote their stock movement without the job's component line.
- The migration test's fixture database was excluded by `.gitignore` (`*.db`), so the test ran only locally;
  Windows CI caught it.
- Changing the language on a non-UI thread re-measured grid headers off the UI thread and crashed
  (Windows CI run 4 and a local ordering-dependent failure).
- The demo "UV printing" service had a standard cost of 0, so its estimate line was 0.

## 2. What was fixed

Every item in §1. In detail:
- **Multi-component architecture** end to end: request items → estimate component lines → job component lines →
  stock issues, remnants and direct costs per line → estimated vs actual per line → reports and cost sheet (§5).
- **Rules that prevent double charging**, enforced in the service layer (not the UI):
  - a stock line is costed only by stock issues and remnants;
  - a direct line is costed only by the cost recorded on it;
  - services can never be stocked;
  - remnants must be raw material;
  - a line with cost keeps its item, source and type.
- **UI standard applied globally** (§4). It is enforced by an automated audit that fails the build on truncated
  headers, clipped text, unreachable buttons, off-screen dialog footers and squeezed grids.
- Every bug in the second half of §1, each with a regression test.

## 3. Database changes

EF Core migration `20260926015011_MultiComponentJobs`, applied automatically at start-up:

| Change | Detail |
|---|---|
| New table `JobComponents` | job, line no, type (category), source, item, remnant, description, unit, planned qty, estimated unit cost / cost (snapshot), estimate line, supplier, notes; cascade with the job |
| New table `RequestItems` | request, line no, type, item (optional), free-text description, quantity per unit, unit, notes |
| New table `ProductTemplates` | code (TPL-…), name, description, quantity, JSON bill of materials / routing, source estimate |
| `EstimateMaterialLines` | + line no, type, source, remnant, description, unit; item now optional (free-text / service lines) |
| `CostEstimates` | + rework allowance % |
| `InventoryTransactions`, `JobCostEntries` | + `JobComponentId` (FK, indexed) |
| `CustomerRequests` | single `MaterialId` / `Thickness` removed **after** their data is copied into `RequestItems` |
| Enums | item kinds + purchased component, packaging, service; cost components + purchased components, external services |
| Chart of accounts | + 1240 Inventory – purchased components, 1250 Inventory – packaging (added to existing databases at start-up) |

**Data migration:**
1. Each request's material becomes a request item.
2. Each estimate line gets a type from its item kind and the source "Inventory".
3. Each job gets one component line per estimate line.
4. Materials issued without an estimate line get an "unplanned" line.
5. Stock movements and material cost entries are linked to their lines.

No amount is recalculated.

**Verified by `MigrationTests`** on the real 1.0.0-rc1 demo database (13 jobs, 19 requests, 17 lines created):
- row counts of every table are unchanged;
- every journal amount, stock movement, cost entry and job total is unchanged;
- every job movement is linked to a line;
- line totals equal the job totals;
- all reconciliation checks pass;
- all 41 reports still run.

## 4. UI changes

- **Request editor**: header fields plus tabs.
  - *Requested items*: unlimited lines, add / duplicate / remove, item or free text, type, quantity per unit, unit, notes.
  - *Attachments*: add / open / remove, with name, type, size, date and user.
  - *Design revision*: revision, date, designer, dimensions, main material, machine minutes, files and status;
    new / open / approve.
- **Estimate editor**: *Components & materials* grid with a selected-line panel.
  - Lines can be added from inventory, purchased for the job, as an external service, from a remnant or as another cost.
  - Lines can be duplicated or removed.
  - **New item** creates an item master record from the line.
  - The panel holds sheet nesting with pieces and a remnant picker.
  - New fields: rework allowance and a component total. **Save as template** is added.
- **Job screen**:
  - *Components* tab with add / edit / duplicate / delete, issue / return, use remnant, record direct cost,
    and save job as template.
  - *Cost & variance* tab shows component-level estimated vs actual above the cost-component table.
  - *Reverse direct cost* on a selected cost entry.
  - New dialogs: component line editor (with **New item**) and direct cost.
  - Issue and remnant dialogs are bound to the selected line.
- **Estimates list**: *New from template* (template picker with delete).
- **Items & Materials**: the new kinds, with a wider filter and column.
- **Global DataGrid standard** (`GridStandard`, applied by the global DataGrid style to every grid):
  - headers wrap and are never trimmed;
  - each column's minimum width fits its longest header word, in the current language;
  - header and cell tooltips;
  - numbers are aligned to the cell end, mirrored in RTL;
  - empty and loading messages;
  - the first column of list pages is frozen.

  Existing features kept: search, filters, sort, resize, reorder, column chooser, export (PDF/Excel/CSV), print, paging.
- **Scrolling**: pages sit in a vertical scroller with a minimum height (`ViewportFill`). On small screens the page
  scrolls instead of squeezing grids; grids keep their own vertical and horizontal scrolling. Dialogs scroll their
  content with Save/Cancel always visible, and the clipping defect is fixed.

## 5. Multi-component architecture

```
Request ──< RequestItem (type, item or free text, qty per unit)
   │
Estimate ──< EstimateMaterialLine (type, source, item?, remnant?, sheet nesting / qty, unit cost, cost)
   │                                       │ snapshot
Job ──────< JobComponent (type, source, item?, planned qty, estimated cost, supplier)
                 ├──< InventoryTransaction (issue / return / remnant saved or used)  ← stock lines only
                 └──< JobCostEntry (stock cost, remnant value, or DirectCost with its journal) ← direct lines only
```

- **Types:** raw material, purchased component, consumable, packaging, external service, other direct cost.
  Each maps to one cost component.
- **Sources:** inventory, remnant, direct purchase, external service, manual cost.
- A purchased component is either received into stock (Dr Inventory – purchased components) and issued to the line,
  or its bill is recorded on a direct-purchase line (Dr WIP / Cr supplier). It is never both.
- An estimate aggregates materials, purchased components, consumables, packaging, external services, machine,
  maintenance, labor, design, setup, finishing, other direct costs, scrap allowance, rework allowance and overhead
  (15 components).
- Actual cost = the sum of cost entries. **Estimated vs actual by line:** line rows first, then rows for costs not on
  a line (machine, labor, overhead, scrap, rework, per-unit allowances). They add up exactly to the estimate total
  and the actual cost. Tested with unlimited lines (20 in the stress tests).
- **Product templates:** save an estimate or a job as a template. A new estimate from a template re-prices stock
  lines at today's average cost and keeps the template's price for direct purchases and services.

## 6. Accounting validation

Verified by assertions on ledger balances, not only by totals:

| Step | Posting checked |
|---|---|
| Purchase LED / handle for stock | receipt: Dr **Inventory – purchased components** 280 / Cr GRNI 280; supplier invoice: Dr GRNI + input VAT / Cr **AP 322**; GRNI back to 0 |
| Issue components and material to job | Dr WIP (job) / Cr the item's inventory account (raw −q×62, components −140, remnants −12 + offcut) |
| Direct purchase on credit | Dr WIP 52 + input tax 7.80 / Cr AP 59.80 (supplier); no stock movement for the line |
| External service / manual cost, cash | Dr WIP + tax / Cr Cash; reversal returns WIP and the line to 0 while keeping the original entry |
| Completion / delivery | WIP (job) = actual cost of the job |
| Invoice | Dr AR = invoice total / Cr Sales + output VAT; Dr COGS = actual job cost / Cr WIP → WIP (job) = 0 |
| Payment | AR (customer) back to 0 |
| Every posted journal entry | Σ debit = Σ credit **per entry** (serving-board E2E) and overall (all scenarios) |

The demo company (release artifact) passes all 15 reconciliation checks, including "every posted entry balances"
(0 unbalanced) and ledger debits = credits (504,514.49).

## 7. Inventory validation

- Inventory GL equals stock value for each account: raw 15,899.69, consumables 298.80, finished goods 360.00,
  purchased components 3,675.60, packaging 690.90, remnants 138.62 (demo company). The same is checked after every
  test scenario.
- Material quantity = warehouse balances = stock ledger.
- Every job stock movement references the job, component line, date, warehouse, quantity, unit cost, total cost and
  user. Asserted in the serving-board E2E; demo company: 0 unlinked movements.
- A remnant used on a line leaves remnant inventory at its value and becomes *Consumed*. A remnant saved from a job
  reduces that line's cost.
- Services cannot be stocked. Over-issue and over-return are rejected (existing tests).

## 8. Costing validation

End-to-end cases (service level unless noted):
- **Restaurant illuminated sign (spec §28):**
  - 9 lines, including acrylic sheet (nesting), LED, adapter, screws, tape, box, UV service, mounting kit
    (direct purchase) and installation;
  - design V1 rejected, V2 approved; remnant; scrap; rework; QC; delivery; invoice; payment;
  - estimate 917.30, actual 912.62, revenue 1,310.43, profit 397.81 (30.36 %).
- **Engraved wooden serving board with restaurant logo (spec §29):**
  - plywood, acrylic logo from a remnant, brass handle bought through PO → receipt → supplier invoice → issue,
    gift box, glue/tape;
  - operations, scrap, rework, QC, completion, delivery, invoice, payment;
  - estimate 409.11, actual 460.82, revenue 584.44, profit 123.62 (21.15 %).
- **Original 32-step workflow:** still passes (service and UI).
- **UI:** restaurant sign through the screens, with request items, V1/V2, estimate component grid, job component
  lines, issue per line, direct purchase, external service, manual cost plus reversal, an added line, save as
  template and estimate from template.

In every case the checks hold:
- category totals = estimate cost components;
- line estimates = estimate total;
- line actuals + non-line rows = actual cost = WIP;
- gross profit = revenue − actual;
- margin = profit ÷ revenue;
- job profitability report = cost sheet.

**Stress:** 20 lines of every type and source through estimate, duplicate, quotation, job, issue / direct cost and
report (service); 20 lines entered in the estimate grid, saved, reopened in order and scrolled to the last line, in
English and Arabic at 1366×768 with the layout audit (UI).

## 9. Test count

61 automated test methods. Most are scenario tests with many assertions each.

| Group | Tests |
|---|---|
| Unit | 24 |
| Integration | 30 (accounting integrity 5, backup/restore 3, critical workflow 1, inventory costing 2, localization 4, **migration 1**, **multi-component 6**, performance 1, reports 2, sales return 1, security 4) |
| UI (headless) | 7 (smoke 2, dialogs 1, critical workflow 1, **layout matrix 1**, **multi-component 2**) |

Coverage of the 25 required areas:

| # | Area | Test |
|---|---|---|
| 1 | Job with zero components | `Job_without_components_and_manual_lines` |
| 2 | Job with one component | `CriticalWorkflowTests` (1 line), `Job_without_components_and_manual_lines` (line added later) |
| 3 | Multiple components | restaurant sign (9–10 lines), serving board (5) |
| 4 | 20 components | `Twenty_component_lines` + `Twenty_component_lines_in_the_estimate_grid` (UI) |
| 5–10 | Material / purchased / consumable / packaging / remnant / external service | restaurant sign, serving board, twenty lines |
| 11–13 | Estimated / actual / estimated vs actual | all E2Es (per line and per component) |
| 14–15 | Inventory issue / purchased component issue | serving board (PO → receipt → invoice → issue), restaurant sign |
| 16–17 | Scrap / rework | restaurant sign, serving board, critical workflow |
| 18 | Profitability | all E2Es (`JobProfitabilityAsync` = cost sheet) |
| 19 | Accounting posting | serving board (per account and per entry), `Component_rules_are_enforced` (reversal), accounting integrity |
| 20 | Sales return | `SalesReturnTests` |
| 21–22 | Backup / restore | `BackupRestoreTests` |
| 23–24 | Arabic / English UI | smoke, dialog, layout matrix, 20-line UI test (both languages) |
| 25 | DataGrid scrolling/layout | layout matrix + audit in smoke and dialog tests; 20-line grid scroll |

## 10. Passed tests

**61/61** (Release, commit `28a735f`, 5 min 36 s). Earlier full runs after the last functional changes: 61/61 (Debug)
and 61/61 (Release, previous `release.sh`).

## 11. Failed tests

**0.** Failures seen during development were fixed and re-run (§1, second list), including:
- two Windows CI failures: the missing fixture, and the off-UI-thread header re-measure;
- false positives in the first version of the layout audit, fixed by making it inspect the real laid-out text lines instead of estimated text widths.

## 12. Known limitations

- **Windows interactive validation is pending** (§14).
- Visual review was done on headless Skia screenshots, not on a physical Windows display. High-DPI scaling (125–200 %)
  was not reviewed.
- Grids set minimum widths but no global maximum. Setting a maximum on an unmeasured column crashes inside the
  Avalonia DataGrid, so it was left out. Users can widen columns freely.
- The empty-grid message appears in the rows area. A *loading* message only shows when a page reports it is busy
  and the grid is empty.
- The layout matrix covers the critical screens listed in spec §33 plus the request, estimate, job, component,
  direct-cost, template, issue, customer, quotation and invoice dialogs. The remaining screens are audited at
  1366×768 only, in Arabic/Light and English/Dark (smoke and dialog tests).
- A direct-purchase line is costed by the amount recorded on the line. It is not matched to a separate purchase-order
  document. Purchases into stock use the full purchasing module.
- Unchanged from 1.0: not code-signed (SmartScreen warning); single currency and company; the utilisation calculator
  is an estimate, not a nesting engine; supplier invoices are posted at receipt cost; QuestPDF Community License terms.

## 13. Known risks

- **Upgrading real customer databases.** The migration was proven on the 1.0.0-rc1 demo database only. Take a backup
  before upgrading a production database. The app also makes an automatic safety backup when you restore.
- **Windows-specific behaviour** not covered by automation: printing through the Windows print verb, native file
  pickers, fonts and RTL rendering on physical displays, and antivirus interaction with an unsigned single-file exe.
- **Performance with very large component counts per job** was tested up to 20 lines. The large-database test covers
  volume across jobs, not hundreds of lines on one job.

## 14. Windows validation status

- **Automated (GitHub Actions, `windows-latest`):**
  - Run [36221580894](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36221580894) on commit
    `4bd0571` (the last one before the remnant-link fix): **success**.
  - **Final commit `28a735f`: run [36222354588](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36222354588)
    — success** on Windows Server 2025 (10.0.26100), .NET SDK 10.0.401:
    - build: 0 warnings, 0 errors;
    - tests: **61 passed, 0 failed** (4 min 16 s);
    - published `LaserWorksManager.exe --smoke-test`: "Smoke test passed: screen SetupWizardViewModel";
    - demo company: 15/15 reconciliation checks PASS;
    - NSIS installer built; silent install, start of the installed exe (smoke test) and silent uninstall passed
      (the executable was removed);
    - portable zip and checksums built. The artifact `LaserWorksManager-windows` holds the Windows-built release.
  - Earlier failures (runs 3–5) came from the missing fixture and the off-thread header re-measure; see §1.
- **Interactive: Windows runtime validation pending.** This environment is Linux. The installer was not run by a
  person, and the manual checklist from spec §37 (launch, login, customer, request, multi-component job, cost,
  production, invoice, reports, backup, restore, close, reopen, uninstall on Windows 10/11) has **not** been
  performed by hand. It must be done before delivery to a workshop.

## 15. Release artifact names

Built by `tools/release.sh` into `artifacts/release/` (commit `28a735f`):

| File | SHA-256 |
|---|---|
| `LaserWorksManager.exe` (self-contained, win-x64, 65.5 MB) | `11674ab25327a2629d09c36f532f54fec057a2f09bbaa2ee3a6bebddd9c20035` |
| `LaserWorksManagerSetup.exe` (NSIS installer, 59.2 MB) | `28323ee31a440ad87f7cbb2b9cc3ee70567d646e5ff5514c6480db48a5b203d7` |
| `LaserWorksManager-1.1.0-rc1-win-x64-portable.zip` | `2ac4ef68d7978ecf0772e4d64f1adc9fb7cd121b817e2ad98d43092eae3441ce` |
| `LaserWorksDemo.lwbak` (demo company backup) | `3e39da0bdcdb497b5d5480dc77e59dff0487018df8a6501906c0adac2bca9cdf` |
| `LaserWorksDemo-database.zip` | `48a319c4ec2d723d9e434e5a2cde41f268132426d228e9f30ab33b643752ac32` |
| `RELEASE_NOTES.md`, `SHA256SUMS.txt` | |

The Windows CI run on the same commit builds the same set on Windows (artifact `LaserWorksManager-windows` of run
36222354588). Its checksums differ from the Linux-built files because they come from a different build machine; both
are listed in that run's `SHA256SUMS.txt` and here respectively.

## 16. Exact build version

`1.1.0-rc1` — `Version 1.1.0`, `AssemblyVersion/FileVersion 1.1.0.0`, `InformationalVersion 1.1.0-rc1`,
commit `28a735fedf2dcbdfcba7c7ee9484a811d86f09d7`, .NET SDK 10.0.112 (Linux build) / 10.0.x (Windows CI),
Avalonia 11.3, EF Core 10, SQLite.

## Release blockers (spec §35)

| Blocker | Status |
|---|---|
| Job cannot contain multiple components | **Resolved.** 0…N lines, tested to 20 |
| Purchased components cannot be costed | **Resolved.** Via stock or direct purchase, with ledger checks |
| Inventory does not reconcile | **Resolved.** All inventory checks pass |
| Accounting does not balance | **Resolved.** Every entry balances; 15/15 checks |
| Internal scrolling is broken | **Resolved.** Page scroller with minimum height, grid scrolling, dialog scrolling; audited |
| Important DataGrid columns are unreadable | **Resolved.** Wrapped headers with fitted minimum widths; 0 audit problems |
| Important buttons are inaccessible | **Resolved.** Button reachability audited on every page, tab and dialog |
| Data is lost during migration | **Resolved.** Migration test on the real rc1 database |
| Critical workflow fails | **Resolved.** 5 E2E workflows pass |
| Backup/restore fails | **Resolved.** 3/3 |
| Application crashes during normal workflow | None seen in any automated run. **Interactive Windows use is still pending.** |
| Any major button is fake/non-functional | All new buttons are driven by tests through their commands. The dialog test executes every New/Edit/Open command on every module |

Every blocker that automation can check is resolved. Delivery still requires the interactive Windows validation in §14.
