#!/usr/bin/env python3
"""Install the approved Zarr brand icon into Flutter-generated platform runners.
Source: compact preview of the user-approved image; upgrade to original PNG for production.
"""
from pathlib import Path
from io import BytesIO
from PIL import Image
import base64
import json
import sys

root = Path(__file__).resolve().parents[1]
app = root / "apps/client_flutter"
source = root / "branding/zarr_original.png"
if source.exists():
    image = Image.open(source).convert("RGB")
else:
    fallback = root / "branding/zarr_icon_preview.webp.base64"
    image = Image.open(BytesIO(base64.b64decode(fallback.read_text().strip()))).convert("RGB")

def square(size):
    return image.resize((size, size), Image.Resampling.LANCZOS)

def android():
    base = app / "android/app/src/main/res"
    if not base.exists():
        raise RuntimeError("Generate Android runner before installing icons")
    for folder, size in [('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)]:
        target = base / ('mipmap-' + folder)
        target.mkdir(parents=True, exist_ok=True)
        square(size).save(target / "ic_launcher.png")
    # launcher resource name unchanged; manifest references @mipmap/ic_launcher
    print("Android Zarr icon installed")

def ios():
    dest = app / "ios/Runner/Assets.xcassets/AppIcon.appiconset"
    dest.mkdir(parents=True, exist_ok=True)
    specs = [(20,2,'iphone'),(20,3,'iphone'),(29,2,'iphone'),(29,3,'iphone'),
             (40,2,'iphone'),(40,3,'iphone'),(60,2,'iphone'),(60,3,'iphone'),
             (20,1,'ipad'),(20,2,'ipad'),(29,1,'ipad'),(29,2,'ipad'),
             (40,1,'ipad'),(40,2,'ipad'),(76,1,'ipad'),(76,2,'ipad'),
             (83.5,2,'ipad'),(1024,1,'ios-marketing')]
    entries=[]
    for points,scale,idiom in specs:
        pixels = round(points*scale)
        filename = f"zarr_{idiom}_{str(points).replace('.', '_')}_{scale}x.png"
        square(pixels).save(dest / filename)
        entries.append({'size':f'{points}x{points}','idiom':idiom,'filename':filename,'scale':f'{scale}x'})
    (dest / "Contents.json").write_text(json.dumps({'images':entries,'info':{'version':1,'author':'xcode'}},indent=2),encoding="utf-8")
    print("iOS Zarr icon installed")

def windows():
    dest = app / "windows/runner/resources"
    dest.mkdir(parents=True, exist_ok=True)
    image.resize((256,256),Image.Resampling.LANCZOS).save(
        dest / "app_icon.ico", format="ICO", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
    print("Windows Zarr icon installed")

if __name__ == "__main__":
    if len(sys.argv)!=2 or sys.argv[1] not in ('android','ios','windows'):
        raise SystemExit("Usage: python scripts/install_zarr_icon.py android|ios|windows")
    globals()[sys.argv[1]]()
