# What's changed

Each version's notes are shown to people in the app before they update, so write them for someone who uses Speed Budget,
not for a developer. Add a `## <version>` section here before tagging a release; the packaging script puts that section
into the release.

## 0.9.42

- **The right-click menu says "line".** On the transaction tables the menu now reads Edit line, Duplicate line and Delete line (the balance tables too).

- **Room under the last row.** With the oldest first, scrolling to the bottom of a transaction table now stops with the last row in the middle of the screen, with room under it, instead of at the very bottom edge. A new row added at the end opens there too, away from the edge of the app.

- **The eye can limit the table to a recent period.** Under Hide cleared and Hide reconciled, and a divider, it offers Show all transactions, the last week, the last month or the last quarter, the same as on the balance tables.

- **A month line goes when everything under it is hidden.** If the eye hides all the transactions of a month (cleared or reconciled ones, or ones older than the period chosen), its "budget month starts here" line is hidden too.

## 0.9.41

- **The totals under the column headings are gone.** Received, Available now, Budgeted, Spent and Left no longer show a total underneath; the headings and the section titles are back as they were before 0.9.40.

## 0.9.40

- **Totals under the column headings.** Each month now shows the total directly under Received and Available now (added up from the Cash Received rows) and under Budgeted, Spent and Left (added up from the Cash Spend Planning rows). They update as you change a budget.

- **Section titles sit closer to their tables.** Cash Received and Cash Spend Planning now sit directly above the top of their tables, with less empty space between.

## 0.9.39

- **The budget summary is in time order.** It now reads from last month into this month: carried over, released from last month, left over from last month, overspent last month, then income this month, held for next month, budgeted, and available to budget.

- **Each summary line has an information icon.** Point at it, or click it, to read what the figure means.

- **You can hold money for next month in any month.** Before, only money from that month's own income could be held, so a month whose money was released, carried over or left over from the month before could not hold any. Now the whole of Available to budget can be held.

## 0.9.38

- **Money left on a budget item now goes back into the pool.** When a month ends, what is left on each budget item returns to "Available to budget" in the new month (a new "Left over from last month" line in the summary shows it), instead of staying on the item. Overspending already reduced the next month's pool, and still does. This applies to your existing months too, so the available figures of later months can change.

- **Keep a leftover in its budget item.** Right-click the Left figure of an item that has money left and choose "Keep in this budget item for next month". A small arrow after the figure shows it was carried forward in the same item, and the same menu lets you send it back to the pool.

## 0.9.37

- **Each budget month shows how it is doing.** Next to the month's title: a green circle with a tick when nothing is overspent or overbudgeted, a red circle with an exclamation mark when something is, and a yellow triangle with an exclamation mark when a coming month is overbudgeted. Hover over it for the reason.

## 0.9.36

- **Copy last month's budget works again.** The "…" menu at the top of each month fills in the items that have no budget yet from the month before.

- **Reset budget.** The same "…" menu has a new Reset budget, which sets every item of that month back to nothing.

- **Right-click a budget cell.** The menu has Cut, Copy and Paste as three labelled icons at the top, then a divider, then "Use last month's budget" and "Use last month's spending as the budget" for just that item.

- **Budget cells select the whole amount.** Clicking into a budget cell highlights its value, so you can type over it straight away, instead of putting the cursor at the end.

- **New budget items line up.** An item added from a transactions page now shows up on the Budget page with its budget cells in every month, instead of leaving the rows misaligned.

- **Side panel is back to one list.** The "Budget accounts" and "Tracking accounts" headings are gone; all accounts sit under Transactions as before (the account icons stay).

- **The eye button says what it will do.** It now offers "Show cleared transactions" or "Hide cleared transactions" (and the same for reconciled) depending on what is showing.

- **Duplicate on balance-tracking accounts.** Right-click a line to start a new one with the same amounts and comment.

- **Updates are checked while the app stays open.** A copy left running for days now looks for a new version every few hours; before, the timer could stop working after a while.

## 0.9.35

- **Multi-select now tells you what it is.** While a table is in multi-select mode, a banner appears between the buttons saying so, that you can edit or delete several transactions at a time, and to press X to close multi-select mode.

- **Multi-select puts the other buttons away.** While a table is in multi-select mode, the sort, eye and overflow buttons are hidden, and the edit, delete and close buttons sit at the right edge of the table. They come back when you close multi-select.

