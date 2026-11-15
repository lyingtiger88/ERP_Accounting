# Permanent release identity — Zarr

## Android upgrade contract

- Permanent Android applicationId: `ir.kaliia.zarr`.
- Stable **production/release** certificate SHA-256: `35:A4:AF:D0:D4:CA:27:B8:99:88:B1:5F:93:ED:CC:81:CD:92:AF:E3:3F:AD:F3:39:B1:6E:A2:AF:01:AD:67:67`.
- The release key is generated once and preserved offline. Never generate a new private key on the GitHub runner or commit a keystore, password, or credential JSON.
- Android allows an in-place APK update only when the package ID is identical, signing certificates match (or a supported authenticated signing lineage exists), and `versionCode` does not decrease.
- A new debug APK signed with a different runner's debug key is **not** an update of a release APK, even with the same package ID.
- Releases are generated through **Actions → Zarr Android signed upgrade APK → Run workflow**; the workflow fails without secrets and verifies the public certificate fingerprint.
- Release version codes increment using the GitHub workflow run number with a reserved 10000 offset; never decrease codes when changing CI workflows.

## Required GitHub Actions repository secrets

In Settings → Secrets and variables → Actions → Repository secrets, add:

- `ZARR_ANDROID_KEYSTORE_B64` — base64 of the exact `zarr-release.jks` file from the secure signing vault. Base64 is transport encoding **not encryption**, so treat as a secret.
- `ZARR_ANDROID_STORE_PASSWORD` — store password.
- `ZARR_ANDROID_KEY_ALIAS` — the keystore alias.
- `ZARR_ANDROID_KEY_PASSWORD` — key password.

The encrypted Kaliia signing vault contains each JKS and a `signing_credentials.json` record with the corresponding values. Do not paste the password into source, issues, pull requests, README, code review, or Actions logs.

**One-time activation:** the first signed release needs these secrets in GitHub. I cannot configure encrypted GitHub Actions secrets from the currently available repository-file API. Until they are installed, do not advertise DEBUG artifacts as seamless upgrades.

## Windows upgrade contract

Inno Setup installer/Zarr.iss has permanent AppId {A8D6F5BA-B298-44A7-AE61-A6F3EE65E272}. Legacy generated Flutter com.example.erp_accounting APKs are not in-place upgradable to the final ir.kaliia.zarr package.
Use the same Inno Setup `AppId` and product-specific installation path on every future build. New setup EXEs can replace the installed app without uninstalling; leave user data in AppData, not the application binaries directory. A portable EXE by itself is not an installer/auto-updater.

A stable Windows installer identity is separate from Authenticode code signing. For a publicly trusted publisher signature, obtain a code-signing certificate from a trusted CA or an approved signing service; a new self-signed PFX will not establish SmartScreen trust.

## Initial migration warning

**Do not uninstall current APKs containing irreplaceable data.** If a device already has a DEBUG-signed APK or different package ID, these newly created production identities might require a one-time migration. Inventory existing installed packages' signatures with `apksigner verify --print-certs old.apk` and compare against the above SHA-256; then export/backup app data before any reinstall. Android deletes app-private data when the app is uninstalled.

Once the first production-signed APK is accepted on devices, **never rotate or lose the signing key**. Keep two encrypted offline backups of the private vault and credentials. Android App Signing via Google Play uses an additional managed signing key model; confirm Play enrollment before your first Play release.
