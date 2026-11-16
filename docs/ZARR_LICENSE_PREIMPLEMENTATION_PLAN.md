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


## 12. Live repository and Supabase audit — 2026-11-16

This section records read-only inspection of the current `main` branch and the connected Supabase project. No live database, Edge Function, secret, application code, feature flag or release artifact was changed.

### Flutter client findings

- `KaliiaEntitlement` parses a server response with `can_write`, status and expiry, and exposes `requireKaliiaWrite`. The helper is only a local guard primitive; the inspected licensing page explicitly states full API write enforcement is still under development.
- The entitlement client calls `kaliia-entitlement` for status and start_trial. It does not currently parse or verify a signed entitlement document, offline-until claim, issuer, audience, key ID or policy version.
- `ZarrAccountService` stores the Kaliia access and refresh tokens in Flutter Secure Storage and refreshes the Supabase session. That is separate from a persisted signed offline entitlement.
- `LocalDatabase` has a SQLite `sync_outbox` table containing change ID, company ID, entity type, entity ID, operation, payload, created time, attempts, error and sent time. It does not have an entitlement snapshot, authorization-at-creation evidence or explicit accepted/rejected/needs-review state in the inspected schema.
- `OutboxSyncService.syncPending` replays pending records to journal, detail-account and sales/inventory endpoints. The inspected implementation does not check the entitlement or offline grace before replay; it relies on the API response.
- `AccountingSyncService.syncAll` refreshes caches and then pushes pending changes. It has no visible license-state gate in the inspected implementation.
- Existing local backups are SQLite database backups. The proposed license guard must not block viewing/exporting/backing up local data.

### Live Supabase findings

- The connected project is the same project referenced by the Zarr client configuration. Live project schema and deployed Edge Functions were inspected read-only.
- `public.zurvan_licenses` is currently keyed by `(account_id, product_id)` with a unique constraint. It is not keyed by the ERP company ID.
- The license row has `type` values `trial`, `subscription`, `lifetime`; status values `active`, `suspended`, `revoked`; and a check constraint requires expiry for trial/subscription while lifetime must have NULL expiry.
- `public.zurvan_accounts.id` references `auth.users.id`. Existing license rows are attached to a Kaliia account UUID.
- The deployed `kaliia-entitlement` Edge Function calls `kaliia_start_trial` with the authenticated Supabase user ID and product ID. Its database RPC creates a 45-day trial per Kaliia account/product, not per ERP company.
- The same deployed RPC currently allows the product IDs `zurvan`, `maal`, `shopmanager`, and `zarr`; a prior overload visible in the database has an older product allowlist. The active Edge Function uses the newer signature with recovery email.
- The current `kaliia-entitlement` status action queries a single account/product license. It returns trial_available when no license exists, and marks a dated active license expired based on server-side time.
- The separate `zurvan-license-status` and `zurvan-license-start-trial` functions are legacy Zurvan-oriented flows; the latter starts a trial for product `zurvan`, so it must not be reused for Zarr.
- The central product and plan catalogue has Zarr as a product, while commercial prices/sales remain disabled pending approval. No pricing or checkout settings were changed.
- Relevant license, account, device, activation and payment tables have RLS enabled in the inspected schema. Policies and function grants still require a dedicated security review before any schema or function changes.

### Architecture decision required before implementation

The chosen commercial rule is one subscription per ERP company, shared across its users/devices. The existing central entitlement model is one license per Kaliia Auth account and product. The current company-link table exists only in the Zarr ASP.NET database; the central `zurvan_licenses` table does not contain the ERP company binding.

Therefore, implementation must not simply treat an account-level license as a company-level license. Before coding the enforcement path, design a central, immutable company binding and company-scoped entitlement identity, including ownership transfer, unlink/relink protection, one-trial-per-company/product uniqueness and account recovery. Decide whether one Kaliia account may own multiple ERP companies; the schema must enforce the approved rule. Preserve the current account-level entitlement tables for other products.

