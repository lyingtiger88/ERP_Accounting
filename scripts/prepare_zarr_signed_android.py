#!/usr/bin/env python3
"""Configure Flutter's generated Android project for stable Zarr release signing.

Run after 'flutter create . --platforms=android' and before 'flutter build apk'.
No secret value is written to source control. The keystore stays in runner temp.
"""
from pathlib import Path
import os
import sys

APP_ID = "ir.kaliia.zarr"
android = Path("apps/client_flutter/android/app")
kts = android / "build.gradle.kts"
groovy = android / "build.gradle"
if not kts.exists() and not groovy.exists():
    raise SystemExit("Flutter Android Gradle file not generated")
gradle_file = kts if kts.exists() else groovy
source = gradle_file.read_text(encoding="utf-8")
for name in (
    "ZARR_ANDROID_KEYSTORE_PATH",
    "ZARR_ANDROID_STORE_PASSWORD",
    "ZARR_ANDROID_KEY_ALIAS",
    "ZARR_ANDROID_KEY_PASSWORD",
):
    if not os.environ.get(name):
        raise SystemExit(f"Missing signing environment variable: {name}")

if gradle_file == kts:
    if 'signingConfig = signingConfigs.getByName("debug")' not in source:
        raise SystemExit("Unknown Flutter Kotlin Gradle release signing template")
    if 'applicationId = "com.example.erp_accounting"' not in source:
        raise SystemExit("Unexpected Android app ID; refuse silent identity rotation")
    source = source.replace(
        'applicationId = "com.example.erp_accounting"',
        f'applicationId = "{APP_ID}"',
    )
    source = source.replace(
        "    buildTypes {",
        """    signingConfigs {
        create("kaliiaRelease") {
            storeFile = file(System.getenv("ZARR_ANDROID_KEYSTORE_PATH"))
            storePassword = System.getenv("ZARR_ANDROID_STORE_PASSWORD")
            keyAlias = System.getenv("ZARR_ANDROID_KEY_ALIAS")
            keyPassword = System.getenv("ZARR_ANDROID_KEY_PASSWORD")
        }
    }
    buildTypes {""",
        1,
    )
    source = source.replace(
        'signingConfig = signingConfigs.getByName("debug")',
        'signingConfig = signingConfigs.getByName("kaliiaRelease")',
        1,
    )
else:
    if 'signingConfig signingConfigs.debug' not in source:
        raise SystemExit("Unknown Flutter Groovy Gradle release signing template")
    if 'applicationId "com.example.erp_accounting"' not in source:
        raise SystemExit("Unexpected Android app ID; refuse silent identity rotation")
    source = source.replace(
        'applicationId "com.example.erp_accounting"',
        f'applicationId "{APP_ID}"',
    )
    source = source.replace(
        "    buildTypes {",
        """    signingConfigs {
        kaliiaRelease {
            storeFile file(System.getenv('ZARR_ANDROID_KEYSTORE_PATH'))
            storePassword System.getenv('ZARR_ANDROID_STORE_PASSWORD')
            keyAlias System.getenv('ZARR_ANDROID_KEY_ALIAS')
            keyPassword System.getenv('ZARR_ANDROID_KEY_PASSWORD')
        }
    }
    buildTypes {""",
        1,
    )
    source = source.replace(
        'signingConfig signingConfigs.debug',
        'signingConfig signingConfigs.kaliiaRelease',
        1,
    )
gradle_file.write_text(source, encoding="utf-8")
print(f"Configured {gradle_file} with stable package {APP_ID} and private signing inputs.")
