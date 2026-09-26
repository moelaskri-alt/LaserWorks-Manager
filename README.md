# LaserWorks Manager

Desktop software for CO2-laser custom-job workshops. It follows every job from the customer's
request to the money actually made on it:

**request → design → cost estimate → quotation → approval → job → material, machine and labor →
production → scrap and rework → quality → delivery → invoice → accounting → actual cost → profitability**

The question it is built to answer is: *how much did this custom job actually cost, how much did
we sell it for, and how much profit did we really make?*

- Arabic (default) and English, switchable at runtime, full right-to-left layout; light, dark and system themes
- Real double-entry accounting: every operational transaction posts a balanced, traceable journal entry; posted entries are never edited, only reversed
- Moving weighted average inventory, sheet utilisation, reusable remnants, machine hourly cost
- Multi-component jobs: unlimited lines of raw materials, purchased components, consumables, packaging, remnants, external services and other direct costs — from inventory, remnants, direct purchase or manual cost, with no double charging; product templates for repeat products
- Estimates with fifteen cost components (each automatic or manual), what-if pricing, estimated-vs-actual variance per component line, profitability flags
- 41 reports with PDF, Excel, CSV and print; quotation, invoice and job cost sheet documents
- Six roles with per-module permissions, PBKDF2 passwords, account lockout, audit trail
- Backup and restore with checksum validation and an automatic safety backup
- Nothing is hard-coded to one workshop: materials, machines, employees, rates, numbering and taxes are all configured in the app

## Install

| Package | Use |
|---|---|
| `LaserWorksManagerSetup.exe` | Installer for Windows 10/11 x64. Start-menu and desktop shortcuts, uninstaller. Data is kept in `%LOCALAPPDATA%\LaserWorksManager`. |
| `LaserWorksManager-<version>-win-x64-portable.zip` | Unzip anywhere and run `LaserWorksManager.exe`. The `portable.flag` file makes it keep its data in the `data` folder next to the exe (USB stick friendly). |
| `LaserWorksDemo.lwbak` | A complete demo company. Restore it from **Backup & Restore → Restore from file…** |

The executable is self-contained: no .NET installation is needed.

On first start a setup wizard asks for the company, currency and tax, fiscal year, warehouses,
machines, materials, users and opening balances, and can load demo data instead.
Demo users: `admin / Admin@2026`, `manager / Manager@2026`, `accountant / Account@2026`,
`sales / Sales@2026`, `production / Prod@2026`, `store / Store@2026`.

## Documentation

| | |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Setup, materials, machines, requests, quotations, jobs, production, scrap, remnants, backup |
| [Costing](docs/COSTING.md) | Machine hourly cost, sheet utilisation, estimates, actual job cost, variance, profitability |
| [Accounting](docs/ACCOUNTING.md) | Chart of accounts, posting rules for every transaction, periods, reversals, reconciliation |
| [Architecture](docs/ARCHITECTURE.md) | Projects, layers, data model, integrity rules |
| [Testing](docs/TESTING.md) | Test suites, how to run them, latest results |
| [Release](docs/RELEASE.md) | Building the exe, installer, portable zip and demo database |

## Build from source

Requires the .NET 10 SDK.

```bash
dotnet build LaserWorksManager.slnx
dotnet test tests/LaserWorks.Tests                  # unit, integration, headless UI
dotnet run --project src/LaserWorks.Desktop         # start the app
tools/release.sh                                    # exe, installer, portable zip, demo, checksums (needs makensis)
```

## Status

Version 1.1.0-rc1 (multi-component jobs). All 61 automated tests pass on Linux and on Windows.
Automated Windows validation **passed** on a Windows Server 2025 runner (GitHub Actions run
[36222354588](https://github.com/moelaskri-alt/LaserWorks-Manager/actions/runs/36222354588), commit 28a735f):
61/61 tests (including headless UI), the published `LaserWorksManager.exe --smoke-test`, the demo company's 15
reconciliation checks, and a silent install → start → uninstall of `LaserWorksManagerSetup.exe`.
**Interactive Windows validation is pending**: nobody has yet used this build by hand on Windows 10/11 (file dialogs,
printing, high-DPI, SmartScreen).

See [FINAL_QA_REPORT.md](FINAL_QA_REPORT.md) and [docs/TESTING.md](docs/TESTING.md).
