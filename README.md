# ERP Accounting

Cross-platform accounting and resource-management platform for Windows and Android.

> **Working title:** `ERP_Accounting` — final product/brand name will be selected later.

## Project Roadmap

This roadmap is intentionally kept on the repository home page so the current plan and progress are always visible.

| Phase | Focus | Status |
|---|---|---|
| **Phase 0** | Project foundation, architecture, Windows + Android targets, CI, online/USB sync design | 🟡 Mostly complete |
| **Phase 1** | Identity, companies, users, roles, accounting foundation, 2FA, QR pairing | 🟡 In progress |
| **Phase 2** | Offline-first sync engine, online sync, USB delta sync, conflict handling | ⚪ Planned |
| **Phase 3** | Sales, purchasing, customers, suppliers, cash/bank, cheques, tax | ⚪ Planned |
| **Phase 4** | Inventory, warehouses, resources, assets, barcode/QR, serial/batch tracking | ⚪ Planned |
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
- [ ] PostgreSQL migration strategy
- [ ] SQLite local database strategy

### Phase 1 — Identity + Accounting Core **← CURRENT**

#### Identity & Security
- [x] Company bootstrap
- [x] User model
- [x] Role model
- [x] Password hashing
- [x] Login API
- [x] Client login connected to API
- [x] Trusted-device domain model
- [ ] Persistent sessions + refresh tokens
- [ ] User/role administration screens
- [ ] TOTP 2FA
- [ ] Backup recovery codes
- [ ] Trusted-device management
- [ ] Optional QR device pairing
- [ ] Step-up authentication for sensitive operations
- [ ] Security audit log
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
- [ ] Accounting periods
- [x] Client-side Draft / Pending Sync journal workflow
- [ ] Server-side Draft / post / reverse journal workflow
- [x] Floating detail accounts (تفصیلی شناور)
- [ ] Opening balances
- [x] Fiscal year close/reopen enforcement
- [ ] Profit & loss
- [ ] Balance sheet
- [ ] Account turnover reports

#### Persistence
- [x] PostgreSQL server database
- [x] Entity Framework Core
- [ ] Database migrations
- [x] Shared client SQLite storage layer (Windows/Android)
- [x] Windows offline cache validation path
- [ ] Android device validation
- [ ] Secure local credential storage

### Phase 2 — Synchronization Engine

- [ ] Device identity
- [x] Client sync outbox/change-journal foundation
- [x] Outbox push processor
- [ ] Entity revisions
- [ ] Sync cursors
- [ ] Delta upload/download
- [x] Idempotent accounting journal upload
- [ ] General idempotent change processing
- [ ] Conflict detection
- [ ] Conflict resolution policies
- [ ] Online synchronization
- [ ] LAN synchronization
- [ ] USB `.erp-sync` export/import
- [ ] Package encryption
- [ ] Package signatures
- [ ] Replay protection
- [ ] Attachment synchronization
- [ ] Sync diagnostics and history
- [ ] Manual conflict-resolution UI

### Phase 3 — Commercial & Treasury

- [ ] Customers
- [ ] Suppliers
- [ ] Quotations / proforma invoices
- [ ] Sales invoices
- [ ] Sales returns
- [ ] Purchase invoices
- [ ] Purchase returns
- [ ] Discounts
- [ ] Tax
- [ ] Receipts
- [ ] Payments
- [ ] Cashboxes
- [ ] Bank accounts
- [ ] Transfers
- [ ] Cheques
- [ ] Due-date tracking
- [ ] Accounts receivable
- [ ] Accounts payable
- [ ] Bank reconciliation

### Phase 4 — Inventory & Resource Management

- [ ] Products and services
- [ ] Multiple warehouses
- [ ] Inventory receipts/issues
- [ ] Warehouse transfers
- [ ] Real-time stock balances
- [ ] Minimum stock alerts
- [ ] Barcode support
- [ ] QR support
- [ ] Serial numbers
- [ ] Batch/lot tracking
- [ ] Expiration dates
- [ ] Stock valuation
- [ ] Fixed assets
- [ ] Depreciation
- [ ] Equipment
- [ ] Vehicles / machinery
- [ ] Resource assignment and location history

### Phase 5 — Management Layer

- [ ] CRM
- [ ] Customer interaction history
- [ ] Contracts
- [ ] Projects
- [ ] Cost centers
- [ ] Branches / organizational units
- [ ] HR employee records
- [ ] Attendance
- [ ] Leave
- [ ] Payroll foundation
- [ ] Management dashboard
- [ ] KPI widgets
- [ ] Custom reports
- [ ] Approval workflows
- [ ] Notifications
- [ ] Role-based dashboards

### Phase 6 — Production, Distribution & Hardening

- [ ] Automated unit tests
- [ ] Integration tests
- [ ] End-to-end tests
- [ ] Backup / restore
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
- Offline journal-entry UI with Rial amounts
- Local Draft/Pending Sync documents stored transactionally with Outbox
- End-to-end Outbox journal upload with retry/error tracking
- Idempotent server receipts preventing duplicate journal creation
- Configurable Solar Hijri fiscal years with close/reopen control
- Floating detail accounts cached offline and selectable per journal line
- CI for ASP.NET Core build and Flutter analysis
- Architecture for online sync, USB sync, QR pairing and MFA

### Immediate next steps

1. Validate fiscal-year/detail-cache upgrade on Windows
2. Add server-to-client journal pull + sync cursors
3. Freeze Phase 1 schema and generate provider-specific EF Core migrations
4. Verify PostgreSQL provider when Docker/PostgreSQL is available
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
