#!/usr/bin/env python3
"""Install the approved Zarr brand artwork as the Android and Windows app icon."""
from pathlib import Path
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "apps/client_flutter"
SOURCE = ROOT / "branding/zarr_original.webp"

if not SOURCE.is_file():
    raise SystemExit(f"Approved Zarr icon asset is missing: {SOURCE}")

image = Image.open(SOURCE).convert("RGBA")

# Android launcher icon, localized application label and stable icon reference.
android = APP / "android"
if android.is_dir():
    res = android / "app/src/main/res"
    for density, size in (("mdpi", 48), ("hdpi", 72), ("xhdpi", 96),
                          ("xxhdpi", 144), ("xxxhdpi", 192)):
        folder = res / f"mipmap-{density}"
        folder.mkdir(parents=True, exist_ok=True)
        image.resize((size, size), Image.Resampling.LANCZOS).save(
            folder / "ic_launcher.png", format="PNG", optimize=True
        )

    manifest = android / "app/src/main/AndroidManifest.xml"
    if manifest.is_file():
        source = manifest.read_text(encoding="utf-8")
        source, label_count = re.subn(
            r'android:label="[^"]+"',
            'android:label="@string/app_name"', source, count=1
        )
        if label_count != 1:
            raise SystemExit("Could not set the Android app label safely")
        source, icon_count = re.subn(
            r'android:icon="[^"]+"',
            'android:icon="@mipmap/ic_launcher"', source, count=1
        )
        if icon_count != 1:
            raise SystemExit("Could not set the Android launcher icon reference")
        manifest.write_text(source, encoding="utf-8")

    values = android / "app/src/main/res/values"
    values_en = android / "app/src/main/res/values-en"
    values.mkdir(parents=True, exist_ok=True)
    values_en.mkdir(parents=True, exist_ok=True)
    (values / "strings.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<resources><string name="app_name">زر</string></resources>\n',
        encoding="utf-8",
    )
    (values_en / "strings.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<resources><string name="app_name">Zarr</string></resources>\n',
        encoding="utf-8",
    )

# Windows runner: replace Flutter's default icon and generic executable metadata.
windows = APP / "windows"
if windows.is_dir():
    resources = windows / "runner/resources"
    resources.mkdir(parents=True, exist_ok=True)
    image.resize((256, 256), Image.Resampling.LANCZOS).save(
        resources / "app_icon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48),
               (64, 64), (128, 128), (256, 256)],
    )

    rc = windows / "runner/Runner.rc"
    if rc.is_file():
        source = rc.read_text(encoding="utf-8")
        source = re.sub(
            r'(VALUE\s+"FileDescription",\s*)"[^"]*"',
            r'\1"Zarr | زر"', source
        )
        source = re.sub(
            r'(VALUE\s+"ProductName",\s*)"[^"]*"',
            r'\1"Zarr"', source
        )
        rc.write_text(source, encoding="utf-8")

    main_cpp = windows / "runner/main.cpp"
    if main_cpp.is_file():
        source = main_cpp.read_text(encoding="utf-8")
        source, count = re.subn(
            r'window\.Create\(L"[^"]*"\)',
            'window.Create(L"Zarr | زر")',
            source,
            count=1,
        )
        if count == 1:
            main_cpp.write_text(source, encoding="utf-8")

image.resize((512, 512), Image.Resampling.LANCZOS).save(
    APP / "zarr-play-store-icon.png", format="PNG", optimize=True
)
print("Installed the approved Zarr icon and product identity for available platform runners.")
