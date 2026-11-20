# Phase 2 — Implementation Audit

**Audited branch:** `main`  
**Working branch:** `feature/phase-2-sync-engine`  
**Audit type:** static repository inspection and documentation completion  
**Runtime/build/test claims:** none. This audit does not replace automated or integration tests.

## Executive summary

The repository has a meaningful synchronization foundation, but Phase 2 is not complete. The existing implementation is specialized rather than a general synchronization engine.

The protocol specification has been expanded in [SYNC_PROTOCOL.md](SYNC_PROTOCOL.md). The README checklist remains unchanged because documentation is not implementation evidence.

## Verified existing foundations

| Area | Evidence in repository | Audit conclusion |
|---|---|---|
| Local outbox | `local_database.dart`, `sync_outbox` table | Exists; stores ChangeId, company, entity, operation and payload |
| Outbox processor | `outbox_sync_service.dart` | Exists for a finite set of entity/operation handlers |
| Journal upload | `AccountingService.SyncJournalAsync` | Exists with idempotency receipts |
| Journal delta pull | `JournalPullSyncService` and server pull endpoint | Exists with cursor-based paging |
| Detail-account revisions | `AccountingService.SyncDetailAccountAsync` | Exists for this entity type |
| Detail-account idempotency | `DetailAccountSyncReceipts` | Exists |
| Detail-account delta pull | `DetailAccountPullSyncService` | Exists with independent cursor |
| Detail-account conflict workflow | `local_database.dart` conflict storage/resolution methods | Exists for detail accounts |
| Device trust primitives | `TrustedDevice` and `AuthSession.DeviceId` | Authentication-side data model exists; sync integration is not established |

## Gaps found by static inspection

### 1. Device identity is not end-to-end

- Server authentication has a trusted-device model and optional device association on sessions.
- The local sync outbox schema has no DeviceId column.
- Outbox envelopes sent by the current sync processor do not consistently carry a device identity.
- Therefore, the presence of trusted-device authentication entities does not prove that sync requests are device-bound.

**Required before completion:** client identity generation and secure storage, registration/approval, request binding, server enforcement, revocation behavior, and recovery/reinstall rules.

### 2. Revision support is entity-specific

Detail accounts have a revision flow. The inspected code does not establish a reusable revision/change framework for every syncable entity.

**Required before completion:** a common change envelope and explicit per-entity revision policy, including tombstones where deletion is supported.

### 3. Outbox processing is a handler list, not general delta upload

The outbox processor contains explicit branches for selected entity types and operations. Unsupported operations are marked failed. This is a useful foundation, not a generic dispatcher.

**Required before completion:** typed outcomes, durable receipts for each supported entity, payload schema versioning, bounded batch endpoint, and a documented extension contract.

### 4. Download is specialized

Journal and detail-account delta pulls exist. Other data currently includes cache refresh/replacement paths; this is not equivalent to a general ordered server change feed.

**Required before completion:** per-entity change feeds/cursors or a unified feed, transactional local apply + cursor advancement, tombstone propagation, and recovery when cursors expire.

### 5. Conflict handling is specialized

The detail-account conflict path has an interactive resolution flow. Other entity classes do not yet have the documented policy framework.

**Required before completion:** entity-specific policy registry and user-facing conflict review for entities that require human decisions. Posted accounting entries must remain immutable.

### 6. Transport and package security are not implemented by the inspected sync services

No end-to-end LAN transport or `.erp-sync` package pipeline was found in the inspected sync module. Package encryption, signatures, and replay protection remain future work.

**Required before completion:** pairing/trust, signed canonical manifests, AEAD encryption, package and change replay records, import quarantine/validation, and auditable transactional import.

### 7. Attachments and diagnostics are not end-to-end

No attachment content-addressed transfer pipeline or complete sync-run history model/UI was identified in the inspected sync module.

**Required before completion:** hash-addressed blobs, resumable transfer, integrity checks, access controls, sync run records, sanitized errors, and history UI.

## Work completed in this audit (no runtime tests required)

- Rewrote `docs/SYNC_PROTOCOL.md` as a normative protocol specification.
- Documented identity boundaries and explicitly distinguished a DeviceId from proof of device trust.
- Defined change envelope, canonical payload hashing, revisions, tombstones, idempotency receipts, cursor atomicity, and typed outcomes.
- Defined conflict policies by entity class.
- Defined LAN pairing requirements and the `.erp-sync` package import/export lifecycle.
- Specified cryptographic requirements using established primitives rather than custom cryptography.
- Defined attachment integrity and diagnostics requirements.
- Recorded the current implementation boundary without marking unimplemented features complete.

## Additional static findings from client sync review

### 8. Outbox failure lifecycle needs a terminal state

The current outbox selects every row where `sent_at IS NULL`. `markOutboxFailed` increments an attempt counter and records an error, but it does not distinguish retryable failures from permanent rejection or move exhausted items to a dead-letter/conflict state. Unsupported entity/operation pairs are also marked failed while remaining pending, so they can be selected on every future run.

**Required before completion:** typed retryable/permanent outcomes, bounded exponential backoff with jitter for transient failures, a terminal rejected/dead-letter state, user-visible remediation, and a safe retry action that preserves ChangeId only when the payload is unchanged.

### 9. Store-document upload idempotency must be verified

The outbox processor passes ChangeId to the journal and detail-account sync APIs. The inspected store invoice, purchase receipt, and warehouse transfer create calls do not pass the outbox ChangeId into their API calls.

**Risk:** a server commit followed by a lost response may lead the client to retry a create request without a stable idempotency key. This can create duplicate business documents unless the domain API independently provides an equivalent idempotency guarantee.

**Required before completion:** propagate ChangeId into server-side receipts for every create operation, or document and prove an equivalent idempotency mechanism. Add duplicate-delivery and lost-response tests before release.

### 10. Pull cursor handling is transactionally sound in the inspected paths, but recovery is incomplete

Both detail-account and journal page-apply methods write entity rows and their respective cursor inside the same SQLite transaction. This is a good atomicity foundation.

However, the inspected protocol/client path does not yet implement a complete cursor-expiry/snapshot recovery contract. It also lacks a unified cursor namespace across entity types.

**Required before completion:** typed cursor-expired response, snapshot bootstrap, retention watermark, and restart/recovery tests.

### 11. Sync run diagnostics are currently only partial

The orchestration returns counts for the current run and stores a last-sync timestamp, but a durable sync-run history with transport, cursor range, per-outcome counts, correlation ID, and sanitized error categories was not identified in the inspected implementation.

**Required before completion:** persistent run history on client and server, redaction rules, retention, and a user-facing diagnostics view.

## Recommended implementation sequence

1. Device identity end-to-end (client + server + auth integration).
2. General change envelope, schema versioning, entity revisions, and tombstones.
3. General upload endpoint, receipts, typed outcomes, and server change feed.
4. Transactional client apply and generic cursor management.
5. Per-entity conflict policy and conflict UI.
6. LAN pairing and transport.
7. `.erp-sync` export/import, cryptographic protection, and replay prevention.
8. Attachment transfer and sync diagnostics/history.

## Completion rule

A README checkbox may be marked complete only when:
- implementation exists on the working branch;
- automated tests cover success, duplicate delivery, stale revisions, authorization boundaries, and failure/retry behavior as applicable;
- platform-specific behavior is verified on Windows and Android where applicable;
- security-sensitive import/cryptography paths have dedicated tests and review.

Documentation-only work is complete in this audit. Phase 2 implementation remains open.
