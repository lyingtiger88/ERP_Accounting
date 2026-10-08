# ERP Accounting

Cross-platform accounting and resource-management platform for Windows and Android.

> **Working title:** `ERP_Accounting` — final product/brand name will be selected later.

## Offline Demo Mode

The Flutter client includes a serverless local demo mode from the login screen.
It seeds an isolated demo company in SQLite with sample accounts, a 1405 fiscal
year, detail accounts, cost centers, projects, products, warehouses and stock.
Demo API calls are short-circuited locally, demo documents never enter the sync
outbox, and the combined local accounting/store document limit is 500.

A local business engine now executes demo operations without the ASP.NET API:
sales posting decreases stock, calculates weighted-average COGS and creates the
balanced accounting journal; purchase posting increases stock, updates weighted
average inventory cost and creates the purchase/tax/settlement journal; sales
returns restore inventory and reverse revenue/tax/COGS; warehouse transfers keep
the source cost; and manual stock adjustments update the local stock ledger.

## Project Roadmap

This roadmap is intentionally kept on the repository home page so the current plan and progress are always visible.

| Phase | Focus | Status |
|---|---|---|
| **Phase 0** | Project foundation, architecture, Windows + Android targets, CI, online/USB sync design | 🟡 Mostly complete |
| **Phase 1** | Identity, companies, users, roles, accounting foundation, 2FA, QR pairing | 🟡 In progress |
| **Phase 2** | Offline-first sync engine, online sync, USB delta sync, conflict handling | 🟡 In progress |
| **Phase 3** | Sales, purchasing, customers, suppliers, cash/bank, cheques, tax | 🟡 In progress |
| **Phase 4** | Inventory, warehouses, resources, assets, barcode/QR, serial/batch tracking | 🟡 In progress |
| **Phase 5** | CRM, HR, projects, cost centers, management dashboards and workflows | ⚪ Planned |
| **Phase 6** | Backup, recovery, security hardening, installers, Android release, store readiness | ⚪ Planned |

### Phase 0 — Foundation

- [x] Repository initialization
- [x] Architecture baseline
- [x] Windows + Android target definition
- [x] Flutter client workspace
- [x] ASP.NET Core server workspace
- [x] CI pipeline
- [x] Online synchronization architecture
- [x] USB synchronization architecture
- [x] QR pairing security design
- [x] MFA / 2FA security design
- [x] PostgreSQL migration strategy
- [x] SQLite local database strategy

### Phase 1 — Identity + Accounting Core

#### Identity & Security
- [x] Company bootstrap
- [x] User model
- [x] Role model
- [x] Password hashing
- [x] Login API
- [x] Client login connected to API
- [x] Trusted-device domain model
- [x] Persistent sessions + rotating refresh tokens
- [x] User/role administration screens
- [ ] TOTP 2FA
- [ ] Backup recovery codes
- [x] Trusted-device management + session revocation
- [ ] Optional QR device pairing
- [ ] Step-up authentication for sensitive operations
- [x] Accounting audit log
- [x] Security audit log
- [ ] Passkey / Windows Hello / Android biometrics

#### Accounting
- [x] Initial chart of accounts
- [x] Account creation API
- [x] Double-entry journal model
- [x] Debit/Credit validation
- [x] Initial general ledger
- [x] Initial trial balance
- [x] Hierarchical account levels + Iranian Persian base chart
- [x] Debit/Credit account nature metadata
- [x] Control vs postable account enforcement
- [x] Solar Hijri journal numbering
- [x] Configurable fiscal years
- [x] Accounting periods
- [x] Client-side Draft / Pending Sync journal workflow
- [x] Server-side post / reverse journal workflow
- [ ] Server-side Draft workflow
- [x] Floating detail accounts (تفصیلی شناور)
- [ ] Opening balances
- [x] Fiscal year close/reopen enforcement
- [x] Profit & loss
- [x] Balance sheet
- [x] Account and floating-detail turnover reports
- [x] Multi-currency accounting
- [x] Currency definitions and base-currency control
- [x] Historical buy / sell / accounting exchange rates
- [x] Foreign-currency journal posting with preserved original amounts
- [x] Currency position and revaluation-difference reporting
- [x] FX revaluation journal posting against gain/loss accounts

#### Persistence
- [x] PostgreSQL server database
- [x] Entity Framework Core
- [ ] Database migrations
- [x] Shared client SQLite storage layer (Windows/Android)
- [x] Windows offline cache validation path
- [ ] Android device validation
- [x] Secure local credential storage

