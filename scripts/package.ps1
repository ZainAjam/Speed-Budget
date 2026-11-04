<#
.SYNOPSIS
    Builds the Speed Budget installer and the update files.
.DESCRIPTION
    Publishes a self-contained build and packs it with Velopack into artifacts/releases:
      SpeedBudget-App-win-Setup.exe     the installer to run on a PC (per user, no admin rights, no .NET needed)
      SpeedBudget-App-<version>-full.nupkg and -delta.nupkg, releases.win.json
                                        the update files; publish this whole folder to the update source
    Keep earlier releases in the output folder when packing a new version: that is what lets Velopack
    build small delta updates. Your data lives in %LocalAppData%\SpeedBudget-Data and is never touched by
    installing, updating or uninstalling (the program itself lives in %LocalAppData%\SpeedBudget-App).
.PARAMETER Version
    The version to release, such as 0.2.0. Defaults to the Version in SpeedBudget.App.csproj.
.PARAMETER NotesFile
    A markdown file of "what's changed" notes to put in the release. By default the "## <version>" section of
    CHANGELOG.md is used. The app shows these notes before a person updates.
.EXAMPLE
    ./scripts/package.ps1
    ./scripts/package.ps1 -Version 0.2.0
#>
param(
    [string]$Version,
    [string]$NotesFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SpeedBudget.App/SpeedBudget.App.csproj'
if (-not $Version) {
    [xml]$csproj = Get-Content $project
    $Version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

# The notes people read before updating: the "## <version>" section of CHANGELOG.md, unless a file is given.
if (-not $NotesFile) {
    $changelog = Join-Path $root 'CHANGELOG.md'
    if (Test-Path $changelog) {
        $lines = Get-Content $changelog
        $start = [array]::FindIndex($lines, [Predicate[string]]{ param($l) $l -match "^##\s+\[?$([regex]::Escape($Version))\]?\s*$" })
        if ($start -ge 0) {
            $end = $lines.Count
            for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^##\s') { $end = $i; break } }
            $body = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
            if ($body) {
                $NotesFile = Join-Path $root "artifacts/release-notes-$Version.md"
                New-Item -ItemType Directory -Force (Split-Path $NotesFile) | Out-Null
                Set-Content -Path $NotesFile -Value $body -Encoding utf8
            }
        }
    }
    if (-not $NotesFile) { Write-Warning "No '## $Version' section found in CHANGELOG.md, so this release has no notes." }
}

$publish = Join-Path $root "artifacts/publish-$Version"
$releases = Join-Path $root 'artifacts/releases'
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

Push-Location $root
try {
    dotnet tool restore | Out-Null

    dotnet publish $project -c Release -r win-x64 -p:Platform=x64 -p:Version=$Version --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $notesArgs = @()
    if ($NotesFile) { $notesArgs = @('--releaseNotes', $NotesFile) }

    dotnet vpk pack @notesArgs `
        --packId SpeedBudget-App `
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
Write-Host "  Install:  run SpeedBudget-App-win-Setup.exe"
Write-Host "  Update:   publish the contents of that folder to your update source, then set the same address under App Settings > Updates"
