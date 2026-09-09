# Publishes the desktop app to dist\, the command docs/using-the-app.md documents.
# dist\ is gitignored. Run: .\publish.ps1
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'src/Money.Desktop') -c Release -o (Join-Path $PSScriptRoot 'dist')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published $(Join-Path $PSScriptRoot 'dist\Money.exe')"
