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
- [x] Accounting periods
- [x] Floating detail accounts
- [x] Journal entries with double-entry validation
- [x] Offline journal-entry UI
- [x] Local Draft / Pending Sync journal storage
- [x] General ledger with opening/running balances
- [x] Trial balance with opening/closing balances
- [x] Fiscal year close/reopen enforcement
- [x] Fiscal period close/reopen enforcement
- [x] Immutable journal reversal workflow
- [x] Profit & loss
- [x] Balance sheet
- [x] Floating-detail ledger
- [x] Multi-currency accounting
- [x] International currency definitions / base currency
- [x] Historical FX buy/sell/accounting rates
- [x] Foreign-currency journal posting
- [x] Currency position and revaluation report
- [x] FX revaluation journal posting
- [x] Accounting audit log
- [x] PostgreSQL persistence
- [x] Initial SQLite client persistence layer

## Phase 2 — Sync
- [ ] Device identity
- [x] Outbox/change-journal schema foundation
- [x] Accounting documents enqueue transactionally into Outbox
- [x] Outbox push/change-journal processing
- [x] Idempotent accounting journal upload
- [x] Server-to-client journal pull / sync cursors
- [x] Journal delta push/pull
- [x] Detail-account delta pull cursor
- [ ] General delta upload/download
- [x] Detail-account optimistic revisions
- [x] Detail-account conflict detection and interactive resolution
- [ ] General conflict policies
- [x] Bidirectional accounting journal API synchronization
- [ ] General online API synchronization
- [ ] USB export/import package
- [ ] Encryption and signature verification
- [ ] Sync health diagnostics

## Phase 3 — Commercial Operations
- [ ] Customers and suppliers
- [x] Sales invoices
- [ ] Purchase invoices
- [ ] Payments and receipts
- [ ] Cash and bank accounts
- [ ] Cheques
- [x] Sales invoice tax and discounts

## Phase 4 — Inventory & Resources
- [x] Products and services
- [x] Real-time stock balances
- [x] Moving-average inventory valuation
- [x] Multi-warehouse inventory
- [x] Sales issues and manual stock movements
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
- [x] Initial revision/conflict automated tests
- [ ] Broader automated test coverage
- [ ] Backup / restore
- [ ] Disaster recovery
- [ ] Security review
- [ ] Windows installer
- [ ] Android release build
- [ ] Store-ready branding and metadata
