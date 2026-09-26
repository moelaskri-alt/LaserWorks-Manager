# LaserWorks Manager 1.1.0-rc1 — release notes

Second release candidate. It replaces the single-material job model with multi-component jobs and fixes the
screen layout problems found in 1.0.0-rc1. Databases from 1.0.0-rc1 are upgraded automatically on first start.
No posted amount changes during the upgrade.

## What changed since 1.0.0-rc1

**Multi-component jobs**
- Requests, estimates and jobs have unlimited component lines: raw materials, purchased components,
  consumables, packaging, external services and other direct costs.
- Each line has a source: inventory, remnant, direct purchase, external service or manual cost.
- A purchased component is either bought into stock and issued to the job, or bought directly for the job.
  It is never charged both ways: a stock line accepts only stock issues and remnants, a direct line only the
  cost recorded on it.
- The item master now distinguishes raw materials, purchased components, consumables, packaging,
  finished products and services. Two new inventory accounts: 1240 purchased components, 1250 packaging.
- Estimates have fifteen cost components, including purchased components, external services, other direct
  costs and a rework allowance.
- The estimate editor has a component grid with a detail panel. Lines can be added from inventory, purchased
  for the job, external service, remnant or other cost; they can be duplicated or removed, and a missing item
  can be created on the spot.
- The job Components tab shows planned, used and remaining quantity and estimated, actual and variance cost
  per line. From it you can issue, return, use a remnant, record or reverse a direct cost, and add, edit,
  duplicate or delete lines.
- Estimated vs actual works per component line on the job screen, in the job cost sheet (PDF/Excel), in the
  *Estimated vs actual* report and in the new *Job components* report (41 reports in total).
- Product templates: save an estimate or a finished job as a template and start new estimates from it.
- Every stock movement of a job is attached to a component line. Issues made outside the job screen go to
  the item's line, or to a new *unplanned* line.

**Reports (fixed in this release candidate)**
- The Reports screen showed "System.Object" in every data cell. The screen bound its columns straight into the
  report's values, and the grid's two-way cell binding overwrote them. The grid now shows read-only,
  pre-formatted display rows, and report values can no longer be changed by any screen.
- Internal document codes (JobOperation, DirectCost, SalesInvoice, …), audit-trail entity names and numbering keys
  are now shown with translated names. The audit trail shows readable change details instead of JSON.
- Report columns are sized to their content and scroll horizontally.

**Screens**
- Page-level scrolling with a minimum height: small screens scroll the page instead of squeezing grids.
- Dialogs keep Save/Cancel visible and scroll their content. A layout defect that cut off wrapped text on the
  right of every dialog is fixed.
- One DataGrid standard for every grid:
  - column headers wrap instead of being cut off, and each column is at least as wide as its longest header word;
  - header and cell tooltips;
  - numbers are aligned to the end of the cell, including right-to-left;
  - empty and loading messages;
  - the first column of list pages is frozen;
  - sorting, resizing, reordering, column chooser, export and print, as before.
- The request editor has tabs for requested items, attachments (name, type, size, date and user) and design
  revisions (all columns).

## Highlights (1.0 feature set)

- Complete job cycle: customer request → design revisions → cost estimate → quotation (versions) →
  job → material, machine and labor → scrap, rework, remnants → quality → delivery → invoice → payment →
  actual cost → estimated vs actual → profitability
- Cost estimates with eleven automatic/manual components, sheet utilisation, remnant search and what-if pricing
- Actual job cost from real transactions only; variance by component with the main driver; job cost sheet PDF/Excel
- Double-entry accounting posted automatically by every operation; immutable posted entries, reversals,
  monthly periods, trial balance, statements, reconciliation
- Moving weighted average inventory, remnant inventory, purchases (orders, receipts, supplier invoices,
  payments, returns), expenses charged to jobs
- 41 reports in seven groups with PDF, Excel, CSV and print; quotation and tax invoice documents
- Arabic (default) and English with right-to-left layout; light, dark and system themes
- Six roles with an editable permission matrix, account lockout, audit trail
- Backup and restore with checksum validation and automatic safety backup; database integrity check
- First-run setup wizard with optional demo company

## Packages

| File | |
|---|---|
| `LaserWorksManagerSetup.exe` | Windows installer (x64) |
| `LaserWorksManager.exe` | Self-contained executable (no .NET install needed) |
| `LaserWorksManager-1.1.0-rc1-win-x64-portable.zip` | Portable package; data stays next to the exe |
| `LaserWorksDemo.lwbak` | Demo company backup — restore via Backup & Restore (users `admin / Admin@2026` etc.) |
| `LaserWorksDemo-database.zip` | The same demo company as a raw SQLite database |
| `SHA256SUMS.txt` | Checksums |

## Known limitations

- **Interactive Windows validation is pending.** On Windows this release was validated by automation only
  (all 61 tests, packaged-exe start-up smoke test, demo reconciliation, silent install/start/uninstall on a
  Windows Server 2025 runner); it has not yet been used by hand on Windows 10/11.
- Upgrading from 1.0.0-rc1 was tested on the rc1 demo database. Back up a production database before the first start
  of 1.1.0-rc1.
- Grid columns have minimum widths but no maximum width.
- The executable and installer are not code-signed, so Windows SmartScreen shows an "unknown publisher" warning.
- Single currency; single company per database; no payroll or fixed-asset register (depreciation is included
  in machine rates and posted as an expense when you record it).
- The utilisation calculator is an estimate (area and grid methods), not a nesting engine.
- Supplier invoices are posted at the goods-receipt cost; price differences must be corrected on the receipt
  or with a journal entry.
- PDFs use QuestPDF under its Community License (free below USD 1M annual revenue); larger companies need a
  QuestPDF commercial licence.
