# Installs the instrument cluster for the current user (no administrator rights): copies the exe to
# %LOCALAPPDATA%\Programs\ETS2 Instrument Cluster and adds a Start Menu shortcut.
# -NoPrompt skips the questions; -Autostart also starts it when you log in to Windows.
param([switch]$NoPrompt, [switch]$Autostart)
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "publish"
if (-not (Test-Path "$src\Ets2Cluster.exe")) { $src = $PSScriptRoot }
if (-not (Test-Path "$src\Ets2Cluster.exe")) { throw "Ets2Cluster.exe not found: run build.ps1 first (or unpack the portable zip and run this script from it)." }
$dir = Join-Path $env:LOCALAPPDATA "Programs\ETS2 Instrument Cluster"
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item "$src\*" $dir -Recurse -Force
$target = Join-Path $dir "Ets2Cluster.exe"
$sh = New-Object -ComObject WScript.Shell
function Shortcut($path) { $s = $sh.CreateShortcut($path); $s.TargetPath = $target; $s.WorkingDirectory = $dir; $s.Description = "ETS2 Instrument Cluster"; $s.Save() }
Shortcut (Join-Path ([Environment]::GetFolderPath("Programs")) "ETS2 Instrument Cluster.lnk")
Write-Host "Installed to $dir (Start Menu: ETS2 Instrument Cluster)"
if (-not $NoPrompt -and -not $Autostart) { if ((Read-Host "Also start it automatically when you log in to Windows? (y/n)") -eq "y") { $Autostart = $true } }
if ($Autostart) { Shortcut (Join-Path ([Environment]::GetFolderPath("Startup")) "ETS2 Instrument Cluster.lnk"); Write-Host "Autostart added." }
if (-not $NoPrompt) { if ((Read-Host "Start it now? (y/n)") -eq "y") { Start-Process $target } }
