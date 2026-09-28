# ERP Accounting

Cross-platform accounting and resource-management platform for Windows and Android.

## Current milestone

Phase 1 is now under active implementation.

Implemented so far:
- Flutter application shell for Windows and Android
- First-company setup flow
- API-backed username/password login
- Company, user, role and trusted-device domain models
- Development session authentication
- Initial chart of accounts
- Balanced double-entry journal validation
- Initial general ledger and trial balance reports
- CI for ASP.NET Core build and Flutter analysis
- Architecture for online sync, USB sync, QR pairing and MFA

> The current API persistence is intentionally in-memory for the first runnable milestone. PostgreSQL and SQLite are the next persistence layer.

## Core goals
- Double-entry accounting and financial reporting
- Inventory, purchasing, sales, assets, CRM and resource management
- Offline-first Windows and Android clients
- Online sync through a secure API
- Optional offline USB delta synchronization
- Optional QR-based device pairing
- MFA / 2FA, trusted devices and audit logging
- Modular architecture suitable for future Web/POS clients

> Project name is temporary. Product branding will be selected later.

## Stack
- **Client:** Flutter (Windows + Android)
- **Backend:** ASP.NET Core / .NET 10
- **Server database:** PostgreSQL (planned next)
- **Local database:** SQLite (planned next)
- **Sync:** versioned delta sync with conflict handling
- **Security:** password hashing now; refresh sessions, TOTP, passkeys/biometrics and QR pairing planned

## Development

See:
- `docs/DEVELOPMENT.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY.md`
- `docs/SYNC_PROTOCOL.md`
- `docs/ROADMAP.md`
