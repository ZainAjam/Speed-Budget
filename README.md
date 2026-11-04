# Speed Budget

A native Windows (WinUI 3, Fluent) budgeting app, replacing the "Action Budget" Google Sheet.
You budget per item each month, record transactions daily, and draw a **"Let's start a new month"** line in the
transaction list wherever your month begins. Everything after the line counts towards the new month. See
[PLAN.md](PLAN.md) for the design.

## Build and run

Needs the .NET 10 SDK and Visual Studio 2022 with the "WinUI application development" workload.

```powershell
dotnet run --project src/SpeedBudget.App -p:Platform=x64
dotnet test SpeedBudget.sln -p:Platform=x64
```

## Installer and updates

```powershell
./scripts/package.ps1                 # uses the Version in SpeedBudget.App.csproj
./scripts/package.ps1 -Version 0.2.0  # or name the version
```

This builds a self-contained app and packs it with [Velopack](https://velopack.io) into `artifacts/releases/`:

- `SpeedBudgetApp-win-Setup.exe` installs Speed Budget for the current user: no admin rights, no .NET needed, with
  Start menu and desktop shortcuts, and an entry under Settings > Apps for uninstalling.
- The `.nupkg` files and `releases.win.json` are the updates. Publish the whole folder to a web address or a shared
  folder (OneDrive works), then enter that address under **Settings > Updates** on each PC. The app checks when it
  opens and offers **Update and restart**; it also takes a backup first. Keep earlier releases in the folder so
  updates can be small deltas.

The program installs to `%LocalAppData%\SpeedBudgetApp`, separate from your data in `%LocalAppData%\SpeedBudget`, so
installing, updating and uninstalling never touch your budget. The installer is unsigned, so SmartScreen may ask you
to confirm the first time (a code-signing certificate fixes that: pass it to `vpk pack` with `--signParams`).

### Releasing on GitHub

The project lives at <https://github.com/ZainAjam/Speed-Budget>, and releases are built by GitHub Actions
(`.github/workflows`). Every push and pull request is built and tested (`ci.yml`). To publish a version:

```powershell
git checkout master
git pull
git tag v0.2.0
git push origin v0.2.0
```

Pushing the tag starts `release.yml`, which runs the tests, packs the installer and update packages (with deltas
against earlier releases), and publishes them as the GitHub Release for that tag. The tag is the version: `v0.2.0`
releases 0.2.0, and versions must always go up. No token is needed on your PC; the workflow uses GitHub's own.

People install by downloading `SpeedBudgetApp-win-Setup.exe` from the Releases page. Installed copies look at the same
repository (the default under **Settings > Updates**) when they open and every six hours, and show an
**Update and restart** bar when a newer release exists. For that to work for other people, the repository (or a
separate releases-only repository, with `DefaultSource` in `AppUpdates.cs` changed to match) must be public.

Install with the Setup.exe from your own Windows session (double-click it in Explorer) so the shortcuts and icons
point at real paths.

Before tagging a release that changes the database, install the previous release, update it to the new one, and check
your data still opens.

`./scripts/publish.ps1` still makes a plain zip with no installer or updates.

## Where things live

| Path | What |
|---|---|
| `src/SpeedBudget.Core` | Budget engine: months, balances, holds, reconciliation. No UI, no database. |
| `src/SpeedBudget.Data` | SQLite storage (EF Core), sync-ready change tracking, backups, export. |
| `src/SpeedBudget.App` | The WinUI 3 app. |
| `tests/` | Engine and data tests. |

Your data is one SQLite file in `%LocalAppData%\SpeedBudget` (set the `SPEEDBUDGET_DATA` environment variable to use a
different folder, for example when testing). A backup is made automatically when the app opens, at most once a day; the
Settings page can also back up on demand and export to CSV or JSON.

## Status

Built: engine, local storage, accounts and items, transactions per account (splits, transfers, reconcile), the draggable
month line, a multi-month budget screen with cash inflow and outflow and holds, pinned items, backups and export.

Not built yet: sync between devices (the data is already prepared for it), Android apps, automatic updates, and
an installer (a signed MSIX needs a code-signing certificate).
