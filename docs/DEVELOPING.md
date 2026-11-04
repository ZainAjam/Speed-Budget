# Developing Speed Budget

For installing and using the app, see the [README](../README.md). The design notes are in [PLAN.md](PLAN.md).

## Where things live

| Path | What |
|---|---|
| `src/SpeedBudget.Core` | Budget engine: months, balances, holds, reconciliation. No UI, no database. |
| `src/SpeedBudget.Data` | SQLite storage (EF Core), sync-ready change tracking, backups, export, setup templates. |
| `src/SpeedBudget.App` | The WinUI 3 app (unpackaged, self-contained). |
| `tests/` | Engine and data tests. |
| `scripts/` | `package.ps1` builds the installer and update files; `publish.ps1` makes a plain zip; `make-icon.ps1` redraws the app icon. |
| `.github/workflows/` | `ci.yml` builds and tests every push; `release.yml` publishes a release from a tag. |

## Build and run

Needs the .NET 10 SDK and Visual Studio 2022 with the "WinUI application development" workload.

```powershell
dotnet run --project src/SpeedBudget.App -p:Platform=x64
dotnet test SpeedBudget.sln -p:Platform=x64
```

Your data is one SQLite file in `%LocalAppData%\SpeedBudget`. Set the `SPEEDBUDGET_DATA` environment variable to use a
different folder, for example to try things out without touching your real budget. Database changes need an EF
migration (`dotnet ef migrations add ...` in `src/SpeedBudget.Data`); the app applies migrations at startup.

## Developer tools

App Settings has a **Developer** section behind a password (it is in `DeveloperPasswordDialog.cs`; it only keeps the tools
out of everyday use and is not a security measure). It stays open until the app closes or **Lock** is pressed. It has no
tools yet.

## Installer and updates

```powershell
./scripts/package.ps1                 # uses the Version in SpeedBudget.App.csproj
./scripts/package.ps1 -Version 0.9.0  # or name the version
```

This builds a self-contained app and packs it with [Velopack](https://velopack.io) into `artifacts/releases/`:

- `SpeedBudgetApp-win-Setup.exe` installs the app for the current user (no admin rights, no .NET needed).
- The `.nupkg` files and `releases.win.json` are the updates. Keep earlier releases in the folder so updates can be
  small deltas.

The program installs to `%LocalAppData%\SpeedBudgetApp`, separate from the data in `%LocalAppData%\SpeedBudget`, so
installing, updating and uninstalling never touch a budget. The installer is unsigned; a code-signing certificate
(passed to `vpk pack` with `--signParams`) removes the SmartScreen warning.

Run the installer from your own Windows session (double-click it in Explorer). If it is run from a sandboxed tool,
Windows may redirect its files and the shortcuts and icons will point at paths that do not exist for you.

## Releasing on GitHub

Before tagging, add a `## <version>` section to [CHANGELOG.md](../CHANGELOG.md) (for example `## 0.9.2`) describing what
changed in plain language. The packaging script puts that section into the release, and the app shows it in a
**What's changed** dialog when it offers the update, so people can read it before updating. A release with no section
still works, but its dialog says no notes were published.

Releases are built by GitHub Actions. Every push and pull request is built and tested (`ci.yml`). To publish a version:

```powershell
git checkout master
git pull
git tag v0.9
git push origin v0.9
```

Pushing a `v*` tag starts `release.yml`, which runs the tests, packs the installer and update packages (with deltas
against earlier releases), and publishes them as the GitHub Release for that tag. The tag is the version (`v0.9` is
0.9.0), and versions must always go up. While the major version is 0 the release is marked as a **pre-release**. No
token is needed on your PC; the workflow uses GitHub's own.

Installed copies look at this repository (the default under **App Settings > Updates**, set by `DefaultSource` in
`AppUpdates.cs`) when they open and every six hours, and show an **Update and restart** bar when a newer release
exists. The repository has to be public for other people's copies to see releases. Before tagging a release that
changes the database, install the previous release, update it to the new one, and check your data still opens.

## Status

Built: engine, local storage, setup wizard, accounts and items, transactions per account (splits, transfers,
reconcile), the draggable month line, a multi-month budget screen with cash inflow and outflow and holds, pinned items,
backups and export, installer and updates.

Not built yet: sync between devices (the data is already prepared for it), Android apps, and a code-signed installer.
