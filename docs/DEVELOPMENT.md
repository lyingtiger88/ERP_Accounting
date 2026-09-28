# Local Development

## Requirements

### Windows development machine
- Windows 10 or 11
- Flutter stable
- Android Studio / Android SDK for Android builds
- Visual Studio with Desktop development with C++ for Flutter Windows builds
- .NET SDK 10
- PostgreSQL 18, either through Docker Desktop or a local installation

## First setup

From the repository root:

```powershell
Start_ERP_Dev.bat
```

If Flutter or .NET 10 are missing, use:

```text
Install_Flutter_Windows.bat
Install_DotNet10_Windows.bat
```

## Start PostgreSQL

Recommended development path:

```text
Start_Postgres_Dev.bat
```

This uses `compose.yaml` and starts a PostgreSQL container with the development database.

Expected development connection:

```text
Host: 127.0.0.1
Port: 5432
Database: erp_accounting
Username: erp
Password: erp_dev_password
```

The password above is intentionally development-only. Production secrets must never be committed.

If Docker is not installed, a native PostgreSQL installation can be used with the same connection values, or override the connection using:

```powershell
$env:ConnectionStrings__Postgres="Host=...;Port=5432;Database=...;Username=...;Password=..."
```

## Run API

```powershell
cd apps/server_api
dotnet run
```

On startup, the development API currently calls EF Core `EnsureCreated` to create the initial schema if the database is empty.

Use `ERPAccounting.Api.http` for smoke requests.

## Run Windows client

```powershell
cd apps/client_flutter
flutter run -d windows
```

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
- PostgreSQL persistence through EF Core
- Persistent Company/User/TrustedDevice records
- Persistent chart of accounts and journal entries
- General ledger and trial balance
- Development bearer sessions

Next:
1. EF Core migrations replacing `EnsureCreated`
2. SQLite local client database
3. Refresh-token persistence
4. TOTP MFA and recovery codes
5. QR device pairing
6. Sync change journal