### Additional blockers and corrections to prior assumptions

- The 45-day trial is server-created and repeat calls return the existing license, but its current uniqueness scope is account + product, not company + product.
- The database explicitly models lifetime licenses, so a NULL expiry is valid only when `type='lifetime'`. The ASP.NET `CanWriteAsync` currently treats any active row with NULL expiry as perpetual without checking `type`; fix this before rollout.
- The central license table's unique account/product constraint means the ASP.NET `limit=1` query should not return duplicates under the current schema. Keep explicit integrity checks, but the main data-model gap is company binding, not arbitrary duplicate selection.
- The current Flutter offline queue does not implement the proposed seven-day grace or signed authorization. The grace policy is still a design target, not a delivered feature.
- The current client has no demonstrated cross-platform offline entitlement state machine. Windows and Android parity must be implemented and tested.
- Existing server tests cover company-link scoping and missing-secret fail-closed behavior. They do not yet exercise full entitlement status, trial expiry, subscription/lifetime semantics, API-wide mutation middleware, offline outbox replay or company-level trial uniqueness.
- The current CI workflow runs Flutter tests when client files change. A server test workflow and integrated end-to-end licensing workflow still need to be verified/added.

### User testing checkpoints

No interactive test is required from the user during the remaining source audit and architecture work. Request a user-run test only when the implementation reaches a stage requiring a real installed Windows/Android app, real ERP owner credentials, real Kaliia account linking, or an actual offline/online transition. Never ask the user to share passwords, refresh tokens, service-role keys or signing private keys in chat.



### Further live database security and commercial-catalogue checks

- The two `kaliia_start_trial` overloads are `SECURITY DEFINER`, but direct EXECUTE is granted only to `postgres` and `service_role`; `anon` and `authenticated` cannot call them directly. The Edge Function is the authenticated entry point and verifies the caller with `auth.getUser` before invoking the RPC. Preserve this boundary.
- The separate legacy `zurvan_start_trial` RPC is SECURITY INVOKER and also only executable by `postgres` / `service_role`; it is explicitly Zurvan-only.
- The inspected license/account/device/product/plan tables have RLS enabled and no direct table policies. This is consistent with keeping these tables behind trusted Edge Functions/service-role code, but the grants and all Edge Function paths must be audited together before exposing any direct client access.
- Supabase security advisors also report project-wide RLS-without-policy findings and one unrelated SECURITY DEFINER warning for `zurvan_is_creator()`, plus leaked-password protection disabled. These are wider project security findings, not changes made as part of Zarr licensing; they should be triaged separately.
- The live Zarr product is enabled but sales are disabled. Its 1-, 3-, 6- and 12-month plan slots are all unpriced and disabled. Do not enable checkout or invent prices as part of licensing implementation.
- The company-scoped entitlement model must coexist with the current account-scoped licence model used by other Kaliia products. A dedicated company/product entitlement record (or an equivalent immutable binding layer) is required; do not repurpose or weaken the existing unique account/product constraint.


### Company-link and manual-payment flow audit

