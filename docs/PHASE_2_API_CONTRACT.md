# Phase 2 — Synchronization API Contract

**Status:** proposed contract for implementation. Existing specialized endpoints remain supported until a versioned replacement is implemented. Endpoints described here are not live merely because they are documented.

## 1. Conventions

- Base path: `/api/sync/v1`.
- Transport: HTTPS. LAN uses the same logical envelopes over authenticated encrypted channels.
- Authentication: existing bearer session; it must be active, belong to the organization, and be bound to a registered active device.
- Tenant scope is resolved from the authenticated session. A client-supplied OrganizationId is checked against the session and is never trusted as authorization.
- IDs are UUIDs. Timestamps are UTC ISO-8601 and diagnostic only.
- JSON uses camelCase. Body size, page size, attachment size, and batch count are server-configured and returned in capabilities.
- Unknown schema versions and unsupported entity types produce explicit rejections, not silent skips.

## 2. Device lifecycle

### Register device

`POST /api/sync/v1/devices/register`

Request:

```json
{
  "deviceId": "UUID",
  "deviceName": "Finance workstation",
  "platform": "Windows",
  "appVersion": "1.0.0",
  "protocolVersion": 1,
  "publicKey": "base64-encoded-public-key",
  "keyAlgorithm": "Ed25519"
}
```

The server binds registration to authenticated UserId and CompanyId. Reusing a DeviceId with a different public key is a security conflict and must not silently replace the key.

Response:

```json
{
  "deviceId": "UUID",
  "trustState": "Pending",
  "registeredAt": "UTC timestamp",
  "requiresApproval": true
}
```

A registration is not trusted until server-side approval succeeds. Approval must be explicit (owner/admin approval or an already-authorized enrollment flow); possession of a bearer token alone must not silently promote an unknown device.

### Get current device

`GET /api/sync/v1/devices/current`

Returns DeviceId, TrustState, key fingerprint, platform, protocol compatibility, registration time, and last-seen time. Never returns private key material.

### Revoke device

The existing authenticated device-revocation endpoint remains the source of truth. Revocation invalidates refresh and sync use for sessions associated with that device. A revoked DeviceId cannot be re-registered by replaying its old registration payload.

## 3. Capabilities

`GET /api/sync/v1/capabilities`

Response:

```json
{
  "protocolVersion": 1,
  "minimumClientVersion": "1.0.0",
  "maximumBatchCount": 100,
  "maximumPayloadBytes": 1048576,
  "maximumPageSize": 200,
  "supportedEntityTypes": ["DetailAccount", "AccountingJournal"],
  "supportedTransports": ["Online"],
  "serverCursor": 0,
  "retention": {
    "changeHistoryDays": 365,
    "receiptDays": 730
  }
}
```

Capabilities are protocol metadata unless a feature is explicitly licensed or role-restricted. Never expose another tenant's cursor or data.

## 4. Upload changes

`POST /api/sync/v1/changes:batch`

Request:

```json
{
  "protocolVersion": 1,
  "changes": [
    {
      "changeId": "UUID",
      "entityType": "DetailAccount",
      "entityId": "UUID",
      "operation": "Upsert",
      "baseRevision": 3,
      "schemaVersion": 1,
      "occurredAt": "UTC timestamp",
      "payloadHash": "sha256:lowercase-hex",
      "payload": {}
    }
  ]
}
```

OrganizationId, UserId, and DeviceId are derived from the authenticated session/device registration. If echoed in an envelope, the server compares them to authenticated values and rejects mismatches.

Response preserves request order:

```json
{
  "results": [
    {
      "changeId": "UUID",
      "outcome": "Applied",
      "entityId": "UUID",
      "currentRevision": 4,
      "serverCursor": 128,
      "errorCode": null,
      "message": null
    }
  ],
  "serverCursor": 128
}
```

Allowed outcomes:
- `Applied`: accepted and committed.
- `AlreadyApplied`: matching receipt exists; no side effects repeated.
- `Conflict`: BaseRevision is stale; includes a permission-filtered current entity/revision where safe.
- `Rejected`: invalid payload, unsupported schema, forbidden operation, or domain invariant violation.
- `Retryable`: transient failure; client retains the same ChangeId and payload.

A batch may contain mixed outcomes. Each accepted change is atomic with its receipt and server change-feed row. If transport fails before a response, retry the same immutable envelopes.

## 5. Download changes