- **Multi-select opens and closes instantly.** Pressing the multi-select button shows the edit, delete and close buttons straight away, and pressing X puts the multi-select button straight back, with no sliding or folding.

- **Tracking accounts have a radar icon, and the accounts are in two sections.** Under Transactions, the side panel now lists your accounts under "Budget accounts" and "Tracking accounts". Tracking accounts use a new radar icon in the same colour as the other icons.

- **Adding a bank account is a + beside All Bank Accounts.** The "Add bank account" entry at the bottom of the side panel is gone; there is now a + at the right end of the All Bank Accounts row instead.

- **The account tag on a transaction page shows the account's icon.** The "Budget account" or "Tracking account" tag in the top right of a transaction page now carries the same icon the side panel uses for that kind of account.

## 0.9.34

- **The side panel is now a separate piece drawn over the page.** Choosing an account or page in the panel no longer makes it close and open again: it stays open until the pointer leaves it. The page behind keeps drawing while the panel opens and closes, so nothing waits on the panel or jumps.

- **The side panel is frosted and easy to tell from the page.** It now has a frosted-glass background in a slightly different shade from the rest of the app, with a thin line down its edge, so you can see where it ends. The icons in the collapsed panel are no longer cut off.

- **The side panel slides smoothly even while a page is loading.** Opening and closing the panel is now drawn by Windows on its own, so it no longer stutters while the screen is busy building a page.

- **The side panel no longer shuts when you choose something in it.** With the panel set to open on hover, clicking an account or page now keeps the panel open; it closes only when the pointer leaves it. The page also opens straight away again, and the panel's section titles ("Budget", "Transactions") no longer stay behind, cut off, once the panel has collapsed.

- **The side panel opens reliably when you hover over it.** Moving the pointer quickly in and out of the collapsed panel could leave it stuck part-way open, or not opening at all. The panel now takes its open and close requests one at a time, and puts itself right if it is ever left half-open.

- **The side panel shuts completely before the page you chose opens.** With the panel set to open on hover, choosing an item (or an account) now closes the panel and waits until its closing animation has finished, and only then builds the page, so the panel no longer stutters while the screen is busy. If the panel is held open for any reason, the page opens after a moment anyway.

- **Find the difference: the date fix now works, and tells you when it cannot.** "Use the file's date" did nothing on a transaction that was already reconciled with an earlier file (those are locked). It is now "Set its bank date to …": it sets only the bank's own date, which is allowed on a reconciled transaction too, and leaves the transaction's own date as you entered it. The balance check also counts a cleared transaction from its bank date, as the bank does, so a transaction entered on the 31st that reached the bank on the 3rd is no longer seen as a difference. Un-clear and Delete on a reconciled transaction now say it is locked instead of silently doing nothing.

## 0.9.33

- **The import wizard asks where the file's balances and date are.** Besides the balance after each line, the setup step has **Cleared balance**, **Available balance** and **Balances are as at**. For each you choose where in the file to read it: the last line of a column, one cell (a column and a row), or the last figure in a row (for a line above the transactions such as "Balance, 2114.88, 2093.88"); the date can be the date of the latest line or a cell. The wizard looks for cells labelled like Closing balance, Available balance and Statement date, and shows what each place reads as, so you can see at once if it is right. The places are remembered for the account. After the import the file's available balance is compared with the account's available balance (cleared and uncleared transactions together) on the file's date, and that decides the green tick or the yellow warning, because the bank's available balance takes account of what is still pending. The cleared balances are compared too and told alongside: if they differ but the available balances agree, the result is still green and the message explains it. Without an available balance in the file, the cleared balance decides. If the cleared balance differs, Find the difference works even when the file has no balance on each line.

- **Cleared and uncleared transactions are both reconciled by confirming them.** The **Already in the table** tab now lists every transaction that matches a line of the file, cleared or not, and says which it is. Confirming a pair (**Same transaction: reconcile it**) marks the transaction reconciled, and a reconciled transaction is always cleared as well. Lines added from the file are added cleared and reconciled, and the message after the import says how many were reconciled. On the **Not in the table** tab each line has a **Budget item** drop-down, the same one as in the transaction table, so a new transaction can get its budget item straight away.

- **The result of the balance check speaks only about the file's date.** It compares the figures on that date and says nothing about later transactions or today's balance.

