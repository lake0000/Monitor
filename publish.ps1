$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"

dotnet publish (Join-Path $root "src\DiskGrowthMonitor.App\DiskGrowthMonitor.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o (Join-Path $root "publish\win-x64")

Get-Process DiskGrowthMonitor.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Copy-Item `
    -LiteralPath (Join-Path $root "publish\win-x64\DiskGrowthMonitor.App.exe") `
    -Destination (Join-Path $root "DiskGrowthMonitor.App.exe") `
    -Force

Copy-Item `
    -LiteralPath (Join-Path $root "publish\win-x64\e_sqlite3.dll") `
    -Destination (Join-Path $root "e_sqlite3.dll") `
    -Force

Write-Output "Published root EXE: $(Join-Path $root 'DiskGrowthMonitor.App.exe')"
Write-Output "Published root SQLite native library: $(Join-Path $root 'e_sqlite3.dll')"
