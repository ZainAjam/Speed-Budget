# What's changed

Each version's notes are shown to people in the app before they update, so write them for someone who uses Speed Budget,
not for a developer. Add a `## <version>` section here before tagging a release; the packaging script puts that section
into the release.

## 0.9.20

- **Month lines now say which budget month starts and when.** Each month line in the transactions table reads, for example, "**November 2026 budget month starts here** on 24 October 2026", with the first part in bold. The first line shows the date the budget starts on.
- **The Budget screen always opens on the month you are recording transactions for**: the latest month that has been started (after the last month line), not simply today's calendar month. The button that jumps back to the first month now takes you to that month too.
- **Right-click a transaction that is being edited** and the menu now offers **Save** and **Delete** (instead of Edit and Delete), so you can save it without reaching for the tick.
- **Moving or covering budget (right-click a Left figure on the Budget screen):** the "Move it to" and "Take it from" boxes are now ordinary drop-downs with the search inside the list. Beside each item the list shows what is left on it, in green when there is money left.
- **The move, allocate and cover dialogs now lead with the figure**: how much is available to budget, left on the item, or short is the first thing in the box, on a line of its own in bold (green for money there is, red for a shortfall), with what to do underneath.

## 0.9.19

- **Even table lines at every zoom.** Every line between rows and between columns in the transactions table is now one screen pixel wide and the same brightness as at 100%, at every zoom level (before, some looked thicker or bolder than others).
- **The "Recorded transactions" title is gone** from the top of the transaction tables; New Transaction, New Transfer and Start a new month now sit where it was.
- **The Month line panel now runs all the way down to the bottom of the window**, as it does beside the Expected transactions bar.
- **Zoom now only affects the tables.** Zooming in or out changes the size of the budget table and the transactions table only. Everything else stays at normal size: page titles, buttons, balances, the pinned-items strip, the left panel and the Month line and Expected transactions panels. On the Budget screen, zooming out also makes room for more months side by side.
- **Fixed: the Month line and Expected transactions panels were see-through at some zoom levels below 100%** (for example 95%, 85% and 75%), letting the table show through them. They are now solid at every zoom.

## 0.9.18

- **See what makes up a figure.** Press a value in the **Received** or **Spent** column on the Budget screen and All Bank Accounts opens showing only the transactions that make up that figure (that budget item, in that month). The item and month are shown beside **Clear filters**. Filters, including this one, only last until you leave that screen: coming back to the transactions always starts with nothing filtered.
- **All Bank Accounts.** The first entry under Transactions in the left panel now shows the transactions of every account together, with an Account column telling them apart.
- **Filter the recorded transactions like in Excel.** Every column heading in the transactions table now has a small funnel. Press it to see the values in that column, tick the ones you want to keep (or search for them), and press OK. A filtered column's funnel turns gold, and a **Clear filters** button appears beside **Hide cleared**. Filters on several columns work together, and they reset when you leave the page. While a filter is on, the month lines are hidden. The small question mark beside each column title is gone: hover the title itself to read what the column is.

## 0.9.17

- **Zoom now affects only the page, not the left panel.** The left panel keeps the same size and look at every zoom level, so its items always line up under their headings and it opens and closes the same way. The budget and the bank account transactions are what zoom in and out.

## 0.9.16

- **Zoom.** Press Ctrl and + or Ctrl and - (or hold Ctrl and turn the mouse wheel) to make the whole app smaller or larger, from 50% to 200%, and Ctrl and 0 to go back to 100%. Zoom out to fit more on screen when Speed Budget is snapped beside a banking website. The zoom sits in the footer of the left panel, above Keyboard Shortcuts, on every screen: with the panel open it shows − and + buttons with the current percentage between them (click the percentage to type any value from 50 to 200), and with the panel narrow it is just a magnifying glass with a plus, which opens the same buttons when clicked. The zoom is remembered the next time the app opens. Pop-up boxes stay at normal size.
- **Keyboard Shortcuts moved into App Settings**: a **Show keyboard shortcuts** button there opens the full list in a pop-up. The left panel now ends with the zoom, a line, and App Settings. The zoom controls are only in the left panel (and Ctrl with + or −), no longer in App Settings.

## 0.9.15

