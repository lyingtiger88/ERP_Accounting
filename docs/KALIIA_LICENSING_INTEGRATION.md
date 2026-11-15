# Kaliia Soft licensing integration — Maal (repository mapping pending confirmation)

Candidate product ID: `maal`. This repository is a cross-platform Flutter Windows/Android accounting and resource-management client with an ASP.NET Core API, PostgreSQL server and local SQLite/offline-first workflows. Its README currently calls the project ERP_Accounting and says the final product brand is not selected; confirm that this is the Maal product before enabling commercial entitlements for real customers.

## Central entitlement API
Supabase project `yztithydgdvydaktuxlw`; POST `https://yztithydgdvydaktuxlw.supabase.co/functions/v1/kaliia-entitlement` with `{"action":"status","product_id":"maal"}` or `{"action":"start_trial","product_id":"maal"}`. Require a verified Supabase Auth access token and publishable key. Never embed the service-role key.

## Required architecture
1. Add a Supabase identity-link table mapping the existing ERP tenant/user identity to a verified Supabase Auth UUID. Do not equate an ERP local user ID with auth.users.id.
2. Keep tenant licensing separate from user roles: choose and persist the licensed tenant/company scope, and define whether one subscription covers one company or multiple companies before launch.
3. Add a shared Flutter entitlement service injected with the existing authenticated token provider. Refresh on sign-in, startup, resume and every 12 hours while online.
4. Add an immutable entitlement snapshot with server timestamp, expiry, product ID and tenant ID; protect it using Windows DPAPI / Android Keystore. Never extend a trial from the client clock.
5. Enforce read-only in application/domain command handlers and server API authorization for every mutation, not only in Flutter widgets. The ASP.NET API must independently validate tenant entitlement before accepting writes; offline outbox must stop enqueueing new mutations when entitlement expires.
6. Preserve local SQLite data, reports, export, backup and read/search after expiry. Do not discard pending outbox data; retain it and clearly mark it blocked pending renewal.
7. A manually approved payment updates the central license for product `maal`; client refresh restores writes without reinstalling.

## Unresolved commercial decisions
- Confirm this repository is the Kaliia Soft Maal product (README currently labels it ERP_Accounting).
- Decide whether the billable unit is a user, device, or company/tenant.
- Decide seat/device limits and offline grace period.
- Map the ERP identity and tenant model to Supabase Auth before activating trial creation.

## Acceptance tests
Trial boundary at 45 days; separate entitlements for `maal` and other products; tenant isolation; paid renewal; revoked and suspended read-only; offline expiry/outbox behavior; server-side write rejection; preserved reports and local data; no privileged Supabase key in Flutter or ASP.NET client configuration.


## Important authentication compatibility note
The existing Maal Flutter app authenticates against its own ASP.NET API and stores that API's custom access token in `SecureSessionStore`. That token is **not** a Supabase Auth JWT and must not be sent to `kaliia-entitlement`. The Flutter entitlement client intentionally requires a token provider but is not wired into the current login flow. Before enabling trial or write enforcement, implement a server-side tenant-to-Kaliia-account identity bridge (with credentials held only by the API/server), define company-level subscription ownership, and have the API enforce entitlements on every mutation. The client module alone does not activate or enforce a Maal subscription.
