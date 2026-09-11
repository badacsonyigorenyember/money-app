# Publishes the desktop app to dist\, the command docs/using-the-app.md documents.
# dist\ is gitignored. Run: .\publish.ps1
$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'

# `dotnet publish -o` writes into the directory without emptying it first, so a file that is no
# longer produced - or one whose copy failed - is left behind from the previous run and the output
# is a mix of two builds. Publishing on top of a dist\ whose Money.exe is still open fails here,
# with a locked-file message, instead of producing that mix silently.
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish (Join-Path $PSScriptRoot 'src/Money.Desktop') -c Release -o $dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The window is unusable without its stylesheet and htmx, and a publish that drops them still
# exits 0 - so the only thing that catches it is looking.
foreach ($asset in 'wwwroot\app.css', 'wwwroot\lib\htmx.min.js') {
    if (-not (Test-Path (Join-Path $dist $asset))) { throw "Publish is missing $asset" }
}

Write-Host "Published $(Join-Path $dist 'Money.exe')"
