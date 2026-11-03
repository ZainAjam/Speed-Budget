# Speed Budget — Build Plan

> **Status (3 Nov 2026):** M0–M7 and most of M9 are built (see [README.md](README.md)). **M8 (sync) is deliberately
> skipped for now**; the data model is sync-ready. Not built: sync, Android, automatic updates, a signed installer,
> the Payments page (dropped), and the Actual Budget ideas (rules, scheduled transactions, budget templates, per-item rollover, statement import,
> end-to-end encryption, undo/redo, reports, quick-add hotkey, reconcile locking).

A native Windows 11 (Fluent) app that replaces the "Action Budget" Google Sheet. Android phone, tablet and desktop apps come later. All devices, including several Windows PCs at once, will share one budget through sync.

The budget starts fresh with 2027 data. There is no import of earlier history.

---

## 1. What the sheet does today (and what changes)

| Sheet feature | In Speed Budget |
|---|---|
| Budget items in groups, with sub-items (Nazreen's FNB → ○ Groceries…), account numbers and notes | Groups → Items → optional Sub-items. Drag to reorder. |
| 12 Budgeted / Outflows / Balance column blocks per year, reset every December | One month at a time, with a month switcher. Months run on indefinitely, so there's no yearly reset. |
| Summary: Income, Overflow, Budgeted, Not budgeted / Overbudgeted, Available to Budget | Summary card, with the maths in §4. |
| Transactions listed in the order they were entered; month set by position | **Sorted by date** (ascending or descending). The draggable **"Let's start a new month"** line sets which month each transaction belongs to (§3.2). |
| "INCOME FOR THIS MONTH" / "INCOME FOR NEXT MONTH" items | Replaced by **Income lines** in the budget, with **Hold for next month** (§4). |
| One bank account (the sheet is named "Easy Account FNB") | **Several accounts.** Each is either a **Budget account** or a **Tracked account** (§3.1). |
| 2-line / 3-line splits | Any number of split lines. |
| Balance Watcher | Pinned items shown as a strip on every page. |
| Bank balances (statement, uncleared, available for spending) | A Reconcile panel for each account. |
| Budget Report (EFT payments to make) | A "Payments to make" checklist. |

---

## 2. Architecture

### 2.1 Offline-first with sync from day one
Each device keeps a full local copy of the data in **SQLite**. The app reads and writes that local copy, so it is fast and works offline. A **sync service** in the cloud exchanges changes between devices.

```
 Windows PC #1 ─┐                           ┌─ Android phone (later)
 Windows PC #2 ─┼── HTTPS (push/pull) ──► Sync API ◄──┼─ Android tablet (later)
 Windows PC #n ─┘     + live "something changed"     └─ Android desktop (later)
                      notification
                         │
                     PostgreSQL
```

- **Only source data is synced:** accounts, groups, items, allocations, income holds, transactions, split lines and new-month markers. Derived values (which month a row is in, balances, Available to Budget) are **worked out on each device** from that data. Because the calculation is deterministic, every device shows the same numbers.
- **Change tracking:** every synced row has:
  - a `Guid` id (UUIDv7), so ids can be created offline
  - `UpdatedAt`, using a hybrid logical clock
  - `UpdatedBy` (the device id)
  - `IsDeleted` (deleted rows are kept as tombstones so the deletion syncs)
- **Push:** the device sends rows it changed since its last push.
- **Pull:** the server returns rows changed after a cursor. The cursor is the server's own increasing change number.
- **Conflicts:** last writer wins, field by field, using the hybrid clock. For a personal budget that is predictable and enough. Two edits to *different* fields of the same transaction both survive.
- **Live updates:** the server sends a lightweight "pull now" signal (SignalR / WebSocket) when another device pushes, so open PCs refresh within seconds.
- **Sign-in:** an account on the sync service (Microsoft or Google sign-in through OpenID Connect). One sign-in links a device to the shared budget.

### 2.2 Technology

| Concern | Choice |
|---|---|
| Windows UI | **WinUI 3** (Windows App SDK), Mica, NavigationView, light/dark |
| Windows language | **C# / .NET 10** (LTS), CommunityToolkit.Mvvm |
| Local storage | SQLite through EF Core |
| Sync API | **ASP.NET Core (.NET 10)** with PostgreSQL and SignalR. Shares the domain library with the Windows app. |
| Hosting | Azure App Service + Azure Database for PostgreSQL, or a small VPS/container host (open question 1) |
| Android (later) | Kotlin + Jetpack Compose (Material 3, adaptive layouts for phone, tablet and desktop), Room (SQLite), same sync protocol |
| Money | Integer cents everywhere (DB, API, both clients) |
| Tests | xUnit, plus **shared golden test files** (§4.3) |

**Keeping Windows and Android consistent:** the budget maths will exist twice, once in C# and once in Kotlin. To keep them in agreement:
1. A written engine spec (§4).
2. A folder of **golden test files**: JSON inputs and their expected outputs. Both test suites run against the same files.
3. A versioned sync API contract (OpenAPI).

### 2.3 Solution layout
```
SpeedBudget.sln
├─ src/SpeedBudget.Core/        # domain model + budget engine (no UI, no DB)
├─ src/SpeedBudget.Data/        # EF Core local DB, change tracking, migrations
├─ src/SpeedBudget.Sync/        # sync client (push/pull, cursor, live signal)
├─ src/SpeedBudget.App/         # WinUI 3 app
├─ server/SpeedBudget.Api/      # ASP.NET Core sync service
├─ spec/golden/                 # shared JSON test cases (also used by Android later)
├─ spec/sync-api.yaml           # OpenAPI contract
└─ tests/…
```

---

## 3. Data model

```
Budget        Id, Name, Currency (ZAR)
Account       Id, BudgetId, Name, Institution?, AccountRef?, Type (Cheque|Savings|Credit|Cash|Investment|Loan),
              IsOnBudget (Budget account vs Tracked account), OpeningBalance, OpeningDate, IsClosed, SortOrder
Group         Id, BudgetId, Name, Kind (Income | Expense), SortOrder
BudgetItem    Id, GroupId, ParentItemId?, Name, AccountRef?, Notes?, PaymentMethod?, ReportHeading?,
              IsPinned, IsArchived, SortOrder
MonthMarker   Id, BudgetId, Date, DayOrder, Label ("February 2027")       ← the "Let's start a new month" line
Allocation    MonthId*, BudgetItemId, Amount              (expense item: budgeted; income item: expected)
IncomeHold    MonthId*, IncomeItemId, Amount              (part of this month's income held for next month)
Transaction   Id, AccountId, TransDate, DayOrder, BankDate?, Payee, Memo, Amount (+in / −out),
              IsCleared, TransferPairId?
SplitLine     Id, TransactionId, BudgetItemId?, Amount, Memo?
Payee         Name, LastBudgetItemId, LastAccountId       (autocomplete)
```
\* A month is identified by the marker that starts it. The first month is the start of the budget.

### 3.1 Budget accounts vs tracked accounts
- **Budget account:** all of its income and spending affects the budget. Every transaction must be assigned to an income or expense item.
- **Tracked account:** its transactions and balance are recorded but **do not affect the budget**. Assigning an item is optional, for reporting only. The new-month line is not shown on tracked accounts.
- **Transfers** between two accounts are a linked pair of transactions (`TransferPairId`):
  - **budget → budget:** no effect on the budget. Money only moved between accounts.
  - **budget → tracked** (for example, to a savings pocket): counts as spending from the budget. It is assigned to an expense item such as "FNB Savings Pocket".
  - **tracked → budget:** counts as income in the budget. It is assigned to an income item.
- An account can be switched between budget and tracked later. A confirmation shows how this changes past months.

### 3.2 Date sorting and the draggable new-month line
- Transactions are sorted by `(TransDate, DayOrder)`. Descending view shows the same order reversed. `DayOrder` is the order within one day; you can change it by dragging a row within the same date.
- The **"Let's start a new month" line is a row in the same sorted list**, with its own `(Date, DayOrder)`. A budget-account transaction belongs to the month whose line comes before it. It is "after the line" in time, whichever direction the list is shown in.
- **Dragging the line** between two rows gives it the date and an in-between `DayOrder` of the spot where it lands. Every row after it becomes part of the new month, and all totals update straight away.
- The line is **one per budget, not per account**. Because it is a point in time, it appears in the same place in every budget account and in the combined "All budget accounts" view.
- **Edge cases:**
  - A new transaction dated on the same day as a line is placed at the end of that day, so it lands in the new month. Dragging fixes it if needed.
  - Changing a transaction's date can move it across a line. The row briefly highlights to show it changed month.
  - A line can't be dragged past the next or previous line.
  - Deleting a line merges its month into the one before, after a confirmation showing what moves.
- Sorting uses the **transaction date**. Bank date is used only for clearing and reconciling.
- Sync: a line move is a single-row change to `Date` and `DayOrder`, so it syncs cleanly.

---

## 4. Budget engine (SpeedBudget.Core)

### 4.1 Income lines and Hold for next month
- The budget has an **Income** group with income items (for example, "FNB Investment", "Other income"). Each one shows **Expected** (optional, typed in), **Received** (from transactions) and **Held for next month**.
- **Hold:** a "Hold for next month" field on each income line, plus a "Hold all" button. A held amount isn't available to budget this month. It is added to next month's Available to Budget as **"Released from last month"**. The amount held can't be more than what was received.

### 4.2 Formulas (for month m, budget accounts only)
```
Received(m, inc)   = Σ split lines on income item inc in month m
Income(m)          = Σ Received(m, inc)
HeldOut(m)         = Σ IncomeHold(m, inc)
HeldIn(m)          = HeldOut(m−1)
Item.Budgeted(m)   = Allocation(m, item)
Item.Activity(m)   = Σ split lines on expense item in m          (−out / +in)
Item.Remaining(m)  = Budgeted + Activity                           ← "what is left"
Overflow(m)        = Σ Item.Remaining(m−1)                         (leftovers go back to the pool, as in the sheet)
CarriedOver(m)     = Available(m−1)                                 (unbudgeted or overbudgeted amount from last month)
Available(m)       = Income(m) − HeldOut(m) + HeldIn(m) + Overflow(m) + CarriedOver(m) − Σ Item.Budgeted(m)
```
- The engine is a set of pure functions over an in-memory snapshot, recalculated in full after every local edit or sync pull. With a few thousand transactions a year this is instant.
- Per-item rollover of leftovers (for sinking funds) can be a later setting. The default matches the sheet.

### 4.3 Golden tests
Each file in `spec/golden/*.json` contains accounts, items, transactions, markers, allocations and holds, plus the expected per-month totals. Cases cover:
- a line moved across a busy day
- a transaction's date edited across a line
- held income being released
- transfers between budget and tracked accounts
- overbudgeting
- splits

---

## 5. Screens (Fluent)

1. **Budget**
   - Month switcher (‹ February 2027 ›).
   - Summary card: Income · Held for next month · Released from last month · Overflow · Budgeted · **Available to Budget**.
   - **Income** section: Expected / Received / Hold.
   - **Expense** groups: Budgeted (edit inline) / Spent / Remaining, with progress bars.
   - Actions: "Copy last month's budget", "Budget = last month's spending".
2. **Transactions**
   - An account picker: All budget accounts, each account, and tracked accounts.
   - A date-sorted virtual list with an ascending/descending toggle.
   - Quick-add row: Tab moves between fields, Enter saves. Payee autocomplete suggests the item and account used last time. Amount accepts arithmetic (`=120+35.5`).
   - The **new-month line** is an accent divider labelled "February 2027 starts here", with subtotals. Drag it with the mouse, touch or keyboard (Alt+↑/↓). Ctrl+M adds a new line.
   - Splits expand inline. Filters never hide split lines.
   - The watcher strip sits above the list.
3. **Accounts:** add or edit accounts and set **Budget / Tracked**. Shows the balance of each account.
4. **Reconcile** (per account): statement balance, uncleared total, available to spend.
5. **Payments:** a checklist of this month's EFT and transfer items.
6. **Manage Items:** groups (Income / Expense), items, sub-items, payment method, report heading, pinned status.
7. **Settings:** sync sign-in and status (last synced, pending changes), devices, currency, local backup/export (CSV, JSON).

**New-month flow:** add or drag the line → the app suggests a label (next calendar month, editable) → optionally **copy last month's allocations and expected income** → the Budget page opens on the new month.

**First run (fresh 2027 start):**
1. Create the budget.
2. Add accounts and mark each as Budget or Tracked, with opening balances.
3. A guided setup wizard (first run, or Settings) offers a template (Household, Minimal or empty), then lets the user tick accounts, budget groups and items, and common counterparties. It only adds what is missing, so it can be run again.
4. Set up the first month.

---

## 6. Milestones

| # | Milestone | Done when |
|---|---|---|
| M0 | **Setup:** .NET 10 SDK and the VS WinUI workload, solution skeleton, `git init` | An empty WinUI app with Mica and NavigationView launches |
| M1 | **Core engine + golden tests:** markers and month assignment, accounts on/off budget, transfers, income holds, the maths in §4 | All golden cases pass |
| M2 | **Local data, ready for sync:** EF Core schema with Guid ids, hybrid-clock `UpdatedAt`, tombstones, local change log | Data saves and loads, and changes are recorded for push |
| M3 | **Accounts + Manage Items + first-run setup** | A 2027 budget can be set up from scratch |
| M4 | **Transactions:** date-sorted list, fast entry, autocomplete, transfers, cleared/bank date | A day of entries takes seconds |
| M5 | **New-month line:** divider, drag to move, add/delete/merge, copy allocations | Dragging moves rows between months and the numbers stay correct |
| M6 | **Budget screen:** income lines with Hold, expense items, summary card | Numbers match the engine and the golden tests |
| M7 | **Splits, watcher strip, Reconcile, Payments** | Feature parity with the sheet |
| M8 | **Sync service:** ASP.NET Core + Postgres + SignalR, sign-in, push/pull, conflict rules, deployed | Two PCs editing the same budget agree within seconds, including edits made offline |
| M9 | **Polish + packaging:** shortcuts, backups and export, icon, MSIX/installer, auto-update | Installable on every PC |
| Later | **Android** (Kotlin/Compose): phone, tablet and desktop layouts, Room, same sync API, golden tests ported | — |

Sync is built at M8, but the data model supports it from M2, so nothing has to be reworked. M1 is the foundation. Once M2 is done, M3, M4 and M6 can be built side by side.

---

## 7. Open questions

1. **Hosting for the sync service:** Azure (managed, simplest to run, but costs more), or a cheap VPS/container host that you manage yourself? I'll check current prices before we choose.
2. **Sign-in provider:** Microsoft account, Google account, or both?
3. **Sharing:** just you, or should other people (for example, Nazreen) be able to sign in to the same budget? This affects permissions on the sync service.
4. **Same-day placement:** is "a new transaction on the line's date goes into the new month" the right default, or should it go into the earlier month?
5. **Tracked-account categories:** should tracked accounts be able to use budget items just for reports, or have no categories at all?
