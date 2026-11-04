# Speed Budget

**A fast, simple budgeting app for Windows.** You decide what each month's money is for, type in your transactions as
they happen, and see at a glance what is left on every budget item. Everything stays on your own PC.

> Speed Budget is a pre-release (version 0.9). It works and is in daily use, but expect rough edges, and keep
> backups of anything you care about (the app makes one for you every day).

## Install

1. Open the [Releases page](https://github.com/ZainAjam/Speed-Budget/releases) and download
   **`SpeedBudget-App-win-Setup.exe`** from the newest release.
2. Run it. It installs for your own account only, so it needs no administrator rights and nothing else to be installed.
   Speed Budget then opens, and you get a Start menu and desktop shortcut.
3. **Windows may warn you the first time** ("Windows protected your PC") because the installer is not signed with a paid
   certificate. Choose **More info**, then **Run anyway**.

You need 64-bit Windows 10 (version 1809 or later) or Windows 11.

**Updates are automatic.** When a newer version is released, a bar appears at the top of the app. Press
**What's changed** to read what is new first, then **Update and restart**. The app takes a backup of your data first, and an update never changes your budget.

**To uninstall,** use Settings > Apps > Installed apps in Windows. This removes the program but keeps your data.

## First time you open it

A short setup wizard starts by itself. It helps you create:

- your **bank accounts**, and whether each is a *budget account* (money you budget with, like a cheque or savings
  account) or a *tracked account* (outside your budget, like a retirement fund or a home loan);
- your **budget groups and items**, from a ready-made template (Household, Minimal, or empty) that you can tick and edit;
- the **counterparties** you deal with most, such as shops and your employer.

Nothing is saved until the last step, you can change everything later, and you can run the wizard again from
**App Settings** (it only adds what is missing).

Your budget has a **start date**. It begins as the first day of the current month, and the first "(month) starts here" line
at the bottom of the transactions table is where it sits. Nothing in the budget can be dated before it. If you add or edit a
transaction dated earlier, Speed Budget asks whether to start the budget from that month instead, and adds the months in
between. You can also select that first line and choose a different start date: an earlier one adds the months in between
(what you have already budgeted stays where it is), a later one is allowed while no transaction or month line comes before it.
The date and the month are separate: a budget that starts on 24 October can be the start of November. Select the first
line and choose both the date it starts on and the budget month that the transactions after it belong to.

## How it works

- **Budget items** are the things you set money aside for each month, such as Groceries, Petrol or Electricity. They
  sit in **groups** (Home, Vehicle, ...). Income items, such as Salary, hold the money coming in.
- **Transactions** are what actually happens: money out or money in, on a date, with a **counterparty** (who it was
  with: the shop, the person, your employer) and the budget item it counts against.
- **Months are drawn by you, not by the calendar.** In the transaction list there is a line that says
  "*February 2027 starts here*" (with the solid line on the side of that month's own transactions, so it sits above the
  text when the newest is first and below it when the oldest is first). Everything after the line belongs to that month. If your pay arrives on the 25th, put
  the line there. You can drag the line to move it. If you forget a line, no month is lost: the Budget screen still shows
  every month between your lines, and you can budget for it.
- On the **Budget** screen you give each item an amount for the month. The screen shows what you budgeted, what you
  spent and what is **left**. Money left on an item carries over to the next month on that same item. If you overspend an
  item, the shortfall comes out of the next month's money still to be assigned.

## Everyday use

**Record a transaction.** On a Transactions screen press **New Transaction** (or `Ctrl+N`). A new row appears as the latest
record, dated today, ready to fill in. Pick the counterparty (the app fills in the budget item and account you last used
with it), choose the budget item, and type an **outflow** (money out) or **inflow** (money in). Press `Enter` to save the
row, or `Esc` to throw it away. Typing a new counterparty or budget item adds it on the spot. Tick **Cleared** once the
transaction has shown on your bank statement. If the row is dated on the day a month starts, you are asked which month it
belongs to.

**Move money between accounts.** Press **New Transfer** for a new row in the same way: the Account box is the account the
money leaves (or arrives in, if you type an inflow) and the Counterparty box is the other account. A transfer between a
budget account and a tracked account counts as spending or income against a budget item you choose.

**Edit a transaction in place.** Press the pencil at the end of a row in the recorded transactions table and its cells turn into boxes (clicking a value only highlights the row): change the
date, counterparty, memo, budget item, account, outflow, inflow or bank date right there. This works for transfers too: their Counterparty box picks the account on the other side, and the date, memo and amount change on both sides together. The
Press Enter in a box, or click another row, to save your changes; press Esc, or click outside the table, to cancel them and put the row back as it was. At the end of every row a yellow pencil turns edit mode on for it (while editing it becomes a green tick that saves the row), and the bin deletes the transaction. The table has a **Memo** column for a note on each transaction.

**Split a transaction.** Open a transaction's budget item drop-down and choose **Split between several items**, then give
each item its share; the shares must add up to the transaction's amount. A split transaction shows "Split (2 lines)", and
pressing that opens the split again. You can also split while adding a new transaction. To turn a split back into an ordinary transaction, open its drop-down and choose a single item.

**Side panel.** In App Settings, under **User Interface**, choose **Expanded** (the full left panel), **Expand on hover only** (the default: a strip of icons that opens while the pointer is over it) or **Collapsed** (always the strip of icons).

**Resize the table.** Drag the divider on the right of a column heading in the recorded transactions table: as in Word, the divider moves between that column and the one next to it, so the table keeps its width and its place. If the next column is already as narrow as it can go, the divider carries on into the columns beyond it. Hold **Shift** while dragging to change just that column and move everything to its right along with it (the table scrolls sideways if it gets wider than the window). Your sizes are remembered. Selecting a month line opens a panel over the table from the right edge, for
changing its date; drag its left edge to resize it, and close it with the X or Esc.

**Bank date.** The table shows each transaction's date and, beside it, the date it showed on your bank statement. Entering a
bank date on a transaction also marks it as cleared.

**Start a new month.** When your month rolls over, press **Start a new month** (or `Ctrl+M`), give the month a name and a
start date, and a line appears in the list. Drag it up or down to fine-tune, select it and use `Alt+Up` / `Alt+Down`, or select it and change the date it starts on. If you add a transaction or transfer on the very day a month starts, the app asks which month it belongs to, because that day could be either.

**Plan your budget.** Open **Plan your spending** (under the Budget heading in the left panel). Click an amount to edit it: `Enter` saves and leaves the cell, `Esc` cancels, the arrow
keys move between cells, and you can type sums such as `1200+350`. Months ahead are shown as planned, so you can fill
them in early. Use the two buttons at the top right to see one month or several. Press the months at the top to pick a month and
year and jump straight to it, and when the current month is not the leftmost one, a small calendar button appears next to the arrows
to bring you back to it.

**Hold income for next month.** Right-click an income item and choose **Hold for next month** to keep part of
this month's pay for next month, so you budget with money you already have.

**Watch the items that matter.** The strip at the top of the Transactions screens can show up to five budget items
with what is left on each. Press **+** to add one, click one to jump to it on the Budget screen, and right-click one to
remove it.

**Balances at a glance.** Under Transactions in the left panel, each account shows its available balance: green when it is above
zero, red when below, and plain when zero.

**Tidy up.** The **Configurations** section lists Accounts, Counterparties and Budget Items. Right-click a budget item or a
counterparty anywhere to edit it, drag items to reorder them, and pick an icon for each item.

## Keyboard shortcuts

| Keys | What it does |
|---|---|
| `Ctrl+1` ... `Ctrl+5` | Go to Budget, Transactions, Accounts, Counterparties, Budget Items |
| `Ctrl+N` | New transaction |
| `Ctrl+M` | Start a new month |
| `Alt+Up` / `Alt+Down` | Move the selected month line |
| `Enter` / `Esc` / arrows | Save, cancel, and move between cells when editing the budget |

## Your data, backups and settings

Everything is kept in a single file on your PC, in `%LocalAppData%\SpeedBudget-Data` (the program itself is in `SpeedBudget-App` beside it). There is no account and nothing is sent
anywhere. The **App Settings** screen (bottom left) lets you:

- **Back up now.** A backup is also made automatically each day the app opens, and the newest 14 are kept.
  To restore one, close the app and copy it over `budget.db` in the data folder.
- **Export** your transactions to CSV (opens in Excel or Google Sheets) or everything to JSON.
- **Run the setup wizard** again to add more accounts, items or counterparties.
- **Reset the app** to a clean slate. This deletes everything (a backup is saved first), and the setup wizard runs again.
- **Check for updates**, or point the app at a different update source.

## What is not there yet

Syncing a budget between several devices, and phone and tablet apps, are planned but not built. For now the budget lives
on the one PC.

## Questions and problems

Please [open an issue](https://github.com/ZainAjam/Speed-Budget/issues) and say what you did and what you expected.

## Licence

Copyright (c) 2026 Zain Ajam. All rights reserved. You are welcome to download and use the app for your own
budgeting; the source code may not be copied or reused without permission. See [LICENSE](LICENSE).

## For developers

Building from source, running the tests and publishing releases are covered in
[docs/DEVELOPING.md](docs/DEVELOPING.md). The original design notes are in [docs/PLAN.md](docs/PLAN.md).
