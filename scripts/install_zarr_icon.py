#!/usr/bin/env python3
"""Install the approved Zarr icon into Flutter-generated Android launcher resources."""
from io import BytesIO
from pathlib import Path
import base64
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "apps/client_flutter"
SOURCE = ROOT / "branding/zarr_original.webp"

if not SOURCE.is_file():
    raise SystemExit(f"Approved Zarr icon asset is missing: {SOURCE}")

image = Image.open(SOURCE).convert("RGB")
res = APP / "android/app/src/main/res"
if not res.is_dir():
    raise SystemExit("Generate the Android runner before installing the launcher icon")

for density, size in (("mdpi", 48), ("hdpi", 72), ("xhdpi", 96),
                      ("xxhdpi", 144), ("xxxhdpi", 192)):
    folder = res / f"mipmap-{density}"
    folder.mkdir(parents=True, exist_ok=True)
    image.resize((size, size), Image.Resampling.LANCZOS).save(
        folder / "ic_launcher.png", format="PNG", optimize=True
    )

# Also provide a 512px Play Store icon in the build workspace.
image.resize((512, 512), Image.Resampling.LANCZOS).save(
    APP / "zarr-play-store-icon.png", format="PNG", optimize=True
)
print("Installed the approved Zarr launcher icon for all Android densities.")