- **The import wizard's setup step scrolls, and has a Preview.** The step now scrolls when the window is too small for all of it. Below a divider line, a **Preview** heading introduces what will be read from the file with the current settings: how many transactions, from when to when, the money in and out, and the first lines as they will be read (date, counterparty and amount).

- **A multi-select button above the table.** It sits before the sort button. Pressing it opens it leftwards into three buttons: a pencil (edit the ticked transactions together), a bin (delete them, after asking) and a cross (close multi-select). The pencil and the bin wait until something is ticked. Pressing the cross folds the three back into the multi-select button; leaving multi-select any other way (Esc, the right-click menu, or after pressing the pencil) does the same.

- **A table's page shows at once, with a loading ring while the rows load.** Choosing a bank account in the side panel now brings up its page straight away, with everything in place except the table itself: the account's name and badge, the three balances (or the balance figures of a balance account), the headings and the buttons. Only the table shows a turning ring with "Loading transactions…" over the table. The transactions fill in when they are ready and the ring fades away. This is for the transaction tables and for the balance-account tables.

- **Tracking accounts have their own icon in the side panel.** An activity monitor now stands for an account that is only being tracked (budget accounts keep the payment card), as the eye is now used for the button that hides transactions.

- **An eye button on the balance-account screen chooses how far back to show.** It sits after the sort button and offers Show all transactions, Show the last week, Show the last month and Show the last quarter (counted back from today); it is coloured while the table is cut short. The Report view follows the same stretch, and the sort button stays in the Report view too, putting the latest month first or last. The top figures still cover every line. The buttons are also spaced on purpose now: sort and eye together, a wider gap, then Transactions and Report together, and the same wider gap before the three-dot menu.

- **Blank lines in a CSV file keep their place**, so the row numbers you choose in the import wizard are the line numbers in the file, and a line with no amount (such as an interest rate note) is simply left out.

## 0.9.32

- **Import and Export replace "Import from Excel" and "Export to Excel".** Export still saves an Excel workbook. **Import** now reads Excel workbooks, CSV and text files, and zip files that hold them (a zip with several files lets you choose which one).
- **An import wizard maps the file to the table, then reconciles it.** The wizard guesses which column is the date, the counterparty and the money (one amount column, or separate money out and money in columns), the date layout, and the heading rows to skip; correct anything it got wrong, and it remembers the layout for the account next time. The second step has two tabs. **Already in the table** shows each transaction that is in the table on the left and the matching line of the import file on the right, so you can check it is really the same one: tick the pair and the transaction is marked reconciled, and cleared (it gets the bank's date), untick it and the line moves to the other tab. **Not in the table** lists the lines of the file the table does not have, and you tick the ones to add as new, cleared transactions. Transactions already reconciled with an earlier file are left out of this tab, as they need nothing. Press Apply when you are happy; Each tab has **Select all** and **Deselect all**. The Counterparty column can be left as (none): lines are then imported with a blank counterparty, and on the **Not in the table** tab each line has a counterparty box where you can pick one from your list, type a new one, or leave it blank. The reconciliation opens as large as the window. On the **Already in the table** tab, each pair is a card with the transaction table's version on the left and the import file's on the right, each in its own labelled panel. Where a value (date, counterparty, memo or amount) differs, press the version to keep: it is outlined and ticked, and the other is dimmed; where the table has nothing (no date yet, no counterparty or memo) the file's value is chosen for you. **Not the same** rejects a pair and moves the line to the **Not in the table** tab, where **Back to match** puts it back; the memo of a line to be added can be edited there. On the **Not in the table** tab the counterparty box is the same drop-down as in the transaction table, with its search box inside. The first step has a **Remember this setup and skip this step next time** tick: once ticked for an account, importing a file that fits the remembered setup goes straight to the reconciliation, where **Edit import settings** takes you back to change it. nothing changes before that, and Ctrl+Z takes the whole import back. An expected transaction with no date is matched too, and takes the bank's date. A workbook exported from Speed Budget is still imported as it is.
- **After an import, the account is checked against the bank.** When the file has a balance column (the setup step finds it; you can change it), the balance on its latest line is compared with the account's cleared balance in Speed Budget on that date. If they agree, a big green tick draws itself with the message that everything reconciles; if not, a yellow warning triangle bounces in with a message saying the account does not reconcile and by how much it is out. Apply stays available when everything in the file is already cleared, so the check can be run on its own.
- **When an import does not reconcile, Speed Budget helps you find out why.** The yellow warning now has a **Find the difference** button. It compares the file's running balance with the account at the end of each day, then lists what differs, each with a fix: the account and the file already differing before the file starts (change the opening balance), the same transaction dated differently (use the file's date), a cleared transaction the file does not have (un-clear it, or delete it), and a line of the file the account does not have (add it). It checks again after every fix, and the green tick plays when everything reconciles. As a last resort, **Add a balancing transaction** adds one cleared transaction for exactly what is left over.
- **Transactions that agree with the bank's file show a green tick.** When an import matches a transaction in the table with a line of the file, whether it is cleared by the import or was already cleared, the transaction is marked reconciled, and its cleared tick is replaced by a small round green icon with a white tick. Already-cleared transactions that match are marked too, even when there is nothing else to apply.
- **An eye and a sort button replace the Hide cleared box and the sort drop-down.** The eye opens a small menu with two switches, **Hide cleared transactions** and **Hide reconciled transactions**; it is coloured while anything is hidden. The sort button (on the balance-account screen too) switches between showing the newest first and the oldest first; its tip says which way the table runs now.
- **Importing a file you exported does not create duplicates.** When an import (an Excel file exported from Speed Budget, or one of your own in that layout) has a line that is already in the table (same account, date, counterparty, memo and amount), it is not added again, and the summary says how many were left out. Two identical transactions in the table count as two. A line with a blank counterparty can now be imported too.

