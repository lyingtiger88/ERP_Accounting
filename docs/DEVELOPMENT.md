# Local Development

## Requirements

### Windows development machine
- Windows 10 or 11
- Flutter stable (current project baseline: Flutter 3.47+)
- Android Studio / Android SDK for Android builds
- Visual Studio with Desktop development with C++ for Flutter Windows builds
- .NET SDK 10

## First setup

From PowerShell at repository root:

```powershell
./scripts/bootstrap.ps1
```

The script generates Flutter's standard `windows/` and `android/` runners if they are not present, then restores Flutter and .NET dependencies.

## Run API

```powershell
cd apps/server_api
dotnet run
```

Use `ERPAccounting.Api.http` for Phase 1 smoke requests.

> Phase 1 currently uses development-only in-memory persistence. Restarting the API clears users, companies, devices and sessions.

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
- Responsive Flutter login shell
- Responsive ERP dashboard shell
- Company/User/TrustedDevice domain models
- First-company bootstrap endpoint
- Password hashing
- Login and temporary bearer session
- Pending trusted-device registration

Next:
1. PostgreSQL persistence
2. SQLite client persistence
3. API client wiring from Flutter login
4. JWT/refresh-token session model
5. TOTP MFA and recovery codes
6. QR device pairing
7. Sync change journal