`GET /api/sync/v1/changes?afterCursor={cursor}&limit={limit}`

Response:

```json
{
  "fromCursor": 120,
  "nextCursor": 128,
  "hasMore": true,
  "changes": [
    {
      "cursor": 121,
      "changeId": "UUID",
      "entityType": "DetailAccount",
      "entityId": "UUID",
      "operation": "Upsert",
      "revision": 4,
      "schemaVersion": 1,
      "payloadHash": "sha256:lowercase-hex",
      "payload": {}
    }
  ]
}
```

- Results are strictly ordered by server cursor.
- Cursors are opaque monotonic positions, not timestamps.
- Client commits page and next cursor in one local transaction.
- If the requested cursor predates retained history, return `410 cursor_expired` with `snapshotRequired: true`; never skip missing history.
- A client must not advance its cursor merely because it received the response.

## 6. Conflict resolution

`POST /api/sync/v1/changes/{changeId}:resolve`

Request:

```json
{
  "resolution": "KeepServer",
  "expectedServerRevision": 8,
  "replacementPayload": null
}
```

Resolution is entity-policy-specific. It is a new audited decision; it never edits the original immutable change. KeepLocal enqueues a new ChangeId with BaseRevision equal to the latest server revision. KeepServer acknowledges the server version and discards only the local conflict copy after durable local commit.

Posted journals and stock movements do not expose overwrite/KeepLocal; they use correction/reversal or append-only domain workflows.

## 7. LAN pairing and exchange

LAN is a transport adapter, not a second authority.

1. A trusted device creates a short-lived pairing challenge bound to OrganizationId, DeviceId, expiry, and a random nonce.
2. The receiving device proves possession of its registered private key.
3. Both devices display organization and device identity and require explicit confirmation.
4. The pairing token is single-use, short-lived, and scoped to negotiated sync permissions.
5. Revocation or expiry immediately ends pairing.

LAN exchange mirrors batch upload/download semantics. Peer acceptance is provisional unless the peer is the designated organization authority; otherwise the server reconciles envelopes later.

## 8. USB package import/export

The offline-capable client may assemble an export package after obtaining authorized cursor metadata.

Import is two-stage:
- **Validate:** inspect package header, bounds, signature, organization, source device, algorithms, hashes, and replay status. No business rows change.
- **Commit:** apply accepted envelopes and record PackageId/ChangeId receipts transactionally.

Package outcomes: Validated, NeedsConflictResolution, Rejected, Imported, AlreadyImported.

The package uses a canonical manifest and vetted AEAD encryption. Signing and encryption keys are distinct. A package is never executable and never contains long-lived auth tokens or private keys.

## 9. Attachments

`POST /api/sync/v1/attachments/init` creates an authorized upload session for declared SHA-256, byte length, MIME type, and entity reference. Chunk upload is resumable. `POST /api/sync/v1/attachments/{uploadId}:complete` verifies the full content hash before making the immutable blob available.

Download is authorized per organization/entity. A blob is not present locally until final length and SHA-256 are verified.

## 10. Diagnostics

`GET /api/sync/v1/runs` returns the current user's permitted sync-run history. A run records transport, device, start/end, cursor range, counts by outcome, protocol version, and sanitized error codes. It must not include secrets, tokens, MFA material, private payloads, or attachment contents.

## 11. Standard error envelope

```json
{
  "error": {
    "code": "device_not_trusted",
    "message": "This device is not approved for synchronization.",
    "retryable": false,
    "correlationId": "UUID"
  }
}
```

Suggested HTTP mapping:
- 400: malformed envelope/schema.
- 401: missing/expired/invalid session.
- 403: organization, role, device trust, or entity authorization failure.
- 409: revision conflict or idempotency-key payload mismatch.
- 410: cursor expired; snapshot required.
- 413: batch/package/payload too large.
- 422: domain validation rejection.
- 429: rate limit.
- 5xx: retryable only when explicitly identified by the error contract.

## 12. Compatibility and rollout

1. Keep existing journal and detail-account routes operational during migration.
2. Add the new protocol under v1; do not silently change existing request DTOs.
3. Introduce feature flags per organization for device-bound sync and general change processing.
4. Register device identity only through authenticated enrollment; never infer identity from device name/platform.
5. Dual-read/dual-write only where idempotency guarantees are explicit.
6. Remove legacy routes only after supported clients migrate and telemetry confirms no use.
7. Protocol incompatibility fails closed with a clear upgrade requirement.
