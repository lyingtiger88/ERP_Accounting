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
