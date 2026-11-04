# What's changed

Each version's notes are shown to people in the app before they update, so write them for someone who uses Speed Budget,
not for a developer. Add a `## <version>` section here before tagging a release; the packaging script puts that section
into the release.

## 0.9.7

- Transactions are now edited right in the table: click a row and its cells turn into boxes. There is no separate edit panel for transactions any more. A bin at the end of the row deletes it.
- Fixed: choosing the first month could fail with "An error occurred while saving the entity changes".
- **New Transaction** and **New Transfer** now add a new editable row to the table as the latest record, dated today, instead of opening a dialog. Press Enter to save it (or choose another row), Esc to discard it. The counterparty fills in the budget item you last used with it. The old New Transaction and New Transfer dialogs are gone.
- Your budget now has a **start date**, shown as the first "(month) starts here" line. Select it to change the date. Nothing can be dated before it: if you add or edit a transaction dated earlier, you are asked whether to start the budget from that month, and the months in between are added to your budget. The start can move later while no transaction or month line comes before the new date. A month line can't be placed before the start. The "which month is your first transaction in" question is gone.
- Transfers can be edited in the table too. Every field is editable on both transactions and transfers, including the account. For a transfer, the Counterparty box picks the account on the other side; the date, memo and amount are shared by both sides, and the names and budget item follow along.
- The lines between the months on the Budget screen are a little thicker, so it is easier to tell the months apart.
- Every column of the transactions table, including Counterparty, is resized the way columns are in Word. Dragging a heading's right divider moves it between that column and the next one, so the table keeps its width and does not move. Hold Shift while dragging to change only that column and move everything to its right along with it. The last column (with the delete button) takes up whatever room is left.
- A budget-account transaction, or a transfer to a tracked account, that has no budget item now says "Choose a budget item" in its Budget item cell.
- Your edits to a row are saved when you press Enter in a box or click another row. Press Esc, or click anywhere outside the table, to cancel instead: nothing in the table keeps focus and the row goes back to the values it had before you started editing.
- New **Memo** column in the transactions table.
- Open a transaction's budget item drop-down and choose **Split between several items** to share it over several budget items. This works when adding a new transaction too.
- Reconciling has been removed from the Transactions screens (the Reconcile button, the Hide reconciled filter and the padlock). Transactions that were already reconciled can now be edited like any other.
- The panel for a month line slides in over the table from the right edge of the window. Close it with the X in its corner, or press Esc.

## 0.9.6

- The Budget screen no longer shows totals on the budget group rows: only the budget items show figures.
- In the recorded transactions table you can drag the right edge of a column heading to make that column wider or narrower. The sizes are remembered.
- The panel for editing a transaction or month line can be made wider or narrower by dragging its left edge. Its size is remembered too.

## 0.9.5

- The program and your data now live in two clearly named folders: **SpeedBudget-App** (the program) and **SpeedBudget-Data** (your budget and backups). Your existing data is moved across automatically the first time this version opens, and a short README in the data folder explains what it is.
- Because the program's folder has a new name, earlier pre-releases cannot update themselves to this version. **Uninstall the old Speed Budget** (Settings > Apps > Installed apps), then run this version's installer. Your budget is not touched by uninstalling.
- Includes everything from the earlier pre-releases listed below.

## 0.9.2

- The Budget screen now shows every month between your month lines, even the ones you forgot to start. You can budget in them, and nothing is hidden.
- The budget starts on the current month. The setup wizard no longer asks for a starting month.
- The first time you add a transaction to a budget account, Speed Budget asks which month it belongs to.
- Starting a new month: you now choose the month and year, and the name is made for you. Month lines can no longer be renamed.
- You can choose which month the first month is by selecting the first "(month) starts here" line.
- Every month line has a start date you can edit.
- Adding a transaction or transfer on the same day a month starts now asks which month it belongs to.
- Budget screen: press the months at the top to jump to any month, a calendar button takes you back to the current month, and the left arrow at the first month adds an earlier month.
- The transactions table shows the bank date beside the date.
- The left panel has bolder section headings, "Manage" is now "Configurations", "Settings" is now "App Settings", and each account shows its available balance (green above zero, red below).
- The update banner now has a "What's changed" button, so you can read these notes before you update.

## 0.9.1

- New app icon: a wallet with a banknote.
- The window remembers its size and position.
- The Counterparty column in the transactions table no longer disappears in a narrow window.
- A month line sits above its text when the newest transactions are first, and below it when the oldest are first.

## 0.9.0

- The first pre-release of Speed Budget.