## 0.9.31

- **Add Entry is now a + button** on the screen of an account kept by its balances.

- **Hide cleared also hides a month line whose transactions are all cleared.** With **Hide cleared** on, a month with nothing left under its line (every one of its transactions is cleared) no longer shows its line either. A month with no transactions at all keeps its line.

- **Several rows can be edited at once.** Press the pencil on as many rows as you like and they all stay open; each has its own tick to save it, and Enter in a row saves just that row (Esc drops just that row). In multi-select mode, right-click and choose **Edit** (or **Edit N transactions**), just like Delete, to open all the ticked rows together. Right-clicking a row that is being edited offers Save, Discard changes and, with several open, Save all and Discard all.
- **One "+" button adds things.** The three buttons above the table are now a single **+** button: pressing the **+** adds a new transaction, and its arrow offers New transaction, New transfer, a divider line and New month.

- **Pages open much faster, so the side panel no longer stalls while one loads.** A table row used to build every one of its editing boxes (date pickers, counterparty and budget item pickers, number boxes) even though only one row is ever being edited; they are now built only for the row being edited. Opening a table is several times quicker (for example 1.9 s down to 0.05 s to settle on one test account), which is what was freezing the side panel's animation.
- **The side panel reacts when the pointer comes back in at the window edge.** When the pointer left the window by its edge and returned at the same place, the window sometimes got no pointer event at all, so the panel stayed shut (or stayed open while the pointer was outside). The panel now also checks where the pointer is many times a second, so it follows the pointer however it moves.

- **The side panel opens and shuts promptly.** In Expand on hover only mode it now opens within a moment of the pointer reaching the icon strip (a request made while it was still moving used to be lost, and is now made as soon as it stops, or tried again). Choosing an item, an account included, shuts the panel straight away and opens the page after it, instead of waiting for the page to build first; it stays shut until the pointer has left the strip and come back. The wide, empty panel that showed while it shrank is gone: it now goes straight back to the strip of icons.

- **The Expected transactions panel is gone: expected transactions now live in the transaction table.** A transaction recorded with a **future date** is a *scheduled* transaction, and one recorded with **no date and no bank date** (press the small x in the date box while editing to clear it) is an *expected* transaction with no date yet. Both are greyed out and in italics, and neither changes any account balance or budget figure. A scheduled one becomes an ordinary transaction by itself when its date arrives; an undated one becomes ordinary once it is given a date or a bank date (a bank date also clears it, and the transaction takes that date). Anything left in the old Expected list moves into the table the first time this version opens: dated ones become scheduled transactions (a repeating one, its next date) and ones without a date become undated. In Excel, an undated transaction has an empty Date, and a row with an empty Date is imported as one. Transactions dated in the future no longer count in the budget until their date arrives.

