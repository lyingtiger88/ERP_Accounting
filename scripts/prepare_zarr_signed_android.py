#!/usr/bin/env python3
"""Configure Flutter's generated Android project for stable Zarr release signing.

Run after 'flutter create . --platforms=android' and before 'flutter build apk'.
No secret value is written to source control. The keystore stays in runner temp.
"""
from pathlib import Path
import os
import re
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
    if 'applicationId = "com.example.zarr"' not in source:
        raise SystemExit("Unexpected generated Zarr Android app ID; refuse silent identity rotation")
    source = source.replace(
        'applicationId = "com.example.zarr"',
        f'applicationId = "{APP_ID}"',
    )
    source = source.replace(
        "    buildTypes {",
        """    signingConfigs {
        create("kaliiaRelease") {
            storeFile = file(System.getenv("ZARR_ANDROID_KEYSTORE_PATH"))
            storeType = "PKCS12"
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
    if 'applicationId "com.example.zarr"' not in source:
        raise SystemExit("Unexpected Android app ID; refuse silent identity rotation")
    source = source.replace(
        'applicationId "com.example.zarr"',
        f'applicationId "{APP_ID}"',
    )
    source = source.replace(
        "    buildTypes {",
        """    signingConfigs {
        kaliiaRelease {
            storeFile file(System.getenv('ZARR_ANDROID_KEYSTORE_PATH'))
            storeType 'PKCS12'
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


# Apply the approved localized product identity to Android launcher and app label.
manifest = android / "src/main/AndroidManifest.xml"
if not manifest.exists():
    raise SystemExit("Generated AndroidManifest.xml not found")
manifest_text = manifest.read_text(encoding="utf-8")
manifest_text, label_count = re.subn(
    r'android:label="[^"]+"',
    'android:label="@string/app_name"',
    manifest_text,
    count=1,
)
if label_count != 1:
    raise SystemExit("Could not safely set Android application label")
manifest_text, icon_count = re.subn(
    r'android:icon="[^"]+"',
    'android:icon="@mipmap/ic_launcher"',
    manifest_text,
    count=1,
)
if icon_count == 0:
    manifest_text = manifest_text.replace(
        "<application",
        '<application android:icon="@mipmap/ic_launcher"',
        1,
    )
manifest.write_text(manifest_text, encoding="utf-8")

values = android / "src/main/res/values"
values_en = android / "src/main/res/values-en"
values.mkdir(parents=True, exist_ok=True)
values_en.mkdir(parents=True, exist_ok=True)
(values / "strings.xml").write_text(
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<resources>\n    <string name="app_name">زر</string>\n</resources>\n',
    encoding="utf-8",
)
(values_en / "strings.xml").write_text(
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<resources>\n    <string name="app_name">Zarr</string>\n</resources>\n',
    encoding="utf-8",
)
print("Configured localized Android app names: Persian=زر, English=Zarr.")
