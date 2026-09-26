# Release

## Version

`Directory.Build.props`: `Version` (numeric, used for file and installer versions) and
`InformationalVersion` (display version, e.g. `1.0.0-rc1`).

## Building the artifacts

On Linux (or Windows with Git Bash) with the .NET 10 SDK, NSIS 3 (`makensis`), `zip` and `sha256sum`:

```bash
tools/release.sh            # runs the tests first; SKIP_TESTS=1 to skip
```

Outputs in `artifacts/release`:

| File | How it is made |
|---|---|
| `LaserWorksManager.exe` | `dotnet publish src/LaserWorks.Desktop -c Release -r win-x64` — self-contained, single file, compressed, native libraries and content bundled |
| `LaserWorksManagerSetup.exe` | `installer/LaserWorksManager.nsi` — installs to Program Files, Start-menu and desktop shortcuts, uninstaller, Add/Remove Programs entry; includes docs and the demo backup |
| `LaserWorksManager-<ver>-win-x64-portable.zip` | exe + `portable.flag` + docs + demo backup |
| `LaserWorksDemo.lwbak` | `dotnet run --project src/LaserWorks.Tools -- demo <folder>`: runs the setup and the demo scenario through the real services, checks reconciliation, writes a backup |
| `LaserWorksDemo-database.zip` | the demo SQLite database |
| `RELEASE_NOTES.md`, `SHA256SUMS.txt` | notes and checksums |

The GitHub Actions workflow `.github/workflows/windows-release.yml` builds the same artifacts natively on
Windows, runs the test suite there, smoke-tests the published and the installed executable, and uploads the
artifacts; on a `v*` tag it attaches them to a GitHub release.

## Smoke test switch

`LaserWorksManager.exe --smoke-test` starts the application normally (database creation or migration,
first screen), waits for the window to render, logs the result and exits with code 0 on success or 1 on
failure. Set `LASERWORKS_DATA` to use a throw-away data folder.

## Upgrades

Database migrations are applied automatically at start-up; take a backup before installing a new version.
Uninstalling never deletes `%LOCALAPPDATA%\LaserWorksManager`.

## Signing

The executable and installer are not code-signed. Sign both with `signtool` before distributing widely.