- **A tidier budget item drop-down.** "(no item)" is gone from the list. It now reads: Split between several items, then a **Most used budget items** heading with your three most used items (each with a star), and then every budget item under a heading for its group. The headings are bold and in the app's accent colour. Typing in the search box lists just the matches, still under their groups. Enter on an empty search no longer picks anything.

- **The app now fits its window by itself, down to half of a small screen.** Instead of running off the side, each screen gives up what it can do without as the window narrows, and only then scales down:
  - **Transaction tables:** the columns narrow first; then the Bank date column goes, the Memo becomes a small icon (hover for the text, click to read or change it in a small box), the Account column goes, and Cleared shrinks to a tick. Nothing needs scrolling sideways.
  - **Budget:** one whole month (all its columns) always shows. The names column gives up room first, and its titles shrink to fit. Zoomed in, months narrow a little before one is taken off the screen.
  - **Balance accounts:** the columns narrow; then the Comment becomes an icon (click to read or change it), and Total changes and Daily changes go. The Report table narrows too.
  - **Expected transactions:** in a narrow window the boxes are stacked in rows and each expected item is shown on two lines, at a readable size.
  - **Page margins and the side panel:** margins are slim in a narrow window, and an Expanded side panel stays a strip of icons while the window is narrow. Zoom still works as a refinement: zooming in simply moves a screen to its more compact layout sooner.

## 0.9.30

