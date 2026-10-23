#!/usr/bin/env python3
"""Set the displayed Zarr application name in Flutter-generated platform runners."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1] / "apps" / "client_flutter"

def android():
    manifest = ROOT / "android/app/src/main/AndroidManifest.xml"
    if not manifest.is_file():
        raise SystemExit(f"Android manifest not found: {manifest}")
    data = manifest.read_text(encoding="utf-8")
    data, n = re.subn(r'android:label="[^"]*"', 'android:label="زر"', data, count=1)
    if n != 1:
        raise SystemExit("Could not find android:label in the generated manifest")
    manifest.write_text(data, encoding="utf-8")
    print("Android launcher label: زر")

def ios():
    info = ROOT / "ios/Runner/Info.plist"
    if not info.is_file():
        raise SystemExit(f"iOS Info.plist not found: {info}")
    data = info.read_text(encoding="utf-8")
    for key in ("CFBundleDisplayName", "CFBundleName"):
        pattern = r'(<key>' + key + r'</key>\s*<string>)[^<]*(</string>)'
        data, count = re.subn(pattern, lambda m: m.group(1) + "زر" + m.group(2), data, count=1)
        if count != 1:
            raise SystemExit(f"Missing {key} in Info.plist")
    info.write_text(data, encoding="utf-8")
    print("iOS app display name: زر")

if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in ("android", "ios"):
        raise SystemExit("Usage: python scripts/set_zarr_app_name.py android|ios")
    {"android": android, "ios": ios}[sys.argv[1]]()
