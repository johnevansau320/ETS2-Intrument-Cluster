# Builds the self-contained app into publish\ (Ets2Cluster.exe and the .NET runtime files beside it: no .NET install needed on the target PC).
# Plain files, not a single self-extracting exe, which security programs trust more. Needs the .NET 8 SDK.
Remove-Item "$PSScriptRoot\publish" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$PSScriptRoot\Ets2Cluster.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$PSScriptRoot\publish"
Write-Host "Built: $PSScriptRoot\publish\Ets2Cluster.exe"
