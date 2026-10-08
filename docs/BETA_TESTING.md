# ERP Accounting 0.5.0-beta.1

This build is a **controlled beta** intended for a small real-user pilot (roughly 5–20 users) with an identified administrator.

## Beta scope

The following workflows are in scope for beta validation:

- Company bootstrap, password login, persistent sessions and refresh-token rotation
- User/role administration and password reset
- Trusted-device listing and session revocation
- Double-entry accounting, journals, fiscal years/periods, closing/reopen and reversals
- Detail accounts, cost centers and accounting projects
- Multi-currency accounting and FX revaluation
- Products/services, warehouses, stock, moving-average valuation
- Sales invoices and sales returns
- Purchase orders, staged receipts and purchase returns
- Warehouse transfers, stock adjustments and Lot/Serial/Expiry tracking
- Cashbox/bank definitions, receipts, payments and internal transfers
- Unified Reports Center with explicit from/to date filters
- CSV export for spreadsheet use
- Serverless Demo mode
- Local SQLite backup/restore
- Server backup/restore scripts for SQLite and PostgreSQL
- Windows and Android CI build artifacts

## Explicitly outside this beta

These areas are not blockers for the controlled beta and should not be presented as completed:

- Cheque lifecycle and bank reconciliation
- Full AR/AP aging and due-date workflows
- Quotations/proforma/POS
- Full general offline synchronization for every ERP entity
- USB/LAN sync packages
- TOTP/passkeys/biometrics
- Fixed assets, CRM, HR and payroll
- Custom report designer
- Production installer/auto-update/store publishing

## Recommended beta deployment

1. Use PostgreSQL for a shared multi-user server.
2. Put the API behind HTTPS (reverse proxy or equivalent).
3. Do not expose a development SQLite server database to multiple remote users.
4. Run `scripts/backup_server.ps1` on a schedule and retain multiple generations.
5. Test `scripts/restore_server.ps1` on a non-production copy before the pilot starts.
6. Create individual user accounts; do not share the Owner password.
7. Give users the minimum role they need.
8. On each Windows/Android client, set the API URL from the login screen before first login.
9. Keep Demo mode isolated from real company data.

## Acceptance checklist

Before admitting a tester:

- [ ] API `/health` reports healthy.
- [ ] Administrator can log in and the session survives an app restart.
- [ ] A non-owner user can be created and assigned a role.
- [ ] Revoking a device invalidates its sessions.
- [ ] A sales invoice posts and creates a balanced accounting journal.
- [ ] A purchase order can be approved, partially received and completed.
- [ ] Sales and purchase returns update stock and accounting correctly.
- [ ] Receipt/payment/transfer operations create balanced treasury journals.
- [ ] Reports Center returns correct values for a selected from/to date range.
- [ ] A local client backup can be created and restored.
- [ ] A server backup has been created and a restore rehearsal has succeeded.
- [ ] Windows beta artifact launches.
- [ ] Android beta APK installs and launches on at least one physical device.

## Data-safety rule

Beta testers should use operational data only after backup and restore have both been rehearsed. Until a pilot passes the acceptance checklist, treat the environment as pre-production.
