# Pack all src libraries as 0.1.0 into artifacts/nuget.
# Run from the code directory: .\pack.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "artifacts\nuget"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-ChildItem $out -File -ErrorAction SilentlyContinue | Remove-Item -Force

dotnet pack (Join-Path $root "Zeus.sln") -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed."
}

Write-Host "Packages written to $out"
Get-ChildItem $out -Filter *.nupkg | ForEach-Object { Write-Host ("  " + $_.Name) }
