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

## Integration and deployment blockers — NOT completed

**IMPORTANT: The above does not mean paid license enforcement is active in the accounting application.** The ASP.NET API has its *own* user/company authentication; it does not currently know which Supabase account owns a specific company. Do not use a client-provided Supabase UUID, company ID, or app metadata to authorize server writes. A verified, server-side link must be established.

1. Define ownership: a single Zarr subscription belongs to a verified account/company, not each device separately; confirm whether a legal/accounting entity can be licensed under multiple organizations.
2. Implement a safe account-to-company linking protocol, authenticated by the ASP.NET company owner AND Kaliia Supabase login. Store the verified binding in the server DB.
3. Enforce Zarr entitlement server-side for ALL mutating accounting, stock, invoice, bank and other APIs; central authorization should fail closed. Client button disabling alone is insufficient.
4. Gate the Flutter offline mutation outbox and local commands; keep reports, search, historical data, exports and backups available after expiry. Preserve blocked pending operations without treating them as committed.
5. Define a signed, bounded offline entitlement policy before allowing offline writes; no fresh 45-day period may be minted locally.
6. Validate Windows and Android account/refresh flows, 45-day exact boundary, paid renewal, revocation, network loss, company isolation and archive integrity with real user sessions.
7. Enable pricing and checkout for Zarr only after business rates, merchant bank account, manual-payment test and license activation end-to-end checks are confirmed.

## Safety properties

- Supabase service-role and secret keys MUST remain exclusively server side.
- Kaliia and local ERP sessions are independent until owner-verified linking.
- The license product ID `maal` must NEVER be used by the Zarr client.
- An expired or suspended licence must never trigger deletion of accounting records.
- Current app account setup and demo mode remain accessible during migration.

## Follow-on work

Implement owner-verified company linking; then server-side authorization before all write routes, and only then activate paid sales or claim a complete read-only enforcement policy.
