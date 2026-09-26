#!/usr/bin/env bash
# Builds the LaserWorks Manager release artifacts into artifacts/release:
#   LaserWorksManager.exe                         self-contained single-file Windows x64 executable
#   LaserWorksManagerSetup.exe                    NSIS installer
#   LaserWorksManager-<ver>-win-x64-portable.zip  portable package (data kept next to the exe)
#   LaserWorksDemo.lwbak / LaserWorksDemo-database.zip   demo company (restorable backup / raw SQLite database)
#   RELEASE_NOTES.md, SHA256SUMS.txt
# Requires: .NET 10 SDK, makensis (NSIS 3), zip, sha256sum. Runs on Linux or Windows (Git Bash).
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
DISPLAY=$(sed -n 's:.*<InformationalVersion>\(.*\)</InformationalVersion>.*:\1:p' Directory.Build.props)
OUT=artifacts/release
PUB=artifacts/publish/win-x64
rm -rf "$OUT" "$PUB" artifacts/demo artifacts/portable
mkdir -p "$OUT" artifacts/demo

echo "== LaserWorks Manager $DISPLAY"
echo "== tests"
if [ "${SKIP_TESTS:-0}" != "1" ]; then
  dotnet test tests/LaserWorks.Tests -c Release --logger "trx;LogFileName=results.trx" --results-directory artifacts/test-results
fi

echo "== publish win-x64"
dotnet publish src/LaserWorks.Desktop -c Release -r win-x64 -o "$PUB"
cp "$PUB/LaserWorksManager.exe" "$OUT/"

echo "== demo company"
dotnet run --project src/LaserWorks.Tools -c Release -- demo artifacts/demo/data
cp artifacts/demo/data/LaserWorksDemo.lwbak artifacts/demo/ 
cp artifacts/demo/LaserWorksDemo.lwbak "$OUT/"
(cd artifacts/demo/data && zip -q -9 "../../../$OUT/LaserWorksDemo-database.zip" laserworks.db)

echo "== installer"
(cd installer && makensis -V2 -DVERSION="$VERSION" -DDISPLAYVERSION="$DISPLAY" -DSOURCE="../$PUB" -DOUTFILE="../$OUT/LaserWorksManagerSetup.exe" LaserWorksManager.nsi)

echo "== portable zip"
P=artifacts/portable/LaserWorksManager
mkdir -p "$P/docs"
cp "$PUB/LaserWorksManager.exe" README.md LICENSE-NOTICES.txt "$P/"
cp docs/*.md "$P/docs/"
cp artifacts/demo/LaserWorksDemo.lwbak "$P/"
echo "Data is stored in the 'data' folder next to LaserWorksManager.exe while this file exists." > "$P/portable.flag"
(cd artifacts/portable && zip -q -r -9 "../../$OUT/LaserWorksManager-$DISPLAY-win-x64-portable.zip" LaserWorksManager)

echo "== notes and checksums"
cp docs/RELEASE_NOTES.md "$OUT/RELEASE_NOTES.md"
(cd "$OUT" && sha256sum LaserWorksManager.exe LaserWorksManagerSetup.exe LaserWorksManager-*-portable.zip LaserWorksDemo.lwbak LaserWorksDemo-database.zip > SHA256SUMS.txt && cat SHA256SUMS.txt)
ls -la "$OUT"
