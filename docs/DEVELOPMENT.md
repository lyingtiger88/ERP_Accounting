# Local Development

## Requirements

### Windows development machine
- Windows 10 or 11
- Flutter stable
- .NET SDK 10
- Visual Studio with Desktop development with C++ for Flutter Windows builds
- Android Studio / Android SDK only when testing Android

## Local development database — no Docker required

The API now defaults to **SQLite** for local development.

The database file is created automatically under:

```text
apps/server_api/data/erp_accounting.db
```

This database is persistent: closing and restarting the API does not delete companies, users, devices, accounts or journals.

PostgreSQL support remains enabled for server/production-style testing, but Docker is optional during normal local development.

## First setup

From the repository root:

```text
Start_ERP_Dev.bat
```

If Flutter or .NET 10 are missing:

```text
Install_Flutter_Windows.bat
Install_DotNet10_Windows.bat
```

## Run API with local SQLite

```powershell
cd apps/server_api
dotnet run
```

The default provider from `appsettings.json` is `Sqlite`.

On first startup EF Core creates the development schema automatically.

## Run Windows client

In another terminal:

```powershell
cd apps/client_flutter
flutter run -d windows
```

## Persistence smoke test

1. Create a company.
2. Sign in.
3. Close the Windows client.
4. Stop the API with `Ctrl+C`.
5. Start the API again.
6. Start the client again.
7. Sign in with the same credentials.

If login works without recreating the company, persistence is working.

## Optional PostgreSQL mode

When Docker Desktop or PostgreSQL is available, switch providers for that API process:

```powershell
$env:Database__Provider="Postgres"
dotnet run
```

Expected development PostgreSQL connection:

```text
Host: 127.0.0.1
Port: 5432
Database: erp_accounting
Username: erp
Password: erp_dev_password
```

With Docker installed, `Start_Postgres_Dev.bat` starts the development PostgreSQL container.

## Run Android client

Start an Android emulator or connect a device, then:

```powershell
cd apps/client_flutter
flutter run -d android
```

## Current implementation boundary

Implemented:
- Responsive Windows/Android Flutter shell
- API-backed company bootstrap and login
- EF Core persistence
- Persistent SQLite development database without Docker
- PostgreSQL provider for server/production-style deployment
- Persistent Company/User/TrustedDevice records
- Persistent chart of accounts and journal entries
- General ledger and trial balance
- Development bearer sessions

Next:
1. EF Core migrations replacing `EnsureCreated`
2. Client-side SQLite database for true offline-first Windows/Android use
3. Refresh-token persistence
4. TOTP MFA and recovery codes
5. QR device pairing
6. Sync change journal
