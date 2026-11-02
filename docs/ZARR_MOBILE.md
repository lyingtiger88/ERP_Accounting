# Zarr (زر) — Android and iOS foundations

## Two install modes (same app package)
- **Standalone**: local company and SQLite as source of truth; no API required; local accounting/stock engine is reused from existing Demo business engine. Production standalone company provisioning and removal of the Demo 500-document cap are **not implemented yet**.
- **Companion**: secondary authorized device for a company hosted by the Windows/main server (or an explicitly designated remote server). Local SQLite caches approved data and queues eligible offline drafts. Desktop is preferential master **only by explicit pairing/server authority**, not simply because it is online.
- Both modes share UI/domain models but use separate namespaced company data. Never blend demo, standalone, and companion datasets.

## Mode transition safety
- Store device mode under local metadata key `zarr.mobile.configuration.v1`.
- Switching from standalone to companion **never** uploads local journals automatically. Require company identity matching, explicit user confirmation, full backup and a reviewed one-time migration/import.
- No automatic downgrade from Companion to Standalone while pending sync outbox items exist.
- Financial records are immutable after posting; corrections use audit-tracked reversal.

## Pairing and sync acceptance criteria (planned, NOT implemented)
1. Desktop/server displays short-lived, one-use QR containing only pairing challenge identifier and HTTPS server discovery (no access/refresh tokens).
2. Phone authenticates as a named user and submits challenge; Owner approves with role scope and device ID.
3. Persist device credentials in OS secure storage, rotate tokens, revoke from server, expire challenges.
4. Send idempotent change IDs with per-company revisions, deterministic conflict resolution and audit log.
5. Online/LAN HTTPS sync and offline outbox retry with bounds; no plain HTTP on physical devices.
6. Server-side authorization validates tenant, permission, trusted device, and document state on every mutation.
7. Restore/backup rehearsal and iOS/Android end-to-end tests precede real-financial-data pilot.

## iOS bring-up
- The Flutter client uses `sqflite`, `path_provider`, `flutter_secure_storage`: packages support iOS but **native integration remains unvalidated**.
- Use macOS runner and Xcode to generate iOS runner using `flutter create . --platforms=ios --project-name erp_accounting`.
- Build unsigned simulator smoke artifact in CI. A distributable IPA requires Apple signing credentials, real-device tests, app IDs, provisioning profiles and entitlements.
- Check iOS Keychain behavior, SQLite database persistence, backup location, safe area, right-to-left screens, and background/foreground token restore.

## Branding
- Product is **Zarr / زر**. Approved gold-on-navy icon must be exported into Android adaptive icon layers, iOS AppIcon asset catalog and Windows executable icon. Do not use a different placeholder as if it were the approved asset.
- The approved image is held in the design conversation; binary repository assets and native app icon integration are not yet committed.

## Milestone status
- [x] Android Flutter app foundation and CI artifact (existing)
- [x] Offline SQLite, Demo engine, and existing server API (existing)
- [x] Mode model, local mode configuration and unit tests (this branch)
- [x] Initial iOS CI smoke workflow (this branch)
- [ ] Mode selection and live standalone onboarding UI
- [ ] Production standalone accounting provision
- [ ] Companion pairing and sync
- [ ] Approved icon native exports and install-time integration
- [ ] iOS device validation and signed IPA
- [ ] Android and iOS release/acceptance tests
