# Zarr licensing — pre-implementation architecture and rollout plan

Status: DESIGN / AUDIT ONLY. No application code, database schema, secrets, feature flags, or release artifacts are changed by this plan.

## 1. Product decisions

- Product: Zarr (product_id: `zarr`), administered by Kaliia's central licensing service.
- Trial: one 45-day trial per company, full feature access, started by an explicit owner action and timestamped by the server. A client must never mint or restart a trial.
- Commercial term: annual subscription initially.
- License scope: one company subscription shared by its authorized users and registered devices; not one license per installation.
- Connectivity: online-first. A signed offline entitlement may permit up to 7 days since the last successful server validation, but never beyond the subscription/trial expiry. The grace duration is a product policy and must be centrally configurable.
- Expiry/suspension: read-only mode. Preserve local and server data; allow viewing, search, reports, export and backup. Block all new mutations and prevent queued writes from being silently committed.
- Renewal: restore write access after a fresh server validation; no reinstall and no data reset.
- Windows and Android must implement the same entitlement states and policy.

## 2. Architecture

1. Kaliia License Service is the sole authority for account status, product entitlement, trial start/end, subscription term, suspension/revocation and device policy.
2. Zarr ASP.NET API validates company ownership and entitlement server-side for every mutating API. The client cannot assert its own company, account, expiry, trial start, or entitlement.
3. The licensing service issues a short-lived, signed entitlement document. It contains only the minimum claims needed by Zarr: product, company binding, entitlement state, issued-at, not-before, expiry, offline-until, policy version and a unique entitlement identifier.
4. The signing private key remains in the central service. Zarr clients and API receive only public verification keys. Key identifiers and rotation overlap must be supported.
5. Windows and Android persist the signed document using platform-protected storage. Local device time is not sufficient to extend access; clients must detect rollback/tampering and fall back to read-only when the signed validity window cannot be trusted.
6. The server remains authoritative for online writes. Offline writes are accepted into a clearly marked local pending queue only while the signed entitlement permits them; they are not treated as server-committed until the server revalidates and accepts them.

## 3. Access matrix

| State | Create/edit/delete locally | Server writes | View/search/reports | Export/backup |
|---|---|---|---|---|
| Trial active | Allowed | Allowed | Allowed | Allowed |
| Paid active | Allowed | Allowed | Allowed | Allowed |
| Offline within signed grace | Allowed as pending local operations | Not available until reconnect | Allowed | Allowed |
| Offline grace elapsed | Blocked | Blocked | Allowed | Allowed |
| Expired | Blocked | Blocked | Allowed | Allowed |
| Suspended/revoked | Blocked | Blocked | Allowed | Allowed |
| License service unavailable | Existing signed grace only | Server policy decides; fail closed for unverified writes | Allowed | Allowed |
| Unlinked company | No commercial writes until linked/entitled | Blocked when enforcement is enabled | Existing data remains readable | Allowed |

Important: a pending local operation created during valid offline grace must be preserved after expiry, displayed as pending/not synchronized, and must not be reported as committed. On reconnection, the server rechecks the current entitlement before accepting it. The product owner must decide whether operations created during a valid grace window may be synchronized after the subscription expires; secure default is to reject writes after expiry while preserving the pending payload for export/manual resolution.

## 4. Trial and subscription lifecycle

- Trial start is an idempotent server transaction performed only after verified company-owner action and account/company linking.
- Persist immutable `trial_started_at` and `trial_expires_at` in UTC. Repeated requests return the existing trial; they never reset it.
- Trial uniqueness is enforced by a database constraint at the company/product level, including after unlink/relink, account change, device replacement or reinstall.
- Paid activation, renewal, suspension, cancellation and manual-payment approval are server-side state transitions with audit records.
- Expiry is computed from server UTC. Client clock changes cannot increase entitlement.
- Define deterministic precedence for multiple entitlements: an active paid term supersedes trial; suspension/revocation overrides active terms; duplicate active rows are treated as a data-integrity error and surfaced for administrative resolution, not selected arbitrarily.
- No expiry value must not implicitly mean perpetual access unless an explicit perpetual entitlement type is approved.

## 5. Company and device identity

- Preserve the existing owner-verified link flow: ERP company Owner session plus a valid, verified Kaliia account token.
- Never trust a client-supplied Supabase UUID, company ID, role, or JWT user metadata as proof of ownership.
- Linking, unlinking, account replacement and ownership transfer require explicit, audited server-side flows.
- Subscription is company-scoped. Device registrations are separately limited/revocable by policy; replacing a device must not consume another company license.
- Define recovery for a lost owner account and prevent an old owner from silently relinking a company after a verified ownership transfer.

## 6. Offline security requirements

