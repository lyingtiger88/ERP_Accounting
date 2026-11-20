# Phase 2 — Synchronization Security Threat Model

**Status:** design and review checklist. This is not a penetration-test report or a claim that listed controls are implemented.

## Assets

- Accounting journals, balances, customer/supplier identities, inventory movements, attachments.
- Organization membership, user roles, licenses, trusted-device registrations, sessions.
- Local outbox, conflict copies, change feeds, sync cursors, idempotency receipts.
- Device signing keys, package encryption keys, passphrases, and recovery material.
- Audit history and backup/snapshot data.

## Trust boundaries

1. Flutter application process and local database.
2. OS secure storage and device key store.
3. Network boundary (internet or LAN).
4. Zarr API and server-side authorization/domain services.
5. Server database and change-feed/receipt storage.
6. Removable media and imported package quarantine.
7. Attachment storage and malware-scanning boundary.

Treat the client, LAN peer, USB package, filenames, timestamps, and all payload fields as untrusted. A valid bearer session does not by itself prove a particular device installation is trusted.

## Threat register

| Threat | Example | Required control | Verification evidence |
|---|---|---|---|
| Tenant spoofing | Client submits another CompanyId | Derive tenant from session; reject mismatches | Cross-tenant API tests |
| Device impersonation | Attacker copies DeviceId | Registered public-key proof; server-side trust state | Invalid-signature and revoked-device tests |
| Stolen session | Refresh token copied from disk | OS-protected token storage, rotation, revocation, TLS | Session theft/reuse tests |
| Replay of change | Old upload resent after success | Organization-scoped ChangeId receipt and payload hash | Duplicate and hash-mismatch tests |
| Replay of USB package | Same package imported twice | Durable PackageId ledger and per-change receipts | Re-import tests |
| Payload tampering | Amount changed in transit/package | TLS, canonical hash, package signature, AEAD | Tamper tests |
| Cursor rollback/skip | Client advances despite failed page | Atomic local apply + cursor transaction | Crash/restart tests |
| Stale overwrite | Old draft overwrites newer record | BaseRevision compare-and-swap and typed conflict | Concurrent update tests |
| Malicious package | Zip bomb/path traversal | Quarantine, strict limits, safe extraction, no executable payload | Fuzzing and archive security tests |
| Attachment substitution | Blob content differs from metadata | Content hash, length verification, immutable addressing | Corrupt/truncated blob tests |
| Privilege escalation | Viewer submits a write | Server authorization on each entity operation | Role matrix tests |
| Sensitive diagnostics | Logs contain tokens/payloads | Allowlisted structured fields and redaction | Log inspection tests |
| Clock manipulation | Client clock set far in past/future | Server sequence is authoritative; client time diagnostic only | Clock-skew tests |
| Device revocation race | Revoked device keeps syncing with refresh token | Check device trust on every refresh and sync request | Revocation integration tests |
| Key loss/reinstall | App reinstall loses private key | Explicit re-enrollment/recovery; never silently clone identity | Recovery-flow tests |
| Resource exhaustion | Oversized batch/attachment | Limits, quotas, rate limiting, bounded concurrency | Load and limit tests |

## Security requirements

### Device identity and authentication

- Generate installation identifiers with a cryptographically secure random generator.
- Keep the device private key non-exportable where platform capabilities allow.
- Store only public key/fingerprint and trust metadata on the server.
- Bind sessions to DeviceId at login/enrollment and check device trust at refresh and every sync operation.
- DeviceName and Platform are descriptive metadata, not identity proof.
- Revocation must invalidate all sessions and pairing grants for that device.
- A reinstall or key loss requires explicit re-enrollment; no hardware fingerprinting.

### Change processing

- Derive CompanyId and UserId from authenticated context; verify the active DeviceId.
- Validate role permissions and domain invariants on the server for every change.
- Persist change application, idempotency receipt, and feed event in one transaction.
- Reject same ChangeId with a different canonical payload hash.
- Bound payload depth, string lengths, batch count, and total bytes.
- Treat client OccurredAt as informational only.

### Package cryptography

- Use established libraries and standard primitives only.
- Prefer Ed25519 signatures for registered device signing keys and AES-256-GCM (or another approved AEAD) for package confidentiality/integrity.
- Derive passphrase-based encryption keys with Argon2id and unique random salts; parameters are versioned and benchmarked for target devices.
- Nonces must never repeat under the same AEAD key.
- Verify signature and metadata before import; authenticate/decrypt before parsing business payloads.
- Separate signing keys from encryption keys.
- Never embed bearer tokens, refresh tokens, MFA secrets, or private keys in a package.
- Do not invent a custom cryptographic format; use a reviewed container/encoding and document canonicalization precisely.

### USB import safety

- Import to a quarantine directory with generated filenames; never honor package paths.
- Enforce compressed and decompressed size limits, entry count, nesting depth, and allowed file types.
- Reject absolute paths, traversal segments, symlinks, duplicate manifest entries, and executable content.
- Verify all hashes and signatures before applying any change.
- Keep validation side-effect-free; commit accepted changes and replay records atomically.
- Record rejected import attempts without logging sensitive package content.

### LAN safety

- Pairing challenge is random, short-lived, one-use, and bound to organization/device identities.
- Both devices display the identities being paired and require user confirmation.
- Authenticate peers cryptographically; do not trust network location or mDNS discovery.
- Encrypt peer transport and limit pairing scope to sync operations.
- Revocation and expiry terminate the pairing grant.

### Privacy and observability

- Logs may contain IDs, event categories, outcome codes, timings, and counts.
- Logs must not contain access/refresh tokens, passwords, MFA codes/secrets, private keys, passphrases, raw accounting payloads, or attachment bytes.
- Diagnostics are tenant- and role-scoped and have retention controls.
- Security events include registration, trust approval, revocation, package import/export, replay rejection, signature failure, conflict resolution, and cursor-expiry recovery.

## Release gates

A security-sensitive capability is not release-ready until:
1. Threat-specific automated tests exist.
2. Authorization is tested across tenant, user role, and device trust boundaries.
3. Failure and process-crash paths are tested for atomicity and cursor safety.
4. Package parsing is fuzzed and resource limits are exercised.
5. Cryptographic dependencies and algorithms receive security review.
6. Diagnostics and exception paths are checked for secret leakage.
7. Windows and Android secure-storage behavior is verified on supported OS versions.

## Current implementation caveat

The repository currently has trusted-device/session primitives in the authentication subsystem and specialized journal/detail-account sync. The inspected outbox schema does not persist DeviceId and the sync handlers do not consistently bind each envelope to an approved device. Therefore, device-bound sync, LAN peer trust, and USB package security must remain marked incomplete until end-to-end controls and release-gate tests are implemented.
