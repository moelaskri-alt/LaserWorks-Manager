# LaserWorks Manager 1.0.0-rc1 — release notes

First release candidate.

## Highlights

- Complete job cycle: customer request → design revisions → cost estimate → quotation (versions) →
  job → material, machine and labor → scrap, rework, remnants → quality → delivery → invoice → payment →
  actual cost → estimated vs actual → profitability
- Cost estimates with eleven automatic/manual components, sheet utilisation, remnant search and what-if pricing
- Actual job cost from real transactions only; variance by component with the main driver; job cost sheet PDF/Excel
- Double-entry accounting posted automatically by every operation; immutable posted entries, reversals,
  monthly periods, trial balance, statements, reconciliation
- Moving weighted average inventory, remnant inventory, purchases (orders, receipts, supplier invoices,
  payments, returns), expenses charged to jobs
- 40 reports in seven groups with PDF, Excel, CSV and print; quotation and tax invoice documents
- Arabic (default) and English with right-to-left layout; light, dark and system themes
- Six roles with an editable permission matrix, account lockout, audit trail
- Backup and restore with checksum validation and automatic safety backup; database integrity check
- First-run setup wizard with optional demo company

## Packages

| File | |
|---|---|
| `LaserWorksManagerSetup.exe` | Windows installer (x64) |
| `LaserWorksManager.exe` | Self-contained executable (no .NET install needed) |
| `LaserWorksManager-1.0.0-rc1-win-x64-portable.zip` | Portable package; data stays next to the exe |
| `LaserWorksDemo.lwbak` | Demo company backup — restore via Backup & Restore (users `admin / Admin@2026` etc.) |
| `LaserWorksDemo-database.zip` | The same demo company as a raw SQLite database |
| `SHA256SUMS.txt` | Checksums |

## Known limitations

- **Windows runtime validation is pending.** The build, all automated tests and a start-up smoke test of the
  real application ran on Linux; the Windows executable and installer were produced by cross-compilation and
  have not yet been run on Windows. The included GitHub Actions workflow runs the tests, the packaged exe smoke
  test and the installer build on a Windows runner.
- The executable and installer are not code-signed, so Windows SmartScreen shows an "unknown publisher" warning.
- Single currency; single company per database; no payroll or fixed-asset register (depreciation is included
  in machine rates and posted as an expense when you record it).
- The utilisation calculator is an estimate (area and grid methods), not a nesting engine.
- Supplier invoices are posted at the goods-receipt cost; price differences must be corrected on the receipt
  or with a journal entry.
- PDFs use QuestPDF under its Community License (free below USD 1M annual revenue); larger companies need a
  QuestPDF commercial licence.
