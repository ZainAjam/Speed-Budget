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

## Package for another PC

```powershell
./scripts/publish.ps1
```

This writes a self-contained zip to `artifacts/`. Unzip it and run `SpeedBudget.App.exe`. It is unsigned, so Windows
SmartScreen may ask you to confirm the first time.

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

Built: engine, local storage, accounts and items, transactions (splits, transfers), the draggable month line, the
budget screen with income and holds, pinned items, reconciliation, payments, backups and export.

Not built yet: sync between devices (the data is already prepared for it), Android apps, automatic updates, and
an installer (a signed MSIX needs a code-signing certificate).
