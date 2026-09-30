# Synchronization Protocol

## Goals
- Work offline on Windows and Android
- Synchronize online through the server
- Exchange changes offline through USB
- Prevent silent data loss
- Preserve accounting auditability

## Sync identity
Each device receives a stable DeviceId.
Each organization has an OrganizationId.
Every syncable entity uses a globally unique EntityId.

## Change journal
Every local mutation creates a change record:

```text
ChangeId
OrganizationId
DeviceId
EntityType
EntityId
Operation
BaseRevision
NewRevision
OccurredAt
PayloadHash
Payload
```

ChangeId must be unique and idempotent.

## Revisions
Entities carry:
- EntityId
- Revision
- CreatedAt
- UpdatedAt
- LastChangedByDevice
- IsDeleted / tombstone when applicable

## Online sync
1. Client authenticates.
2. Client submits changes after its last acknowledged cursor.
3. Server validates permissions and domain invariants.
4. Server applies accepted changes transactionally.
5. Conflicts are returned explicitly.
6. Client downloads server changes after its cursor.
7. Client applies changes locally.
8. Both sides advance sync cursors only after successful commit.

## Conflict handling
Do not use one universal strategy.

Examples:
- Reference data: field-level merge may be possible.
- Draft documents: interactive conflict resolution.
- Posted accounting entries: no silent overwrite; use correction/reversal workflow.
- Inventory transactions: append-only movement model where practical.
- Security/permissions: server-authoritative.

## USB sync
USB uses the same logical change journal as online sync.

Suggested package extension:
`.erp-sync`

Package contents:
```
manifest.json
changes.dat
attachments/
signature.sig
```

Manifest includes:
- FormatVersion
- PackageId
- OrganizationId
- SourceDeviceId
- CreatedAt
- FromCursor
- ToCursor
- ChangeCount
- Encryption metadata
- Content hashes

## USB security
- Encrypt package contents
- Sign packages
- Reject tampered packages
- Detect replayed PackageIds
- Validate organization and device identity
- Never transfer raw long-lived authentication secrets
- Keep an import/export audit trail

## Attachments
Large files should be content-addressed and synchronized separately from ordinary records using hashes to avoid duplicate transfer.

## Recovery
A client must be able to rebuild its local state from:
- latest valid backup/snapshot
- server change history after the snapshot

USB packages are transport artifacts, not primary backups.


## Implemented optimistic revision flow

Floating detail accounts are the first mutable entity using the revision protocol end to end.

- New offline entity: `BaseRevision = 0`.
- Existing entity edit: the client sends its cached `BaseRevision`.
- The server applies the mutation only when `BaseRevision` equals the current server revision.
- An accepted mutation increments Revision and appends an ordered server change.
- Repeated `ChangeId` values are idempotent through server receipts.
- A stale mutation returns an explicit Conflict with the current server entity/revision.
- The client stores conflicts separately from Outbox retries so they do not loop forever.
- Resolution can keep the server state or requeue the local state against the latest server revision.

Detail-account server changes also have an independent sequence cursor, so clients can pull only changed master-data rows instead of downloading the full detail list on every sync.
