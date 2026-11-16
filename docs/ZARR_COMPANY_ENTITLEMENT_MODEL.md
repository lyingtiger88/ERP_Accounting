# Zarr company-scoped entitlement model

Status: DESIGN PROPOSAL ONLY. This document does not apply a migration or change the live Supabase project.

## 1. Decision

Zarr is licensed per ERP company. One Kaliia Auth account may administer more than one ERP company, but each company has its own independent Zarr entitlement, trial history, subscription expiry and payment history. A company may have only one current Kaliia billing owner at a time. A company ID is never accepted as proof of ownership from a client; only the authenticated Zarr ASP.NET API can create or change a binding after validating the ERP Owner session and Kaliia access token.

Keep the existing public.zurvan_licenses account/product model unchanged for Zurvan, Maal, ShopManager and other existing products. Zarr's company scope must not be emulated by reusing a Kaliia user's account-level license.

## 2. Proposed central tables

### zarr_company_bindings

One canonical row per ERP company.

| Column | Type | Purpose |
|---|---|---|
| company_id | uuid PK | Stable ERP company ID |
| product_id | text | Fixed to zarr |
| auth_user_id | uuid FK auth.users | Current verified Kaliia billing owner |
| state | text | active, transfer_pending, suspended |
| linked_at | timestamptz | Server timestamp |
| linked_by_erp_user_id | uuid | ERP Owner ID recorded by API |
| generation | bigint | Incremented on a completed owner transfer |
| created_at / updated_at | timestamptz | Audit timestamps |

Constraints:
- Unique (company_id, product_id).
- A company has one active billing owner per product.
- An Auth account may bind multiple ERP companies; each company is billed independently.
- Re-linking to the same Auth account is idempotent.
- Replacing an existing Auth account is forbidden through ordinary link. It requires an explicit transfer workflow with current-owner approval or an audited Kaliia support recovery procedure.
- Keep an append-only transfer/audit event record; never silently overwrite the prior owner.

### zarr_company_entitlements

One canonical commercial entitlement per ERP company/product.

| Column | Type | Purpose |
|---|---|---|
| company_id | uuid | FK zarr_company_bindings |
| product_id | text | Fixed to zarr |
| entitlement_type | text | trial, subscription, lifetime |
| status | text | active, suspended, revoked |
| started_at | timestamptz | Server start time |
| expires_at | timestamptz nullable | Required for trial/subscription; null only for lifetime |
| trial_started_at | timestamptz nullable | Immutable first trial start |
| trial_expires_at | timestamptz nullable | Immutable 45-day trial boundary |
| generation | bigint | Incremented on each entitlement change |
| source_payment_id | uuid nullable | Approved manual payment |
| created_at / updated_at | timestamptz | Audit timestamps |

Constraints:
- Primary/unique key (company_id, product_id).
- Trial fields are set once and cannot be reset by relinking, account transfer, device replacement or reinstall.
- type=trial/subscription requires expires_at; type=lifetime requires expires_at IS NULL.
- status is limited to active/suspended/revoked.
- A trial is available only if trial_started_at is null; expired/revoked trials remain recorded permanently.
- A paid subscription supersedes a trial. Renewal extends from greatest(current expiry, server now), and increments generation.
- Suspension/revocation overrides an otherwise unexpired term.
- All state changes append an audit event with actor, source, prior state, new state and timestamp.

### zarr_entitlement_audit

Append-only audit history for link, transfer, trial start, payment approval, renewal, suspension, revocation, device enrollment and recovery. Restrict mutation to trusted server-side roles and approved RPCs.

### zarr_manual_payments

Extend the current manual-payment workflow with a nullable company_id for backward compatibility. For product_id='zarr', company_id is mandatory and must match an active zarr_company_bindings row owned by the authenticated Kaliia account. For other products, preserve current account-level semantics. Payment approval must update zarr_company_entitlements for Zarr and zurvan_licenses for legacy account-scoped products.

## 3. RPC / service contract

Expose narrowly scoped, SECURITY DEFINER RPCs only to service_role, with empty search_path, fully qualified object references, strict validation, and explicit grants. End users must not call these RPCs directly.

- zarr_bind_company(company_id, auth_user_id, erp_owner_id, request_id): creates first binding; same-owner retry is idempotent; different-owner replacement returns transfer_required.
- zarr_begin_company_transfer(company_id, current_auth_user_id, new_auth_user_id, request_id): records a pending transfer; does not change ownership immediately.
- zarr_complete_company_transfer(transfer_id, proof): completes only after current-owner approval or audited support recovery; increments binding generation and preserves entitlement/trial history.
- zarr_company_entitlement(company_id): returns binding and effective entitlement state, with server UTC and policy version.
- zarr_start_company_trial(company_id, auth_user_id, erp_owner_id, request_id): verifies active binding, creates exactly one 45-day trial if unused, otherwise returns current entitlement. It must not accept an arbitrary company/user pairing.
- zarr_issue_entitlement(company_id, device_id, policy_version): returns a bounded entitlement payload for signing by the trusted service.
- zarr_approve_company_payment(payment_id, admin_id, request_id): locks the payment, validates pending state and plan snapshot, then atomically updates the company entitlement and payment review state.