### Phase 2 — Synchronization Engine

- [ ] Device identity
- [x] Client sync outbox/change-journal foundation
- [x] Outbox push processor
- [x] Detail-account optimistic revisions
- [ ] General entity revisions
- [x] Journal sync cursors
- [x] Journal delta push/pull
- [ ] General delta upload/download
- [x] Idempotent accounting journal upload
- [ ] General idempotent change processing
- [x] Detail-account conflict detection
- [x] Interactive detail-account conflict resolution
- [ ] General conflict resolution policies
- [x] Bidirectional accounting journal synchronization
- [ ] General online synchronization
- [ ] LAN synchronization
- [ ] USB `.erp-sync` export/import
- [ ] Package encryption
- [ ] Package signatures
- [ ] Replay protection
- [ ] Attachment synchronization
- [ ] Sync diagnostics and history
- [x] Detail-account conflict-resolution UI
- [ ] General conflict-resolution UI

### Phase 3 — Commercial & Treasury

- [ ] Customers
- [ ] Suppliers
- [ ] Quotations / proforma invoices
- [x] Sales invoices
- [x] Sales returns
- [ ] Purchase invoices
- [x] Purchase returns
- [x] Sales-line discounts
- [x] Sales invoice tax amounts + accounting mapping
- [x] Receipts
- [x] Payments
- [x] Cashboxes
- [x] Bank accounts
- [x] Transfers
- [ ] Cheques
- [ ] Due-date tracking
- [ ] Accounts receivable
- [ ] Accounts payable
- [ ] Bank reconciliation

### Phase 4 — Inventory & Resource Management

- [x] Products and services
- [x] Multiple warehouses
- [x] Inventory issues + manual stock adjustments
- [x] Warehouse transfers
- [x] Real-time stock balances
- [x] Minimum stock alerts
- [x] Barcode support
- [ ] QR support
- [x] Serial numbers
- [x] Batch/lot tracking
- [x] Expiration dates
- [x] Moving-average stock valuation for sales
- [ ] Fixed assets
- [ ] Depreciation
- [ ] Equipment
- [ ] Vehicles / machinery
- [ ] Resource assignment and location history

### Phase 5 — Management Layer

- [ ] CRM
- [ ] Customer interaction history
- [ ] Contracts
- [x] Projects
- [x] Cost centers
- [ ] Branches / organizational units
- [ ] HR employee records
- [ ] Attendance
- [ ] Leave
- [ ] Payroll foundation
- [ ] Management dashboard
- [x] KPI widgets
- [x] Unified date-range reporting center
- [x] Sales / purchase / product profitability reports
- [x] Inventory movement and valuation reports
- [x] Customer / supplier commercial-flow reports
- [x] Treasury receipt / payment / transfer reports
- [x] Cost-center / project dimension reports
- [x] CSV export compatible with Excel
- [ ] Custom report designer
- [ ] Approval workflows
- [ ] Notifications
- [ ] Role-based dashboards

## Current release stage

**0.5.0-beta.1 — Controlled Beta Candidate**

The current target is a small real-user pilot (approximately 5–20 users), not unrestricted production deployment. The beta scope, known exclusions and acceptance checklist are documented in `docs/BETA_TESTING.md`.

Key beta hardening now implemented:
- persistent hashed server sessions with rotating refresh tokens
- OS secure token storage on the client
- login/refresh rate limiting
- user/role administration with last-Owner protection
- trusted-device session revocation
- security audit trail
- configurable API endpoint before login
- local SQLite backup/restore with integrity validation and safety copy
- SQLite/PostgreSQL server backup/restore scripts
- CI smoke builds for Windows and Android artifacts

### Phase 6 — Production, Distribution & Hardening

- [x] Initial automated sync/revision tests
- [ ] Broader unit-test coverage
- [ ] Integration tests
- [ ] End-to-end tests
- [x] Local backup/restore + server backup/restore tooling
- [ ] Disaster recovery
- [ ] Security review
- [ ] Performance profiling
- [ ] Database encryption strategy
- [ ] Windows installer
- [ ] Windows auto-update
- [ ] Android release build
- [ ] Android update strategy
- [ ] Store-ready screenshots and metadata
- [ ] Final product branding
- [ ] Production documentation

### Planned Future Extensions

These are outside the first production milestone but the architecture should not block them:

- Web client
- POS client
- Multi-branch cloud deployment
- Public API / integrations
- Excel import/export
- Electronic invoice integrations
- Advanced BI dashboards
- Document management
- Workflow automation
- Optional AI-assisted reporting and anomaly detection

