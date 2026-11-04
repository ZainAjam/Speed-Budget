# What's changed

Each version's notes are shown to people in the app before they update, so write them for someone who uses Speed Budget,
not for a developer. Add a `## <version>` section here before tagging a release; the packaging script puts that section
into the release.

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
