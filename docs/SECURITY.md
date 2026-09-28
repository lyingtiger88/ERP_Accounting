# Security Model

## Authentication layers
Authentication methods are modular and can be enabled per organization.

### Primary authentication
- Username/email + password
- Local PIN for an already trusted device
- Passkey support planned

### MFA / 2FA
- TOTP authenticator is the baseline second factor
- Backup recovery codes
- Biometrics / Windows Hello as device-local convenience factors
- Mobile approval / push confirmation planned

MFA can be optional for normal users and mandatory for privileged roles such as administrators or financial managers.

## QR pairing
QR pairing is optional and is not a replacement for MFA.

A pairing QR must contain only short-lived bootstrap information:
- Pairing session identifier
- Organization identifier
- Server endpoint or local endpoint hint
- Nonce
- Expiration
- Ephemeral public-key material or a reference to it

Never embed:
- Passwords
- Long-lived access tokens
- TOTP secrets
- Refresh tokens

### Pairing flow
1. Authenticated desktop requests a short-lived pairing session.
2. Desktop shows QR.
3. Android scans it.
4. Android proves possession of the pairing session.
5. Desktop/server shows device details and requests approval.
6. A device-specific credential/key is issued.
7. Pairing session becomes invalid immediately.

## Trusted devices
Store:
- Device UUID
- Friendly name
- Platform
- Public key fingerprint
- First paired time
- Last seen time
- Last successful sync
- Trust state
- Revocation time

Users can revoke a device at any time.

## Step-up authentication
Require re-authentication or MFA for sensitive operations, including:
- Disabling MFA
- Changing administrator permissions
- Closing a fiscal year
- Deleting/reversing posted financial documents
- Changing bank/payment configuration
- Exporting sensitive data
- Restoring backups
- Registering new trusted devices

## Session security
- Short-lived access tokens
- Rotating refresh tokens
- Refresh token revocation
- Device-bound session metadata
- Session list and remote logout
- Rate limiting and lockout protections

## Secret storage
- Passwords: modern password hashing with unique salts
- TOTP seeds: encrypted at rest
- Device private keys: OS secure storage where available
- Server secrets: environment/secret manager, never repository
- Backup codes: store only secure hashes

## Audit
Security-sensitive actions must emit immutable audit events:
- Login success/failure
- MFA enrollment/removal
- Device pair/revoke
- Role/permission changes
- Sensitive financial actions
- Backup/restore
- Export operations

Audit events should include actor, device, timestamp, organization, action, target and correlation ID.