---

## Current milestone

**Phase 1 — Identity + Accounting Core**

Implemented so far:
- Flutter application shell for Windows and Android
- First-company setup flow
- API-backed username/password login
- Company, user, role and trusted-device domain models
- Development session authentication
- Initial chart of accounts
- Balanced double-entry journal validation
- Initial general ledger and trial balance reports
- Unified Reports Center with fiscal-year and explicit from/to date filtering
- Operational reporting for product profit, purchases, inventory, parties, treasury, cost centers and projects
- Offline Demo reporting engine plus CSV export
- Offline journal-entry UI with Rial amounts
- Local Draft/Pending Sync documents stored transactionally with Outbox
- End-to-end Outbox journal upload with retry/error tracking
- Idempotent server receipts preventing duplicate journal creation
- Cursor-based server-to-client journal pull and SQLite merge
- Cursor-based floating-detail delta pull
- Optimistic Revision/BaseRevision control for mutable detail accounts
- Explicit conflict storage with Keep Server / Retry Local resolution
- Automatic master-data refresh before bidirectional accounting sync
- Configurable Solar Hijri fiscal years with close/reopen control
- Twelve standard Solar Hijri fiscal periods with close/reopen enforcement
- Journal reversal workflow with immutable original document and audit trail
- Fiscal journal, general ledger, trial balance, profit & loss and balance sheet UIs
- Opening-balance carry-forward in ledger/trial-balance reports
- Floating-detail ledger with opening/running/closing balances
- International currencies with IRR default plus USD/EUR/GBP/AED/TRY/CNY/CHF/CAD/JPY presets
- Historical FX rate table with buy, sell and accounting rates per date/source
- Foreign-currency journals storing original currency amount, historical exchange rate and base-ledger equivalent
- Currency-position/revaluation report for foreign monetary assets and liabilities
- Audited FX revaluation posting that preserves foreign balances while updating base-currency carrying value
- Foreign-currency purchase/sale inventory tests preserving historical base cost
- Floating detail accounts cached offline and selectable per journal line
- CI for ASP.NET Core build, sync/revision tests and Flutter analysis
- Separate Sales & Inventory workspace for products, warehouses, stock and sales invoices
- Sales posting integration: cash/receivable, revenue, tax, COGS and inventory journal lines
- Linked accounting journal number on posted sales invoices
- Non-destructive SQLite/PostgreSQL bootstrap for sales/inventory server tables
- End-to-end automated test for sale → stock issue → balanced accounting journal
- Architecture for online sync, USB sync, QR pairing and MFA

### Immediate next steps

1. Validate SQLite v8 fiscal-period/finalization upgrade plus sales/inventory bootstrap on Windows
2. Add sales returns and warehouse transfers
3. Extend revision/conflict policy to the next mutable business entities
4. Freeze Phase 1 schema and generate provider-specific EF Core migrations
5. Verify PostgreSQL provider when Docker/PostgreSQL is available
5. Persistent sessions and refresh-token rotation
6. TOTP 2FA + backup codes
7. QR device pairing
8. Push/pull Sync Engine

> Server persistence uses EF Core. Local development defaults to a persistent SQLite database so Docker is optional; PostgreSQL remains the server/production provider. Client-side offline SQLite, journal drafts, fiscal-year cache, floating-detail cache and transactional Outbox are implemented.

## Core goals

- Double-entry accounting and financial reporting
- Inventory, purchasing, sales, assets, CRM and resource management
- Offline-first Windows and Android clients
- Online sync through a secure API
- Optional offline USB delta synchronization
- Optional QR-based device pairing
- MFA / 2FA, trusted devices and audit logging
- Modular architecture suitable for future Web/POS clients

## Stack

- **Client:** Flutter (Windows + Android)
- **Backend:** ASP.NET Core / .NET 10
- **Server database:** PostgreSQL + EF Core (server/production); SQLite fallback for local development
- **Local database:** SQLite on Windows/Android (initial layer implemented)
- **Sync:** versioned delta sync with conflict handling
- **Security:** password hashing now; refresh sessions, TOTP, passkeys/biometrics and QR pairing planned

## Development

See:
- `docs/DEVELOPMENT.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY.md`
- `docs/SYNC_PROTOCOL.md`
- `docs/OFFLINE_FIRST.md`
- `docs/MIGRATIONS.md`
- `docs/ROADMAP.md`
