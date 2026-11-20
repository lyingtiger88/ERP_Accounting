# Synchronization Protocol

**Status:** normative design specification. A documented requirement is not considered implemented until its server/client code and automated tests are merged.

## 1. Goals and invariants

- Windows and Android clients must support offline work.
- Online synchronization uses the server as the authoritative coordinator.
- Devices may exchange changes through LAN or removable media without bypassing domain validation.
- No accepted local change may disappear silently.
- Posted accounting entries and inventory movements are immutable business facts; corrections use explicit domain workflows.
- Synchronization must be safe under retries, process crashes, duplicate delivery, clock skew, and partial connectivity.

### Non-negotiable invariants

1. **Organization isolation:** every change, cursor, device, package, attachment, and receipt is scoped to one organization.
2. **Stable identity:** device and entity identifiers survive ordinary application restarts and upgrades.
3. **Idempotency:** replaying the same change identifier with the same canonical payload returns its original result; reusing it with a different payload is rejected.
4. **Atomicity:** applying a change and recording its receipt/change-feed event happen in one database transaction.
5. **Cursor safety:** a cursor advances only after all changes in the page have been durably applied.
6. **No silent overwrite:** stale writes return a typed conflict or a domain-specific rejection.
7. **Server authorization:** a client-supplied DeviceId is an identifier, not proof of device authenticity or authorization.
8. **Auditability:** imports, exports, rejected packages, conflict decisions, and device revocations are attributable and timestamped.

## 2. Identity model

| Identifier | Scope | Purpose |
|---|---|---|
| OrganizationId | Tenant | Prevent cross-company data exchange |
| UserId | Account | Attribute the human actor |
| DeviceId | Installation/device registration | Attribute the client installation |
| EntityId | Entity | Globally unique entity identity |
| ChangeId | One mutation | Stable idempotency key |
| PackageId | One exported package | Package replay detection |
| AttachmentHash | Content | Content-addressed attachment identity |

### Device registration and trust

- Generate a cryptographically random DeviceId on first installation and persist it in the platform's application-support storage.
- Do not derive DeviceId from hardware serial numbers, MAC addresses, hostnames, or other mutable/privacy-sensitive hardware identifiers.
- Store a separate installation key pair in the platform secure store (Windows DPAPI/Credential Manager-backed protection; Android Keystore). Private keys must never be exported in sync packages.
- Register DeviceId, public-key fingerprint, platform, app version, display name, organization, and registering user with the server.
- A device is not trusted merely because it supplies a known DeviceId. Registration and trust approval are server-side operations.
- Every sync request must be authenticated and authorized for the organization and device. Device revocation must invalidate subsequent sync operations.
- Device replacement/reinstallation is an explicit recovery/re-registration flow; do not silently clone an old identity.

The existing authentication domain already contains trusted-device and session-to-device concepts. The sync outbox currently does not persist a DeviceId and the sync endpoints must not be treated as device-bound until that integration is implemented and tested.

## 3. Change journal

Every syncable mutation is represented by an immutable envelope:

```json
{
  "changeId": "UUID",
  "organizationId": "UUID",
  "deviceId": "UUID",
  "actorUserId": "UUID",
  "entityType": "DetailAccount",
  "entityId": "UUID",
  "operation": "Upsert",
  "baseRevision": 4,
  "newRevision": 5,
  "occurredAt": "UTC ISO-8601 timestamp",
  "schemaVersion": 1,
  "payloadHash": "sha256:...",
  "payload": {}
}
```

Rules:
- ChangeId is generated once when the local transaction creates the outbox item. Retries must reuse it.
- The envelope is immutable after enqueue. A changed payload is a new mutation with a new ChangeId.
- PayloadHash is calculated over a documented canonical JSON representation; property ordering, number formatting, Unicode normalization, and null handling must be deterministic.
- OccurredAt is diagnostic metadata only. It must never decide conflict winners or establish authoritative ordering.
- Server sequence/cursor values, not client clocks, define pull order.
- Deletes are tombstones with revisions; physical deletion is deferred until retention and replication guarantees allow compaction.
- SchemaVersion enables explicit payload migrations. Unknown future versions are rejected without advancing the cursor.

