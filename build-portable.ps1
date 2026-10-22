# Zips publish\ into installer\Output\Ets2Cluster-portable-<version>.zip (unpack anywhere and run Ets2Cluster.exe). Run build.ps1 first.
$ErrorActionPreference = "Stop"
$ver = ([xml](Get-Content "$PSScriptRoot\Ets2Cluster.csproj")).Project.PropertyGroup.Version | Select-Object -First 1
$out = Join-Path $PSScriptRoot "installer\Output"; New-Item -ItemType Directory -Force $out | Out-Null
$zip = Join-Path $out "Ets2Cluster-portable-$ver.zip"; Remove-Item $zip -ErrorAction SilentlyContinue
$stage = Join-Path $env:TEMP "ets2cluster_portable"; Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item "$PSScriptRoot\publish\*" $stage -Recurse
if (Test-Path "$PSScriptRoot\docs\ETS2-Instrument-Cluster-Guide.pdf") { Copy-Item "$PSScriptRoot\docs\ETS2-Instrument-Cluster-Guide.pdf" $stage }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Get-Item $zip | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,1)}}
