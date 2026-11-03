<#
.SYNOPSIS
    Builds a self-contained Speed Budget for Windows and zips it, ready to copy to another PC.
.DESCRIPTION
    The result needs no .NET or Windows App SDK installed on the target PC. Unzip it anywhere and run
    SpeedBudget.App.exe. Data is stored per user in %LocalAppData%\SpeedBudget.
.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -Platform arm64
#>
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SpeedBudget.App/SpeedBudget.App.csproj'
[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$name = "SpeedBudget-$version-win-$Platform"
$output = Join-Path $root "artifacts/$name"
$zip = Join-Path $root "artifacts/$name.zip"

Remove-Item $output, $zip -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $project -c Release -r "win-$Platform" -p:Platform=$Platform --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Published $name ($size MB): $zip"