- **Budget sections renamed.** "Cash Inflow" is now **Cash Received** and "Cash Outflow" is now **Cash Spend Planning**.
- **Jump to the latest.** On a long table (the transactions tables and a balance account's table), a round button floats at the bottom centre when the latest line is out of view. Its arrow points up or down, and pressing it jumps to the end where the latest line is, whichever way the table is sorted.
- **Balance account: icons for its two views.** The Daily balances and By month buttons are now icons: **Transactions** (the balance on each date) and **Report** (the month by month summary).
- **The budget keeps its months when you zoom in.** Zoomed in, the months now give up some of their spare width (down to a minimum) before one of them is taken off the screen.
- **Expected transactions fit a narrow window.** Their headings, boxes and list now shrink, as far as it takes, so a whole row fits when the window is about half the screen wide.

- **The three most used budget items are at the top of the budget item drop-down.** In the transaction tables, the items you use most (counted over the transactions of your budget accounts) are listed first, marked with a small star, and the rest stay in their groups below them. The list updates as you add transactions.

## 0.9.29

- **Opening balances count as income of the first month.** The opening balance of each budget account (not tracking accounts) is now part of "Income this month" for the first month of the budget, so it shows in "Available to budget" and carries on into the months after, ready to be given to budget items. Change an account's opening balance and the first month follows.

- **A new line is brought into view and ready to type in.** Adding a transaction, a transfer, a duplicate or a balance entry now scrolls the table to the new line (even in a long list) and puts the cursor in its first box: the counterparty, the other account of a transfer, or the balance.
- **An account kept by its balances can be sorted.** A **Show newest first / Show oldest first** box sits above its table, as on the transactions tables. A new entry goes where the latest lines are: at the top when newest is first, at the bottom when oldest is first.

## 0.9.28

- **The title, totals and buttons of a balance account shrink in place as the window narrows.** Instead of dropping under the title, the account name and the totals at the top (Original balance, Balance now and the gains, withdrawals and net figures) now get smaller together, and so do the buttons below them (Add Entry, Daily balances, By month and the ⋯ menu), as the table does.

## 0.9.27

- **A balance account's screen no longer empties when the window is made narrow.** The title used to be squeezed until it wrapped one letter per line and pushed the whole page out of view. Now the totals drop under the title when there is no room beside it (and scroll sideways if still too wide), and the table is made smaller to fit instead of being cut off.
- **Switching quickly between accounts is steadier.** A single click opens the account at once; when you click through several quickly, the app opens only the one you stop on instead of building every screen you pass through, which was making it close.

## 0.9.26

- **Accounts in the side panel can be dragged into any order.** Drag an account under Transactions to where you want it, budget or tracking accounts alike; the side panel stays open while you drag and after you let go (it closes when the pointer moves away). All Bank Accounts always stays first and Add bank account always stays last. Until you drag one, budget accounts are listed first and tracking accounts second.
- **A clearer icon for budget accounts.** Accounts that count toward the budget now show a payment card in the side panel instead of a bank building (tracking accounts keep the eye).
- **The app no longer closes when the side panel hiccups.** Now and then Windows refused to slide the side panel open or shut just as a page was opened, and the app closed with an "Unspecified error". That error is now noted in the log and ignored, and the app carries on.
- **Dragging accounts is steadier.** The side panel no longer closes when you start dragging an account, and the app no longer closes when you drop one: the list is rebuilt a moment after the drop instead of during it.
- **Switching the side panel to Collapsed no longer risks closing the app.** The panel is now closed first and then turned into the narrow strip of icons, and nothing can hold it open at that moment.

## 0.9.25

- **A tracking account can now be kept by its balances.** When you add or edit a tracking account (one that does not count toward the budget), choose how you keep track of it: **Record each transaction**, like any other account, or **Record its balance**, which suits a retirement fund. A balance account has its own screen: type in the balance on a date (usually every day), anything you withdrew that day, and a comment if you like, and Speed Budget works out the daily change, the total change since the first balance, and the gains and losses (the change in the balance with your withdrawal put back), with the totals at the top. **By month** adds up the gains and losses and the withdrawals for each month and shows the money left to reinvest. The balance beside the account's name is its latest balance.

- **Balances can be brought in from Excel, and every table can be exported.** On the screen of an account kept by its balances, the ⋯ menu above the table offers **Import from Excel** (a file with the headings Date, Balance, Withdrawal and Comment in its first row; an export from Speed Budget has them) and **Export to Excel**. You are shown how many lines are new, how many are for dates the account already has (they take the figures in the file, so the same file can be imported again safely) and which lines will be skipped and why, before anything is brought in. Importing can be undone with Ctrl+Z.
- **A line of a balance account is added and changed in place.** **Add Entry** (top left) puts a new line at the top of the list with its cells open, ready for the balance; pressing a line's pencil does the same for an existing line. A tick (or Enter) saves it and a cross (or Esc) leaves it as it was, and the table stays where it is. What each heading means is in its tooltip, so there are no explanation notes on the screen. **By month**, **Daily balances** and the ⋯ menu sit on the right.
- **Every transactions table can select several rows, duplicate a row, and import from or export to Excel.** Right-click a row and choose **Multi-select** to put the table into multi-select mode: tick boxes appear at the left (the box in the heading ticks them all, or none). In that mode the right-click menu offers only **Delete** (the ticked rows; one Ctrl+Z brings them back) and **Exit multi-select**. Otherwise right-clicking a row offers **Edit**, **Duplicate**, **Delete** and **Multi-select**. **Duplicate** adds a copy dated today as the latest entry, with everything else the same (account, counterparty, memo, budget item or split, and amount), ready to change and save; accounts kept by their balances offer Edit, Delete and Multi-select only. The ⋯ menu at the top right above every table has **Import from Excel** (with a preview) and **Export to Excel**.
- **New Month, and month lines in Excel.** The "Start a new month" button is now **New Month**. **New Month** lines go into Export (the first month at the top, the others where they stand) and are read back by Import: a row with New Month as its Counterparty and the month in the Memo (for example December 2026) starts that month at that place in the list. The account of a balance account shows its **Original balance** (the first line's balance) beside Balance now.
- **Multi-select polish.** Clicking a line of a balance account highlights it as on the transactions tables (a press outside the table or Esc lets go of it). In multi-select mode, clicking anywhere in a row ticks or unticks it, the pencil and bin in the rows disappear, and **Esc** leaves the mode. The balance table's tick boxes now look like the transactions table's. The lines between the cells of the balance table keep the same thickness at every zoom level (as in the transactions table), the new tick column's line too, and the sort box now reads **Show newest first** / **Show oldest first**.
- **Balance account screen: tidier totals, bordered tables, adjustable columns.** The top right now reads Balance now, a divider, then Total Gains / Losses, Total Withdrawals and Total Net Gains / Losses (the gains less the withdrawals). Both tables have lines between their rows and columns, the heading is shaded like the transactions table, and the right edge of a column heading can be dragged to resize the column exactly as in the transactions table (neighbours give and take room, Shift moves the rest along, and the sizes are remembered). The "Imported / Deleted" confirmation disappears by itself after 10 seconds.
- **How a tracking account is kept is chosen once, when it is created.** An account kept by its balances can't be switched to recording transactions (or to a budget account) later, and the "How do you keep track of it?" choice is greyed out when editing an account.
- **Several lines of a balance account can be deleted at once.** Each line has a tick box, and the box in the heading ticks all of them (or none). While lines are ticked, **Delete selected** appears beside Add Entry; one Ctrl+Z brings them all back.

## 0.9.24

- **The Expected transactions table now zooms with the recorded transactions table.** Changing the zoom with the buttons at the bottom left changes both.
- **The monthly figures at the top of the Budget stay folded.** Only **Available to budget** shows until you press its arrow to open the rest. They fold away again as soon as you scroll down, and every time you open the Budget screen, until you open them again.

## 0.9.23

- **Opening the Budget screen is much quicker.** The screen is kept ready between visits, so going back to it only refreshes the figures instead of drawing every row and month again (about a third of a second instead of over a second). It still opens on the current month.
- **Dragging a month line no longer opens the side panel.** The panel opens when you click a line, not when you start dragging it.
- **Moving between months on the Budget screen is quicker.** Stepping along a month now only builds the month that comes into view instead of redrawing every column.
- **Month lines no longer have a date.** Every transactions table starts with a line for the first month of your budget, and you only choose which month each line is for. Everything after a line, down to the next one, counts towards its month; where the line sits in the list decides that, not a date. **Start a new month** now asks only for the month, and the side panel changes just the month. Transactions can no longer be refused for being dated before the budget start, because the first line always comes first.

- **Month lines open in a read-only side panel.** Clicking a "starts here" line in the transactions table shows the month and its start date. Press the pencil to change it; nothing is saved until you press Save (Cancel drops your changes). The same goes for the first month, where the budget starts.

## 0.9.22

- **Moving between months on the Budget screen no longer adds "new month" lines to your transactions.** The Budget screen only shows and budgets; it never changes your transaction tables. The earlier-months arrow stops at the first month. To start the budget earlier, change the budget start on the Transactions page.

## 0.9.21

- **Expected transactions can now have a date.** When you add one in the Expected transactions panel under a bank account, a new **When** box lets you choose **Unknown future date** (it stays on the list until you press the tick and give the date it happened), **Specific future date** (it is added to the transactions table by itself on that day), or **Specific recurring future date** (it is added by itself every week, two weeks, month, three months or year, and then moves on to its next date). The list shows when each one is due. If the app was not open on the day, whatever was due is added, on its own date, the next time you open it. For a repeating one that starts on the 29th, 30th or 31st, shorter months use their last day and it goes back to the original day after.
- **The watcher strip can be put in your own order.** Press an item on the strip and drag it to a different place; the order is remembered. Pressing an item still opens it on the Budget screen, and right-clicking still removes it. New items go at the end.

## 0.9.20

- **Budget items and groups can only be deleted if they have never been used, and nothing is archived any more.** Right-click a budget item or group and choose Delete: if it has budget or transactions on it (or an expected transaction, or sub-items), a message says why straight away, without asking you to confirm anything; only one that has never been used asks for confirmation and is deleted. A group is only deleted if none of its items has been used. An item whose budget was typed and cleared back to zero still counts as unused. Items that an earlier version had hidden are shown again, and the "(archived)" label is gone.
- **The Budget screen and the bank account transactions now have a zoom each.** The zoom in the left panel (and Ctrl with + or −, Ctrl with the mouse wheel, and Ctrl with 0) changes the zoom of the screen you are on, and each screen remembers its own. Pointing at the percentage in the panel says which screen it belongs to. Each starts from your previous zoom the first time.
- **Nothing gets cut off when the window is narrow.** The pinned-items strip, the account heading with its balances, the row of buttons above the transactions table, and the title and buttons along the top of the Budget screen now shrink to fit as the window is made narrower, and every item keeps its place. At full width they look as before.
- **Hold for next month from "Available to budget".** Right-click a positive "Available to budget" figure and choose **Hold for next month…** to set part of it aside: it leaves that month's available budget and becomes available the following month instead. It is taken from the month's income lines (largest first), exactly as if you had held them by hand. The option is only offered when the figure is positive.
- **A small marker shows when money is held.** A month with money held for next month, or released into it, shows a small flag beside its "Available to budget" figure, even when the summary box is folded away. Pointing at it says how much was held and how much was released. Right-click the figure and choose **Release held amount…** to give some or all of it back (you choose how much).
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
