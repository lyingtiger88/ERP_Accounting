# Architecture

## Product shape
ERP Accounting is an offline-first, multi-device accounting and resource-management platform.

### Clients
- Windows desktop client
- Android mobile client
- Shared Flutter application layer where practical

### Server
- ASP.NET Core API
- PostgreSQL as authoritative online datastore
- REST API initially; real-time events can be added later

### Local storage
Both Windows and Android maintain local SQLite databases so users can continue working without internet connectivity.

## High-level components

```
Windows Flutter ─┐
                 ├── Sync Engine ── HTTPS ── ASP.NET Core API ── PostgreSQL
Android Flutter ─┘
       │
       └──────── optional encrypted USB delta packages ────────┘
```

## Core bounded modules
1. Identity & Security
2. Accounting
3. Treasury / Cash & Bank
4. Sales
5. Purchasing
6. Inventory
7. Assets
8. CRM
9. Human Resources
10. Projects & Cost Centers
11. Reporting & Dashboards
12. Synchronization
13. Audit & Compliance

## Offline-first rules
- Business operations are committed locally first.
- Sync uses immutable change identifiers.
- Every syncable entity has a UUID, revision, timestamps and source device.
- Financial posting operations must be auditable.
- Posted accounting documents are corrected through controlled reversal/amendment workflows rather than silent destructive edits.
- Conflicts are resolved per entity policy, not with a global last-write-wins rule.

## Repository layout
```
apps/
  client_flutter/
  server_api/
packages/
  accounting_core/
  inventory_core/
  sync_engine/
  security_core/
  reporting/
  shared_models/
database/
  migrations/
  seed/
  schemas/
docs/
tests/
```

## Design principles
- Local-first usability
- Explicit auditability
- Least privilege
- Modular domain boundaries
- Deterministic synchronization
- Replaceable product branding