## 4. Revisions and concurrency

Every mutable syncable entity carries:
- EntityId
- Revision (monotonically increasing within the organization/entity)
- CreatedAt and UpdatedAt (UTC)
- LastChangedByDevice and LastChangedByUser
- IsDeleted / tombstone metadata where deletion is supported

New local entities start at BaseRevision 0. Existing entity mutations include the last server-confirmed BaseRevision. The server applies a mutation only when the submitted BaseRevision matches the current revision, then increments the revision exactly once.

Revision checks and receipt creation must be transactional. A conflict response includes the current server revision and a safe current representation (subject to permissions). Clients retain the local draft separately from retryable outbox work until a user or policy resolves it.

## 5. Online synchronization

1. Client authenticates and proves it is an active, trusted device.
2. Client uploads a bounded batch of immutable change envelopes.
3. Server validates organization, device, actor permissions, schema version, payload hash, and domain invariants.
4. Server processes each change transactionally and returns an explicit outcome: Applied, AlreadyApplied, Conflict, Rejected, or Retryable.
5. Client marks only Applied/AlreadyApplied changes as sent. Conflict and Rejected changes remain visible and must not loop as ordinary retries.
6. Client downloads server changes after its last committed cursor.
7. Client applies a page and persists the page cursor in the same local transaction.
8. Client repeats while hasMore is true; cursors must be monotonic.
9. Diagnostics record duration, counts, cursor range, and sanitized errors.

A page cursor is not a timestamp. A failed page must be retried from the previous committed cursor.

## 5.1 Local Outbox delivery lifecycle

The Flutter client stores delivery state independently from the immutable change identity and payload:

| State | Meaning | Automatic selection |
|---|---|---|
| Pending | Ready for first delivery | Yes |
| Processing | Claimed by one local worker with an expiring lease | Only after lease expiry |
| Retry | Transient failure with a due timestamp | When due |
| Blocked | Authentication/authorization requires user or session recovery | On the next authenticated sync attempt |
| Conflict | Server state requires explicit resolution | No |
| Rejected | Permanent validation or unsupported-operation failure | No |
| DeadLetter | Retry budget exhausted | No |
| Sent | Server accepted the change and local acknowledgement committed | No |

Batch claiming and lease assignment happen in one SQLite transaction. A worker clears its lease when it records success or failure. A worker crash leaves the item recoverable after the five-minute lease expires. Retry uses capped exponential backoff; the stable ChangeId and payload are never regenerated during retry. If the server may have committed but the local acknowledgement failed, the client retries the same idempotency key.

## 6. Idempotency and receipts

- Receipt uniqueness is scoped by OrganizationId + ChangeId.
- Store the canonical payload hash and the original outcome/response.
- Same ChangeId + same hash: return the original outcome without reapplying side effects.
- Same ChangeId + different hash: reject as an idempotency-key collision and audit it.
- Receipt retention must be at least as long as the maximum supported offline/package replay window. Any receipt compaction requires a durable replay watermark.
- Domain side effects, receipt, and server change-feed row commit atomically.

## 7. Conflict policy by entity class

There is no universal last-write-wins policy.

| Entity class | Policy |
|---|---|
| Reference/master data | Revision check; field-level merge only for explicitly mergeable fields |
| Draft documents | Preserve both versions and provide interactive resolution |
| Posted accounting journals | Immutable; correction/reversal workflow only |
| Inventory movements | Append-only movement model; never overwrite stock history |
| Security, roles, licenses | Server-authoritative; client edits are not accepted as authority |
| Attachments | Content-addressed immutable blobs; metadata conflicts handled separately |

Conflict UI must show entity, local/server revisions, changed fields, timestamps as informational context, and available safe actions. A resolution creates a new change against the latest server revision; it does not mutate the old envelope.

## 8. LAN synchronization

- LAN is a transport for the same signed change envelopes, not a separate consistency protocol.
- Peers authenticate one another using registered device keys and a short-lived pairing challenge.
- Pairing must display and confirm organization and device identity on both ends.
- Only encrypted transport is allowed; discovery advertisements must not contain business data or credentials.
- A peer may relay changes but cannot elevate permissions or declare a change accepted by the server.
- Offline peer receipts are provisional until reconciled with the authoritative server or a designated organization authority.
- LAN sync must support interruption/resume and bounded batches.

