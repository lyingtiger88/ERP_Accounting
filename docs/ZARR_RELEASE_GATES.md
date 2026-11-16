# Zarr release gates and fast-track distribution plan

Last updated: 2026-11-16. This checklist describes the current implementation branch; it is not a release approval.

## Strategy

Ship in two phases rather than enabling an incomplete commercial license:
1. Private distributor/QA beta: signed binaries only after explicit build approval, existing commercial enforcement OFF, no real subscription purchase, test/demo data preferred.
2. Commercial release: only after company-scoped entitlements, secure offline grace, payment approval, expiry behavior and data recovery pass cross-platform E2E tests.

Do not weaken read/export/backup rights after expiration.

## Implemented on the isolated development branch

- Product/account policy: a Kaliia account can register multiple products; one account can own multiple Zarr companies; each company has its own entitlement.
- Central company binding, company entitlement, trial RPC, audit log and annual payment review schema, applied additively in Supabase with RLS and service-role-only execution.
- Zarr API owner-verification and feature-flagged company entitlement path.
- Owner-triggered company trial endpoint and Flutter license UI connection to the ERP API.
- Restriction on silent company owner rebinds; explicit lifetime semantics and fail-closed API middleware for anonymous mutations.
- Offline outbox server-status preflight when replaying queued writes; denied payloads are preserved.
- Test-only GitHub workflow for ASP.NET and Flutter. The repository's broad CI workflow is now gated on the implementation branch to avoid packaging on PRs.

## RS256 offline entitlement work (implementation in progress)

- The API can issue an RS256-signed compact token bound to product `zarr`, one ERP company, entitlement generation and the entitlement expiry.
- Offline token lifetime is capped at 7 days and is also capped by the actual license expiry.
- Flutter verifies RS256 using a pinned RSA-3072 public key and persists only verified tokens using platform secure storage.
- Automated tests include a signed fixture, company mismatch, tampered signature and expired grace; ASP.NET tests verify server signing.

### Key provisioning and production gate

- The public modulus currently embedded in Flutter is a **test-fixture verification key**, not a production key. The corresponding private key is not committed to the repository.
- The ASP.NET API now requires both `Kaliia:EntitlementPrivateKeyPem` and an explicit `Kaliia:EntitlementKeyId`; it does not silently fall back to a default key id.
- The status endpoint returns no offline token unless both settings are present.
- Since no accounting API host exists yet, do not generate or configure a production signing key now. When hosting is approved, generate a fresh RSA-3072 key pair in the host/secret manager, assign a new production key ID, add only the public modulus and key ID to the client in a reviewed release, and store the private PEM only in the host secret store.
- Never commit production private keys, include them in Flutter assets, or paste them into chat/CI logs. Key rotation requires a client release containing the next public key before the server begins signing with its key ID.

- The client records the server enforcement decision per ERP company in secure storage before entering the dashboard. Unknown status is fail-closed for local business writes.
- The local database guards the main user-originated draft, journal, detail-account and store-draft mutation paths. Backup, restore, export and server cache refresh paths remain available.
- The client stores the greatest observed device UTC time in platform secure storage and denies offline writes if the clock moves backwards by more than five minutes. This is a practical rollback-detection layer, not a hardware-backed monotonic clock guarantee.
- Independent review of every local mutation path remains a release blocker. The guard must be extended if new local write paths are added.

## Still blocking commercial release

1. Signed offline entitlement, 7-day limit, clock rollback / reboot protection, public-key rotation and secure storage.
2. Local mutation guarding for every repository, draft, import and bulk operation (outbox replay preflight alone is not enough).
3. Fully wired company payment submission and receipt validation, admin review UI and company-specific renewal E2E.
4. Verified migration of legacy account-scoped Zarr trials to company entitlements without resetting the trial.
5. Complete tests for owner transfer/recovery, company isolation, expiry read-only, backup/export and offline reconnection.
6. Explicit activation of company mode and subsequently license enforcement after pilot migration.
7. Signed release packages and on-device smoke tests on Windows/Android, only after user approval to build.

## Live Supabase audit (2026-11-16)

- Current company bindings: 0.
- Current company entitlements: 0.
- Current company payment requests: 0.
- Legacy `zurvan_licenses` rows for product `zarr`: 0.
- The existing `zurvan_admin_users` and `zurvan_products` columns referenced by the payment approval RPC were verified to exist.

No customer license or trial rows required migration at audit time. This is a count-only audit; it does not prove there are no Zarr installations with locally cached state.

## Immediate user testing checkpoint

No device test is required until a test build is explicitly approved and produced. At that stage:
- Sign in with an ERP Owner account and one verified Kaliia account.
- Link two separate ERP companies to that one account.
- Start trial on one test company; verify the other remains unstarted.
- Verify product registrations for other Kaliia software remain unchanged.
- Simulate license expiry with controlled server test data (never manipulate production customer records).
- Create a pending offline draft, reconnect and verify synchronization/denial without data loss.
- Export/backup data in read-only mode.

Never ask users to share passwords, tokens, payment credentials or service-role keys in chat.

## Release safety

- Current flags: Kaliia:UseCompanyEntitlements=false, Kaliia:EnforceZarrLicense=false.
- Do not switch either flag on for real tenants until legacy binding audit, central migration and tests pass.
- Company payments and sales plans remain disabled until business pricing is approved.
- No manual app build or distribution is authorized by the user.
