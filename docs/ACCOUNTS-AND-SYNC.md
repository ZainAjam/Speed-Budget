# Accounts, budgets and sync — design

Decided with Zain on 8 Nov 2026. Nothing here is built yet. This replaces the "M8 sync" idea in [PLAN.md](PLAN.md).

## Goals
- A **person** has one **account** (email + password). An account holds **many budgets**: ones it owns and ones shared with it.
- Each budget is a separate database with its own data. Budgets sync through the cloud, so any computer (and later phones) signed in to the account shows the same data.
- **End-to-end encrypted:** the server only ever stores data it cannot read.
- Sign in on a new device with **just email + password** (no approval from another device).
- A **forgot password** process exists, and it does not lose budgets in the normal case.
- Users today: Zain and his father. Not a public service.

## 1. Local structure
- One SQLite database per budget: `SpeedBudget-Data\budgets\<budget-id>\budget.db`, with its own `backups\` folder.
- A small app-level file lists the budgets known on this computer (id, name, owner, cloud link, last opened) and app-wide settings (zoom, panel layout).
- The app starts with a picker: open a local budget, create one, or sign in to see cloud budgets. It reopens the last budget automatically. Budgets can also be local-only (no account) and uploaded later.
- `AppServices` opens and closes the database and store per budget instead of once for the whole run.
- **Migration:** the first launch of the new version moves the current database and backups into `budgets\<new id>\` as "My budget". The data is untouched, so existing row stamps keep working for sync.

### Account menu (bottom-left of the side panel, below App Settings)
- **Signed out:** the budgets on this computer, New budget, Manage budgets, and **Sign in**. Local budgets stay usable offline.
- **Signed in:** the account's name and email, the budgets it can open (**owned** and **shared with me**, each marked), **New budget** (local or in the cloud), and **Sign out**.
- **Switching user** is signing out, then signing in as someone else. Signing out forgets that account's keys and sign-in token on this computer; budgets already downloaded stay on disk but can only be opened by that account again.
- Built now: the icon, the menu and the budget list. The Sign in entry is disabled until the sync service exists (step 2).

## 2. Accounts and keys (option A: the password unlocks everything)
- From the password, the app derives two things with Argon2id (slow on purpose), using different salts:
  1. a **sign-in code** sent to the server; the server stores only a hash of it;
  2. a **master key** that never leaves the device.
- The master key unlocks the account's **private key**, which unlocks the key of every budget the account belongs to. Each budget has its own random **budget key**, stored once per member, locked with that member's public key.
- Row data is encrypted on the device (AES-GCM) with the budget key before upload. The server stores opaque blobs.
- Signed-in sessions use short-lived tokens; the refresh token lives in Windows Credential Manager. The password is never stored.
- **Recovery key:** created with the account, shown once to print or save. It can also unlock the private key.
- **Forgot password** (email link proves identity, the account is restored); budgets come back from, in order:
  1. a device that is still signed in (sets the new password, nothing lost);
  2. the recovery key;
  3. another member of a shared budget re-sharing it;
  4. any computer that still holds a local copy, uploading it again.
  Data is lost only if the password is forgotten **and** none of those exist.
- Changing the password re-wraps the private key only; budget data is not re-encrypted.

## 3. Sharing
- Roles: **owner** and **editor** (everything except sharing, removing people and deleting the budget). A read-only **viewer** can be added later.
- The owner invites by email; the invitee accepts from their account. The budget key is locked for the invitee's public key.
- Removing a member rotates to a new budget key the removed person does not have, and re-encrypts going forward.

## 4. Sync
- Every row already has an id (GUID), an `UpdatedAt` hybrid-logical-clock stamp, `UpdatedBy` (device) and an `IsDeleted` tombstone; there are no foreign keys, so rows can arrive in any order.
- **Push:** send rows changed since the last push. **Pull:** ask for rows the server received after a cursor. The server numbers rows as they arrive, so the cursor never depends on any computer's clock.
- **Merge:** per row, the newer stamp wins; deletes are rows like any other.
- The server stores each row as an opaque blob keyed by budget, table and row id, plus its stamp, so new columns need no server change.
- Offline-first: sync on open, shortly after each change, and on a timer. Live "something changed" notifications later.
- **Rows that belong together** (a transaction with its split lines, both sides of a transfer, month lines and their order) are pushed and merged as one unit, followed by a repair-and-flag check after each merge.
- Each budget records the oldest app version allowed to sync; older apps are asked to update.
- Undo history stays per computer and never reverses a change that arrived from sync.
- Joining a new device downloads the whole budget; old tombstones are cleaned up after a while.

## 5. Hosting (Azure; to be confirmed against current free-tier terms)
- Sync service: a small ASP.NET Core app on Azure Container Apps.
- Database: Azure SQL Database (free offer) or Postgres.
- Email for verification and password reset: Azure Communication Services Email.
- The service handles accounts itself (Microsoft Entra is not used, because the app must see the password to derive the keys).
- Everything is set up with Bicep scripts in the repo and a GitHub Actions deploy, so it can be recreated or moved.
- Expected cost: roughly US$0–15 a month for two users. Zain creates the Azure account; the rest is scripted.

## 6. Order of work
1. **Multiple local budgets:** folders, app-level list, picker, migration of the current data. No cloud.
2. **Azure setup, accounts and the sync service:** sign up and sign in, key setup and recovery key, upload a budget, open it on a second computer, sync on demand.
3. **Automatic background sync:** grouped rows, repair check, version check, sync status indicator.
4. **Devices and recovery:** forgot-password flow, re-sharing, key rotation.
5. **Sharing** with another account.

## Open items
- Confirm Azure free-tier terms when the account is created.
- Choose the exact Argon2id settings (memory/time) after testing on the slowest computer in use.
- Phones/tablets later: the same endpoints and encryption (AES-GCM, X25519, Argon2id) are standard, so they can join.
