# Roadmap

## Phase 0 — Foundation
- [x] Repository initialization
- [x] Architecture baseline
- [x] Windows + Android target definition
- [x] Online + USB synchronization design direction
- [x] Optional QR device pairing requirement
- [x] MFA / 2FA requirement
- [x] Create Flutter client workspace
- [x] Create ASP.NET Core server workspace
- [x] Establish CI
- [x] Define multi-provider database migration strategy

## Phase 1 — Identity, Company & Accounting Foundation
- [x] Company / organization bootstrap
- [x] Users and role model
- [x] Password authentication
- [ ] Persistent authorization / refresh-token model
- [ ] TOTP MFA
- [ ] Backup codes
- [ ] Trusted-device management UI/API
- [ ] Optional QR pairing
- [x] Chart of accounts
- [x] Iranian Persian base chart + hierarchy metadata
- [x] Account nature and control/postable rules
- [x] Solar Hijri journal numbering
- [x] Configurable fiscal years
- [ ] Accounting periods
- [x] Floating detail accounts
- [x] Journal entries with double-entry validation
- [x] Offline journal-entry UI
- [x] Local Draft / Pending Sync journal storage
- [x] General ledger (initial)
- [x] Trial balance (initial)
- [x] Fiscal year close/reopen enforcement
- [ ] Audit log
- [x] PostgreSQL persistence
- [x] Initial SQLite client persistence layer

## Phase 2 — Sync
- [ ] Device identity
- [x] Outbox/change-journal schema foundation
- [x] Accounting documents enqueue transactionally into Outbox
- [ ] Outbox push/change-journal processing
- [ ] Delta upload/download
- [ ] Conflict policies
- [ ] Online API synchronization
- [ ] USB export/import package
- [ ] Encryption and signature verification
- [ ] Sync health diagnostics

## Phase 3 — Commercial Operations
- [ ] Customers and suppliers
- [ ] Sales invoices
- [ ] Purchase invoices
- [ ] Payments and receipts
- [ ] Cash and bank accounts
- [ ] Cheques
- [ ] Tax and discounts

## Phase 4 — Inventory & Resources
- [ ] Multi-warehouse inventory
- [ ] Stock movements
- [ ] Transfers
- [ ] Barcode / QR support
- [ ] Batch / serial tracking
- [ ] Expiry tracking
- [ ] Fixed assets
- [ ] Depreciation
- [ ] Equipment / resource assignment

## Phase 5 — Management Layer
- [ ] CRM
- [ ] Projects
- [ ] Cost centers
- [ ] HR
- [ ] Dashboards
- [ ] Custom reports
- [ ] Approval workflows

## Phase 6 — Hardening & Distribution
- [ ] Automated tests
- [ ] Backup / restore
- [ ] Disaster recovery
- [ ] Security review
- [ ] Windows installer
- [ ] Android release build
- [ ] Store-ready branding and metadata