- Use signed, bounded entitlements; never put a Supabase service-role key or signing private key in Flutter, Windows packages, config files or Git.
- Validate signature, issuer, audience/product, company binding, key ID, policy version, issued-at, not-before, expiry and offline-until.
- Store the highest trusted server time / monotonic validation evidence where supported; detect significant clock rollback and require online revalidation.
- Do not use a locally stored boolean such as `isLicensed` as an authorization source.
- Centralize the mutation guard so every local command, repository write, background task, import, bulk operation, sync replay and API endpoint uses the same policy.
- Queue items need stable IDs, idempotency keys, creation metadata and explicit states (pending, accepted, rejected, needs-review). Never silently discard rejected items.
- Revalidate before replay. A client-side guard is usability protection, not a replacement for API enforcement.

## 7. Server enforcement audit findings (current repository snapshot)

- `apps/server_api/Program.cs` contains an optional middleware controlled by `Kaliia:EnforceZarrLicense`; current `appsettings.json` sets it to false. Keep it disabled until rollout gates pass.
- The middleware currently covers non-GET/HEAD/OPTIONS requests under `/api`, with auth and license routes excluded. Audit every mapped route and any non-`/api` mutation route before enabling.
- `ZarrLicenseService.CanWriteAsync` currently reads central account and license rows and checks status/expiry. It needs deterministic handling for duplicate entitlements, explicit perpetual types, cancellation/revocation, trial semantics and service errors.
- The current service returns true for a null `expires_at` on an active license. This must be replaced by an explicit, approved perpetual entitlement rule.
- The current account/license lookup uses `limit=1` without a deterministic ordering or duplicate detection. This must be corrected before commercial enforcement.
- Current server config contains public Supabase URL/public key only; service-role key is expected from server-only secret configuration. Confirm deployment secret presence and least-privilege policies before enabling enforcement.
- Existing documentation states that Flutter offline write/outbox gating is incomplete. This is a release blocker.
- Current code and docs indicate a 45-day trial/account UI exists, but end-to-end server-authoritative trial creation and cross-platform expiry behavior still require verification with real sessions.
- Do not change `Kaliia:EnforceZarrLicense` to true during development or before tenant migration and end-to-end tests.

## 8. Required audit before implementation

- Map all Flutter source roots, license UI/state management, local database/repositories, outbox/sync paths, import/export and background workers for Android and Windows.
- Trace every server mutation route, including bulk endpoints and any route not under `/api`.
- Inspect Kaliia database schema, RLS policies, constraints, indexes, trial creation/activation functions, payment approval and audit history. Confirm actual column names and semantics; do not infer them from client code.
- Verify that trial uniqueness survives company unlink/relink and owner/account changes.
- Inspect local persistence and platform secure-storage behavior on Android and Windows.
- Confirm release signing, app update and migration behavior so a license rollout does not force reinstall or data loss.
- Verify backups and export remain available after expiry and during service outage.

## 9. Implementation sequence (after audit sign-off)

1. Finalize central entitlement schema, constraints, RPC/service contract, audit events and state transition rules.
2. Harden server-side license service and API authorization; add deterministic fail-closed tests and cache policy.
3. Implement signed entitlement issuance/verification and key rotation.
4. Implement a shared client entitlement state machine and protected storage on Windows and Android.
5. Gate every local mutation and offline queue replay; preserve pending/rejected operations.
6. Align UI and recovery flows (trial, active, offline grace, expired, suspended, unlinked, renewal).
7. Run integration and security tests on both platforms, then a staged tenant migration with enforcement disabled.
8. Enable enforcement for a controlled pilot only after explicit acceptance; monitor and expand gradually.

## 10. Acceptance test matrix

- Trial starts once, lasts exactly 45 x 24 hours from server timestamp, and cannot be reset by reinstall, device change, unlink/relink, account switch or clock rollback.
- Paid activation, renewal, expiry, suspension and revocation take effect consistently on both platforms.
- Active paid entitlement wins over trial; duplicate or malformed entitlement records fail safely.
- Online API writes are denied for expired, suspended, revoked, unlinked and invalid-company contexts.
- Every local mutation path and background worker is blocked after expiry/grace, including imports and bulk operations.
- Offline work within grace is marked pending and is never misrepresented as server-committed.
- Offline grace cannot exceed paid/trial expiry and cannot be extended by clock changes or app reinstall.
- Reconnection after expiry does not silently commit queued mutations; pending data remains recoverable/exportable.
- Read-only access, reports, exports and backups remain available in all non-entitled states.
- Cross-company access, token substitution, stale sessions and owner transfer are tested.
- Android and Windows produce identical policy outcomes.
- Service outages, slow responses, key rotation and unknown policy versions have deterministic safe behavior.
- No privileged secret appears in client binaries, repository history, logs or crash reports.

## 11. Rollout and safety gates

- Keep commercial enforcement OFF by default.
- Do not activate checkout/pricing or migrate all companies until schema, real accounts, manual payment activation, backup/restore and both-platform tests pass.
- Pilot with explicitly selected test companies and a rollback switch.
- Before enabling for existing companies, provide owner-linking instructions, support/recovery process and a clear grace/migration window.
- Never delete or lock the customer's accounting data because a subscription expires.
- No release build is part of this design/audit task.