## 9. USB `.erp-sync` package

Suggested package layout:

```
manifest.json
changes.dat
attachments/
signature.sig
```

Manifest fields:
- FormatVersion and SchemaVersion
- PackageId
- OrganizationId
- SourceDeviceId and source public-key fingerprint
- CreatedAt (UTC; informational only)
- FromCursor and ToCursor
- ChangeCount
- Encryption algorithm/KDF parameters and salt/nonce metadata
- Per-entry content hashes and package hash

Import sequence:
1. Read package into quarantine; do not apply any content yet.
2. Validate format/version, size/count limits, organization, source device trust, signature, and all hashes.
3. Decrypt only after signature and metadata validation; use authenticated encryption (AEAD).
4. Check PackageId and each ChangeId against replay/receipt stores.
5. Validate all change envelopes and authorization.
6. Present conflicts/rejections before committing where user action is required.
7. Apply accepted changes transactionally and write import audit records.
8. Mark PackageId consumed only in the same transaction as successful import finalization.

Never import executable content, paths supplied by the package, raw authentication tokens, or private keys. Prevent path traversal and decompression bombs. Export packages are transport artifacts, not backups.

## 10. Package cryptography and replay protection

- Use a vetted AEAD construction (for example AES-256-GCM) with a fresh unique nonce per encryption key.
- Derive a package encryption key from a user-provided passphrase using a memory-hard KDF (Argon2id preferred) with versioned parameters and a random salt; never store the passphrase.
- Sign the canonical manifest and package content hash with the registered device signing key. Keep signing keys separate from encryption keys.
- Verify signature before trusting package metadata or importing payloads.
- Store consumed PackageIds and imported ChangeIds per organization. Reject duplicate PackageIds; idempotent duplicate ChangeIds may be acknowledged only if their hashes match.
- Include format and cryptographic algorithm identifiers to allow safe future migration.
- Do not invent or implement custom cryptographic primitives.

## 11. Attachments

- Address attachment content by SHA-256 hash and store immutable blobs.
- Sync metadata separately from blob transfer; transfer missing hashes only.
- Verify size and hash after download and before linking the blob to an entity.
- Enforce organization authorization, per-file and aggregate size limits, MIME allowlists where appropriate, and malware scanning at the server boundary.
- Use resumable chunk transfer for large files. A partially downloaded blob is never visible as complete.
- Deletion uses tombstones and retention rules; shared blobs are not deleted while referenced.

## 12. Diagnostics and history

Record per sync run:
- RunId, organization, user, device, transport, start/end time
- Cursor start/end and package ID where applicable
- Counts: queued, uploaded, applied, already applied, downloaded, conflicted, rejected, retried
- Outcome and sanitized error category/code
- App/protocol version

Never log access/refresh tokens, private keys, passphrases, MFA secrets, full sensitive payloads, or attachment contents. Provide a local history view and a server-side audit trail with retention controls.

## 13. Recovery

A client rebuilds state from a verified snapshot plus server change history after the snapshot cursor. USB packages are not primary backups. If a cursor is older than retained server history, the server must require a full snapshot/resync rather than silently skipping changes.

## 14. Current implementation boundary (main branch audit)

Implemented foundations:
- Local SQLite sync outbox with stable ChangeId per queued item.
- Outbox push handlers for selected accounting and store draft operations.
- Journal delta pull and cursor handling.
- Detail-account optimistic revisions, idempotent receipts, delta pull, conflict persistence, and interactive keep-server/keep-local resolution.
- Accounting journal push/pull and server-side idempotency receipts.

Not yet implemented end-to-end:
- DeviceId generation, secure persistence, registration, and binding of every outbox envelope/request to an active trusted device.
- General-purpose change envelope and entity revision framework beyond the existing journal/detail-account paths.
- General-purpose upload/download and entity-specific conflict policies.
- LAN transport and pairing.
- `.erp-sync` export/import, package encryption/signatures, and replay protection.
- Attachment transfer and sync diagnostics/history UI.

Do not mark a capability complete based on this document alone. Update the README checklist only after implementation and automated verification.