- **Counterparties are now edited and deleted right in the Counterparty drop-down** of the transaction table: each one has a pencil (rename, change its icon) and a bin. A counterparty that transactions still use can't be deleted, and you are told how many use it. The Counterparties page under Configurations is gone, along with its Ctrl+3 shortcut.
- **Backups are tidier, and you can restore one.** The automatic backup runs once a day and only the newest 31 are kept, so there are never more than 31 automatic backup files. Backups you make yourself with Back up now are never deleted. File names now say how they were made (`budget-auto-…` or `budget-manual-…`). **Restore a backup…** in App Settings lists every backup with its date, time and method; pick one and the app restarts on it. Your current data is saved as a manual backup first, so a restore can itself be undone.
- **Fix for the app closing while you moved over the left panel.** The crash log showed the panel refusing to open from inside a pointer movement ("Unspecified error"), which closed the whole app. The panel is now opened and closed a moment after the movement, and a refusal is ignored instead of closing the app.
- The app now keeps track of what went wrong: `activity.log` in the data folder holds the last things done, and `crash.log` records each failure with those last things. If the app stops without writing anything, the next start asks Windows what happened and records it.
- The + buttons on the budget screen are gone. Right-click **Cash Outflow** (or **Cash Inflow**) to add a budget group, and right-click a budget group to add a budget item to it. Income groups are shown in the Cash Inflow table with a heading, just like the expense groups: you can fold them, right-click them (add an item, rename, delete) and drag them into a new order.
- **Budget items and accounts are now managed where you use them, so the Accounts and Budget Items pages under Configurations are gone.**
  - On the budget screen, right-click a budget item or a budget group to delete it (an item that already has budget or transactions is archived instead, with its history kept; deleting a group deletes its items too). Budget groups can now be dragged into a new order, as items already could.
  - In the left panel, **Add bank account** (at the bottom of the Transactions list) asks for a new account's name, type, whether it counts toward the budget, and its opening balance.
  - Right-click an account in the left panel for **Edit account** (every detail) and then **Delete account** (an account with no transactions, with a confirmation), **Close account** (an account that has transactions: it stays in the list, dimmed, with its history) or **Reopen account** (a closed one). The left panel now stays open while this menu is showing.
- Switching a transaction table between Newest first and Oldest first now shows the end that is latest straight away: the top of the table for newest first, the bottom for oldest first, with no visible scrolling.
- The two buttons at the top right of Plan your spending (one month or several months) are now the same size.

## 0.9.14

- You no longer need to type the amount before splitting a new transaction. Choose Split between several items on an empty new row and the split box opens straight away: pick whether it is money out or in, enter the lines, and the transaction's total is worked out from them. (If you have already typed an amount, the lines still have to add up to it, as before.)
- The "Budget account" and "Tracking account" tags at the top of an account page now look the same (a plain outlined label, no longer a filled blue one that looked like a button); only their words differ, and the words are blue for a budget account and yellow for a tracking account.
- Esc now works at any time in the transaction table, wherever the focus is and even with a drop-down open: it deletes a new transaction, and puts a transaction being edited back to the values it had before and closes it.
- A transaction you are editing now stays in edit mode until you press Enter or the green check (which save it) or Esc (which drops the changes). Clicking another row or anywhere else on the page no longer saves it or takes it out of edit mode; while a row is open, starting to edit another one asks you to finish the first.
- Every column heading in the transaction table now has a small question mark beside it; hover over it for a short explanation of what the column means. The Cleared column is a little wider to make room.
- Adding or changing a transaction no longer redraws the whole table (which made it flash and fade back in): the table stays on screen, only the rows that changed are added, removed or updated, and the table no longer fades or slides rows in and out.
- Click a split transaction's "Split (n lines)" in the Budget item column and a pop-up shows the budget items it is made of, with their amounts and the total.
- A transaction table sorted oldest first now opens already at the bottom, instead of opening at the top and scrolling down to it.
- **Plan your spending, refinements.**
  - Budget cells no longer show a tooltip when you hover over them.
  - Clearing a budget cell leaves it blank while you edit it; leaving the cell (Enter, or the arrow keys) counts the blank as 0.00.
  - Scroll down and the column titles stay in view: Received and Available now while you are in the Cash Inflow rows, Budgeted, Spent and Left once you are in the Cash Outflow rows.
  - The button for showing several months at once now shows two pages stacked.
  - New **+** buttons: beside Cash Inflow to add a budget item, beside Cash Outflow to add a budget group, and beside each group to add a budget item to it. A new group with no items stays on the page so you can add to it.
  - Right-click a budget group to rename it, or a budget item to rename it or change its icon (the budget item menu no longer has Edit Budget Item).
  - The monthly summary at the top is now open whenever you are at the top of the page, and folds away by itself once you scroll down, leaving Available to budget under the month headings. Scroll back to the top and it opens again. If you fold the summary yourself with its arrow it stays folded, and scrolling never opens it, until you open it yourself.
  - The amount in a budget cell now stays against the right-hand edge while you edit it, instead of moving towards the middle.

