# Offline-First Client Storage

The Windows and Android clients maintain their own local SQLite database.

## Purpose

The local database is not a copy of the server database file. It is a client-side working store used for:

- Cached reference data
- Offline reads
- Future offline writes
- Pending synchronization changes
- Sync metadata
- Device-local state

## Current schema

### local_meta
Stores client-side metadata such as the last successful cache refresh.

### cached_user_profile
Stores the last authenticated user profile for local display/context.

This table does **not** currently enable offline authentication.

### cached_accounts
Stores the chart of accounts downloaded from the API.

### sync_outbox
Stores local mutations that must eventually be synchronized.

Fields include:

- change_id
- company_id
- entity_type
- entity_id
- operation
- payload_json
- created_at
- attempt_count
- last_error
- sent_at

## Current behavior

After an online login:

1. The user profile is cached locally.
2. The chart of accounts is downloaded.
3. The previous local account cache for that company is replaced transactionally.
4. The dashboard reads local cache statistics.
5. The Accounting navigation can display the cached chart of accounts directly from SQLite.

This makes it possible to verify local persistence even if the API is stopped after login.

## Next steps

1. Add domain-specific offline repositories.
2. Queue local create/update/delete operations into sync_outbox.
3. Add sync cursors.
4. Add push/pull synchronization.
5. Add conflict policies.
6. Add secure offline trusted-device authentication.
7. Add USB transport for the same change journal.


## Journal Outbox Processing

Accounting documents queued as Pending are uploaded to `POST /api/accounting/sync-journal`.

The upload uses a stable `change_id` and local document ID. The server stores a `journal_sync_receipts` record so retries are idempotent: a timeout or repeated upload returns the already-created journal instead of posting a duplicate.

On success the client updates the local document to Synced, stores the server journal ID and final journal number, and marks the Outbox row with `sent_at`.

On failure the Outbox row remains pending and records `attempt_count` plus `last_error`. Network/auth failures stop the current batch; validation errors stay visible for manual correction or later retry.

Legacy pending documents created before fiscal-year IDs were introduced are backfilled from the cached fiscal-year date range before automatic upload.


## Journal Pull and Cursor

The server maintains an ordered `journal_server_changes` log with a monotonically increasing sequence cursor. Existing journals are backfilled into this log by the compatibility bootstrap.

Clients store the last applied cursor in `local_meta` under a company-scoped key. They request only changes after that cursor from `GET /api/accounting/sync/journals`.

Each pulled journal is merged by `server_id`. A journal previously pushed by the same device updates its existing local row, while a journal created by another device is inserted as a new Synced local document. Lines and floating-detail dimensions are replaced transactionally. The cursor advances only in the same successful SQLite transaction.

A full accounting sync now runs in this order: refresh accounting master data, backfill legacy fiscal-year IDs, push Outbox changes, then pull journal deltas until the server reports no more pages.