Use a stable request/idempotency key for every write RPC. Keep the existing generic kaliia_start_trial and zurvan_manual_review_payment behavior for non-Zarr products; add explicit Zarr branches or new RPCs rather than silently changing their semantics.

## 4. Cross-system trust flow

1. Client signs into the local ERP account and Kaliia Auth independently.
2. Client sends both bearer tokens to the Zarr API link endpoint.
3. Zarr API verifies the ERP token and requires role Owner; it verifies the Kaliia access token against Supabase Auth and checks confirmed email.
4. Zarr API calls the trusted central binding RPC using server-only credentials. The client never supplies a trusted company-owner assertion or service credential.
5. Trial start, entitlement status and payment submission are company-scoped. The API verifies the same current company binding before returning write permission.
6. The API is the authoritative gate for online ERP writes. The client receives a signed entitlement document for local UX and offline grace.
7. On owner transfer, old sessions/tokens cannot rebind the company. New ownership requires the explicit transfer/recovery workflow.

The central binding table is the source of truth. A local ERP database cache may be kept for performance, but it must never authorize access by itself and must be reconcilable against the central binding.

## 5. Signed offline entitlement

Use a compact JWS with ES256 (ECDSA P-256/SHA-256) and a key ID (kid). The private signing key exists only in a protected server-side secret store. Clients embed or securely update public verification keys; key rotation supports overlapping old/new public keys.

Required claims:
- iss: Kaliia licensing issuer
- aud: Zarr client / API audience
- product: zarr
- company_id: exact ERP company binding
- entitlement_id and generation
- status and entitlement_type
- iat, nbf, exp: token validity
- license_expires_at: actual trial/subscription/lifetime boundary
- offline_until: min(iat + 7 days, license_expires_at), with no extension beyond commercial expiry
- policy_version and kid

The client verifies signature, issuer, audience, product, company, generation, nbf, exp and offline_until. Unknown policy versions, missing claims, invalid signature, company mismatch, revoked/suspended state or clock rollback cause read-only mode and an online revalidation requirement.

A signed document is not proof that a local operation has been accepted by the server. Offline changes remain pending in the outbox until server revalidation and successful idempotent application.

Clock rollback is a hard platform limitation on ordinary PCs/phones. Persist last trusted server time and monotonic/boot evidence using platform-protected storage. If a reboot or restore makes elapsed time unverifiable, require online revalidation rather than extending offline grace. Never claim perfect anti-tamper time on a device without a trusted hardware clock/attestation source.

## 6. Payment behavior

- Zarr manual payment is available only after company binding exists.
- The payment record snapshots company ID, product, plan duration, amount, currency, payer Auth ID, receipt metadata and request ID.
- Admin approval locks the pending payment and applies the subscription to that company, not every company owned by the same Kaliia account.
- Renewal extends from the later of current expiry or server now; trial conversion preserves the existing trial expiry before adding the paid duration.
- Rejection/refund/suspension rules are explicit and audited. A refund must not silently delete historical accounting data.
- Do not enable Zarr sales or plans until the owner supplies and approves actual prices, currencies and merchant configuration.

## 7. Migration and compatibility

- Add the company binding/entitlement/audit schema without altering existing account-level licenses for other products.
- Keep the existing local zarr_company_licenses table only as a transitional cache; migrate its mappings after verifying each ERP company Owner and Kaliia account. Resolve conflicting or missing links manually; never infer ownership from email or company name.
- Existing Zarr trial rows in zurvan_licenses cannot be assumed to represent a company trial. Inventory them and create an explicit, audited one-time mapping decision before any migration.
- Do not automatically grant a fresh 45-day trial to existing accounts. Preserve their current paid/trial state and obtain a verified company association.
- Do not enable enforcement globally until every existing company has a verified binding or an approved migration exception and a rollback plan.

## 8. Required tests

- Two ERP companies bound to one Kaliia account have separate entitlements and independent expiry/payment history.
- One ERP company cannot be linked to two Kaliia accounts concurrently.
- Same-owner relink is idempotent; different-owner relink is rejected until transfer/recovery completes.
- Trial is one-time per company/product and survives reinstall, device replacement, owner transfer, unlink/relink and API database restore.
- A Kaliia account cannot request entitlement for another company's UUID.
- Zarr payment approval updates only the selected company; Maal/ShopManager/Zurvan account-level behavior remains unchanged.
- Subscription renewal, trial conversion, lifetime, suspension, revocation and refund are deterministic.
- Signed entitlement fails on tampering, wrong company, wrong product, unknown kid/policy, expiry, offline-until, generation rollback and untrusted clock.
- API writes are blocked on every mutation route; read/export/backup remains available.
- Offline outbox items remain pending and recoverable after expiry and are never silently accepted after expiry.