- The current POST /api/license/link endpoint permits an ERP Owner to overwrite the existing company-to-Kaliia-user mapping after presenting another verified Kaliia access token. It does not currently require a transfer challenge, second-party confirmation, cooling-off period or explicit unlink/recovery protocol. Treat this as a rebind/ownership-transfer security gap; do not expose unrestricted re-linking in the commercial UI.
- The mapping is stored in the ERP API database, while the actual entitlement and payment state are in Supabase. There is no central company binding in the inspected live schema, so the two systems can disagree if local linking succeeds but a central operation fails or if the local database is restored.
- The live manual-payment Edge Function currently records payment requests by auth_user_id, product_id and plan duration. Its approval RPC extends or converts the account-level zurvan_licenses row for that Auth user/product. It does not accept or bind an ERP company ID. Zarr's company-scoped billing must therefore add a verified company reference to the order/payment path and ensure the approval RPC updates the company entitlement, without changing other products' account-level billing.
- The current payment flow has server-side checks for confirmed email, product sales-enabled state, enabled plan, receipt type/size, pending duplicate requests and superadmin review. Zarr's plans are presently disabled/unpriced, so payment submission should remain unavailable until the commercial owner configures them.
- The ASP.NET middleware covers mutating methods under /api except /api/auth and /api/license. The license-link endpoint is consequently outside commercial enforcement by design, but it needs its own strict owner-transfer controls. Any future mutating endpoint added under /api/license must be reviewed explicitly rather than assumed protected.
- Route review confirms many inventory, treasury, accounting, journal, fiscal-year and admin mutations are under /api and thus within the central middleware's path/method envelope. Before enabling it, verify every route's authentication behavior and add integration tests for the complete route map; do not rely only on prefix matching.

### Updated implementation dependency order

1. Specify the central company-binding and company-entitlement data model, including payment/order references and transfer lifecycle.
2. Define one server-to-server protocol for owner-verified link/relink, trial start, status, renewal and payment approval. The ERP API must be the authority for ERP company ownership; Kaliia must be the authority for account identity and commercial entitlement.
3. Extend the Zarr-specific payment path to carry a verified company ID and apply approved payments to the company entitlement. Leave other Kaliia products on their existing account-level model.
4. Harden API middleware and all /api/license mutation endpoints.
5. Implement signed entitlement documents and the offline queue state machine.
6. Only then wire local mutation guards and sync replay to the new entitlement contract.


## 13. Follow-up implementation foundation — 2026-11-16

A separate implementation branch, `feat/zarr-license-enforcement-foundation`, has been created. The following isolated changes are committed there; they are not merged into `main` and do not enable commercial enforcement:

- `ZarrLicenseService.CanWriteAsync` now recognizes an unexpired trial/subscription only when an explicit supported type and parseable future expiry are present. A NULL expiry is accepted only for `type=lifetime`; unknown types and missing expiry fail closed.
- `LinkCompanyAsync` now treats same-owner relinking as idempotent and rejects a different Kaliia account with a transfer-required error rather than silently overwriting the company's local binding.
- `/api/license/link` maps that transfer-required condition to HTTP 409 with a stable error code.
- The optional API enforcement middleware now returns HTTP 401 for anonymous mutations under protected `/api` routes when enforcement is enabled, rather than allowing them to continue to endpoint handlers.
- Additional API tests cover explicit lifetime semantics, expired trial, unknown license type, subscription with NULL expiry, and prevention of unapproved account rebinding.
- A dedicated workflow `.github/workflows/zarr-license-tests.yml` runs ASP.NET API tests and Flutter tests only. It does not create Android/Windows release packages.

A GitHub Actions run was observed for the latest implementation commit (run ID 43814010455); at the time of this update the run was queued / the ASP.NET test job was starting. Results must be checked before treating these changes as validated.

### Remaining architectural blockers

- The current runtime `CanWriteAsync` still reads the legacy account-scoped `zurvan_licenses` row. It is not yet company-scoped and must not be treated as the final approved Zarr commercial model.
- The new company-level central binding/entitlement tables and trusted service-to-service contract have not been applied to Supabase. No live database schema or Edge Function was changed.
- The local owner-transfer protection is a temporary safety guard; the audited central transfer workflow and recovery process remain to be implemented.
- Signed entitlement issuance, offline grace validation, outbox authorization metadata/state transitions and cross-platform local mutation guards remain unimplemented.
- Do not enable `Kaliia:EnforceZarrLicense`, activate Zarr sales/plans, merge the implementation branch or issue a release until company-scoped entitlement migration, compatibility tests and security review pass.

No user-run app test is required yet. Request a real-device test only after the signed entitlement and offline transition flows are implemented and testable in an installed client.
