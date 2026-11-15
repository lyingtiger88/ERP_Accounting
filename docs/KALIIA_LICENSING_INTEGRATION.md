# Kaliia Soft — Zarr (زر) licensing integration

## Product identity

- **Commercial name:** زر (Zarr).
- **Canonical product ID:** `zarr`, completely separate from `maal`.
- **Technical repository:** `lyingtiger88/ERP_Accounting`, existing Flutter package `erp_accounting`, ASP.NET namespace `ERPAccounting.Api` — these technical identifiers are retained for compatibility, but end-user branding is Zarr.
- **Platforms:** Flutter Windows/Android + an ASP.NET Core server API, PostgreSQL server data, offline SQLite client cache.
- **Public website:** https://kaliia.ir.

## Implemented

- The central Supabase catalogue contains product `zarr` (enabled, sales disabled pending pricing and live checkout verification).
- Four price plan slots exist (1, 3, 6, 12 months), all disabled and unpriced. No pricing is invented.
- Supabase Edge Function `kaliia-entitlement` accepts `zarr` for `status` and `start_trial`; server RPC prevents a second trial for the same product and user, and a first trial lasts 45 days.
- The Flutter entitlement model/client supports the independent `zarr` product ID.
- `ZarrAccountService` signs into *Supabase Auth* using a separately stored, verified Kaliia account, rotates refresh tokens, and requests `zarr` status/trial. The ERP API's proprietary bearer token is **never** passed to Supabase.
- `ZarrLicensePage`, linked from Settings → About, allows the user to sign into the Kaliia account, check Zarr's entitlement, explicitly start the 45-day trial and sign out.
- End-user branding in the Flutter dashboard, login and About pages is Zarr. The website and account registration link are shown.

## Owner-verified company linking and server-side enforcement (implemented, rollout-gated)

**Important:** The ASP.NET backend now contains an owner-verification endpoint and optional central read-only middleware. **Commercial enforcement is OFF by default** to avoid breaking existing ERP users before identity linking, configuration and end-to-end tests are completed.

- `POST /api/license/link`: only the ERP company Owner can link a Supabase account, providing the ERP bearer token and a short-lived Supabase access token. The server verifies the token with Supabase Auth and stores only the verified Supabase UUID (no Supabase password or token saved in ERP).
- `GET /api/license/status`: returns company link and current server entitlement status.
- The mapping is stored in `zarr_company_licenses`, bootstrapped for SQLite or PostgreSQL. One company gets one account-level Zarr license shared across its devices.
- `ZarrLicenseService` checks the company owner link, active central account, active `zarr` license, and UTC expiry. An unlinked company, invalid status, missing server secret or network failure fails closed.
- Central ASP.NET middleware can block non-GET `/api` writes with `403 zarr_subscription_read_only`, while allowing historical reads and reports, when `Kaliia:EnforceZarrLicense=true`. Auth and linking routes remain available for renewal/recovery.
- The Flutter Zarr account page includes the verified owner linking action, Zarr license status/trial and a server-side company entitlement check.
- **The Flutter offline write/outbox path is not yet fully gated.** This is a separate critical step before enabling commercial enforcement.

### Server-only configuration

Set the server environment variables (never check a secret into Git):
- `Kaliia__ServiceRoleKey`: secret Supabase service-role key; on the ASP.NET server only.
- `Kaliia__EnforceZarrLicense=true`: enable only AFTER a real owner completes linking and writes are verified blocked/allowed correctly.
- `Kaliia__SupabaseUrl`: optional override of the Supabase project URL.
- `Kaliia__PublishableKey`: optional override of the public key.

Only enable enforcement when the API is reachable over protected HTTPS, all tenant owners have migration instructions, approved subscriptions and trial entitlements have been tested, and the offline client policy is ready.

### Remaining for commercial launch

1. Add a robust offline-outbox write guard and an explicit server-verified offline grace policy (if any); never use the device clock to reset a trial.
2. Test the full ERP Owner ⇄ verified Kaliia account mapping, expiry and manual payment activation with real login sessions.
3. Confirm 45-day exact boundary, company isolation, renewal, suspension and data preservation.
4. Complete Windows and Android production signing, back-ups, real device tests and documented rollout.
5. Enable Zarr prices and checkout only after the accounting API's commercial enforcement is tested.

## Safety properties

- Supabase service-role and secret keys MUST remain exclusively server side.
- Kaliia and local ERP sessions are independent until owner-verified linking.
- The license product ID `maal` must NEVER be used by the Zarr client.
- An expired or suspended licence must never trigger deletion of accounting records.
- Current app account setup and demo mode remain accessible during migration.

## Follow-on work

Finish offline write guards, test verified company linking and online enforcement, then activate commercial pricing and the server enforcement flag only when rollout checks pass.