## 0.9.13

- When a transaction table is sorted oldest first, it now opens scrolled to the bottom, so the last transaction you added is in view and you can add the next one straight away.

## 0.9.12

- Transfers between two accounts that are both in your budget (or both outside it) now show "Not Applicable" under Budget item, since they don't use a budget item.
- Once you press **Update and restart**, the button (and the What's changed link) go away while the update downloads, so it can't be started twice.
- When the figures above **Available to budget** are hidden, Available to budget now stays in view under the month headings while you scroll.
- Budget amount boxes no longer show an X while you edit them.
- When you move or allocate budget, the budget item list shows just the item names (no group) and you can type to search it.
- Clicking a budget amount now puts the cursor at the end of it instead of highlighting it, so you can add to it. Moving into a cell with the arrow keys or Tab still highlights it, so typing replaces it.
- Sums in budget amounts now work when the amount has spaces in it, such as `1 250 + 350`.
- **Undo and redo.** Ctrl+Z undoes your last change to anything you can edit, on any screen: a budgeted amount, a transaction you added, edited or deleted, a budget move, an expected transaction, a counterparty, a budget item or an account. Ctrl+Y (or Ctrl+Shift+Z) puts it back. While you are typing in a box, Ctrl+Z still undoes just the typing. The history lasts until you close the app.

## 0.9.11

- A new app icon: two gold coins, the front one marked R, which reads clearly as money at any size.
- **Expected transactions.** Every account page has an "Expected transactions" bar along the bottom. It opens a flyout where you note down what you expect but has not happened yet (who, memo, budget item, amount). Nothing in it changes a balance or your budget. When one happens, press the move icon on its line, give the date, and it moves into the Recorded transactions table on that date. Clicking outside the flyout closes it.
- **Moving budget around.** Right-click **Available to budget**: when it is green you can allocate some of it to any budget item; when it is red you can cover the shortfall from items that have money left. Right-click a **Left** amount: when green, move some of it to another item; when red, cover the shortfall from another item that has money left.
- Right-click a transaction row for **Edit** and **Delete**. The old "Edit Counterparty" right-click menu is gone.
- A pencil beside each counterparty in the counterparty drop-down edits its name, and a pencil beside each budget item in the budget item drop-down edits the item (name and icon) and its budget group.
- The budget now looks 12 months ahead instead of 3.
- Pressing Enter in a budget amount no longer leaves a white border around the budget table.
- All keyboard shortcut tooltips are gone. Every shortcut is listed under the new **Keyboard Shortcuts** item above App Settings in the left panel.
- App Settings, About, has a **Change log** link that shows every version's notes in a pop-up.

## 0.9.10

- The budget item drop-down in the transactions table now lists just the items (with their icons), without their budget group.

## 0.9.9

- In the left panel, the first section is now called **Budget** and its page **Plan your spending**; **Budget** and **Transactions** are both bold blue headings; and **Configurations** (Accounts, Counterparties, Budget Items) now sit at the bottom, above App Settings with a line between them.
- On the Budget screen, the arrow beside "Available to budget" points down while the figures are hidden and up while they show.
- Every row of the transactions table now has a yellow pencil and a bin at its end. The pencil turns edit mode on for that row; while it is being edited the pencil is a green tick that saves the row.
- A transaction or transfer can be saved without a budget item.
- The New Transaction, New Transfer and Start a new month buttons now sit right after the "Recorded transactions" title.
- A row of the transactions table is edited only after you press its pencil: clicking a value now just highlights the row.
- In money boxes you can type either a point or a comma as the decimal mark, anywhere in the app: "1.5" and "1,5" both mean one and a half. Sums such as 3.5+1.5 work too.
- Scrolling the mouse wheel over a budget amount no longer changes it. The page scrolls instead.
- On the Budget screen, pressing Enter in an amount saves it and leaves the cell; Esc puts the old amount back and leaves it; the Up and Down arrows move between amounts.
- The monthly figures at the top of the Budget screen start collapsed, with only **Available to budget** showing. Press the arrow beside it to show or hide the rest; the app remembers your choice.
- In the left panel, **Transactions** is now just a heading for the accounts under it. It can't be pressed and opens nothing.
- In a transaction or transfer, pressing Enter in a field saves the whole record and takes it out of edit mode (new or existing). Pressing Esc drops every change to it, or the new record altogether, and takes it out of edit mode.
- A transaction or transfer no longer needs an outflow or an inflow: both can be empty.
- On a transactions screen, the balances now sit in the middle of the top line, between the account name and the account type, in the order Cleared, Uncleared, Available. The New Transaction, New Transfer and Start a new month buttons sit in the middle of the Recorded transactions line. This leaves more room for the table.
- The strip of watched budget items can now hold up to five items instead of three.

## 0.9.8

- The button that hid the left panel is gone. In App Settings, under **User Interface**, **Side panel** now has three choices: **Expanded** (the full panel), **Expand on hover only** (the default: a narrow strip of icons that opens while the pointer is over it) and **Collapsed** (always the narrow strip of icons). App Settings also has a line between each of its sections.
- In the transactions table, the Budget item cell now shows just the item (with its icon), without its budget group. The group still shows in the drop-down list.
- A split transaction's Budget item box says "Split (n lines)". Choose a single item from its drop-down to turn it back into an ordinary transaction, or choose "Split between several items…" to change its lines.
- In the left panel, every icon stays in exactly the same place whether the panel is a narrow strip or open, so clicks are predictable. To make that possible, your accounts are now listed directly under Transactions (no longer tucked inside it), and each account's icon is in the strip too.

## 0.9.7

- Transactions are now edited right in the table: click a row and its cells turn into boxes. There is no separate edit panel for transactions any more. A bin at the end of the row deletes it.
- Fixed: choosing the first month could fail with "An error occurred while saving the entity changes".
- **New Transaction** and **New Transfer** now add a new editable row to the table as the latest record, dated today, instead of opening a dialog. Press Enter to save it (or choose another row), Esc to discard it. The counterparty fills in the budget item you last used with it. The old New Transaction and New Transfer dialogs are gone.
- Your budget now has a **start date**, shown as the first "(month) starts here" line. Select it to change the date. Nothing can be dated before it: if you add or edit a transaction dated earlier, you are asked whether to start the budget from that month, and the months in between are added to your budget. The start can move later while no transaction or month line comes before the new date. A month line can't be placed before the start. The "which month is your first transaction in" question is gone.
- Transfers can be edited in the table too. Every field is editable on both transactions and transfers, including the account. For a transfer, the Counterparty box picks the account on the other side; the date, memo and amount are shared by both sides, and the names and budget item follow along.
- The lines between the months on the Budget screen are a little thicker, so it is easier to tell the months apart.
- The first "(month) starts here" line has a budget month as well as a start date: a budget can start on 24 October and be the start of November. Select the line and choose both.
- If the app ever closes unexpectedly, what went wrong is now written to a file called crash.log in the data folder, so it can be looked into.
- A column divider no longer stops when the column next to it is as narrow as it can go: it carries on into the columns beyond, so you can keep dragging it.
- The column dividers in the transactions table are easier to grab: you can press on either side of the line, not only to its left.
- A tracked account's page no longer has a "Start a new month" button, since tracked accounts are outside the budget.
- On the Budget screen, a budget group's heading row has its own tinted background, so groups are easier to tell apart from the items under them.
- Each account's transactions screen has a label in its top right corner saying whether it is a **Budget account** or a **Tracking account**.
- The transactions table's columns now start at a size that fits their usual contents (the Cleared and delete columns are no longer wide). The table fills the full width of the window, and the Memo column takes whatever room the other columns do not need. Column sizes you saved with an earlier version are reset once.
- The "Tracking account" label is now an outlined label with normal text, easier to read than white on grey.
- In the left panel, budget accounts have a bank icon and tracking accounts have an eye icon, so you can tell them apart at a glance.
- Accounts under Transactions in the left panel can be dragged into a new order. Budget accounts are listed first, then tracking accounts, and you can reorder within each kind.
- The recorded transactions table now starts with the oldest transactions first. You can still switch to newest first, and it remembers your choice while the app is open.
- The "Activity" and "Configurations" headings in the left panel are bold and blue.
- The "All accounts" entry under Transactions in the left panel is gone: each account has its own page.
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
