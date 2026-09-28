# ERP Accounting

Cross-platform accounting and resource-management platform for Windows and Android.

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

## Planned stack
- **Client:** Flutter (Windows + Android)
- **Backend:** ASP.NET Core
- **Server database:** PostgreSQL
- **Local database:** SQLite
- **Sync:** versioned delta sync with conflict handling
- **Security:** JWT/refresh sessions, TOTP, passkeys/biometrics-ready, QR pairing

See the `docs/` directory for architecture and roadmap.
