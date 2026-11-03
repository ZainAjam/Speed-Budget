<#
.SYNOPSIS
    Builds the Speed Budget installer and the update files.
.DESCRIPTION
    Publishes a self-contained build and packs it with Velopack into artifacts/releases:
      SpeedBudgetApp-win-Setup.exe      the installer to run on a PC (per user, no admin rights, no .NET needed)
      SpeedBudgetApp-<version>-full.nupkg and -delta.nupkg, releases.win.json
                                        the update files; publish this whole folder to the update source
    Keep earlier releases in the output folder when packing a new version: that is what lets Velopack
    build small delta updates. Your data lives in %LocalAppData%\SpeedBudget and is never touched by
    installing, updating or uninstalling (the program itself lives in %LocalAppData%\SpeedBudgetApp).
.PARAMETER Version
    The version to release, such as 0.2.0. Defaults to the Version in SpeedBudget.App.csproj.
.EXAMPLE
    ./scripts/package.ps1
    ./scripts/package.ps1 -Version 0.2.0
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SpeedBudget.App/SpeedBudget.App.csproj'
if (-not $Version) {
    [xml]$csproj = Get-Content $project
    $Version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

$publish = Join-Path $root "artifacts/publish-$Version"
$releases = Join-Path $root 'artifacts/releases'
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

Push-Location $root
try {
    dotnet tool restore | Out-Null

    dotnet publish $project -c Release -r win-x64 -p:Platform=x64 -p:Version=$Version --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    dotnet vpk pack `
        --packId SpeedBudgetApp `
        --packVersion $Version `
        --packDir $publish `
        --mainExe SpeedBudget.App.exe `
        --packTitle 'Speed Budget' `
        --packAuthors 'Speed Budget' `
        --icon (Join-Path $root 'src/SpeedBudget.App/Assets/AppIcon.ico') `
        --runtime win-x64 `
        --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }
}
finally { Pop-Location }

Write-Host ""
Write-Host "Speed Budget $Version is packed in $releases"
Write-Host "  Install:  run SpeedBudgetApp-win-Setup.exe"
Write-Host "  Update:   publish the contents of that folder to your update source, then set the same address under Settings > Updates"
