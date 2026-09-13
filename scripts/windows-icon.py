"""Reuse the macOS artwork for Windows icons and the WPF title-bar image.

Run with Python and Pillow installed. Existing macOS PNG representations stay
byte-for-byte unchanged; only missing Windows DPI sizes are downsampled.
"""
from pathlib import Path
from io import BytesIO
import struct

from PIL import Image

root = Path(__file__).resolve().parent.parent
source = (root / "Resources/AppIcon.icns").read_bytes()
assert source[:4] == b"icns"
images = {}
position = 8
while position < len(source):
    size = struct.unpack_from(">I", source, position + 4)[0]
    assert size >= 8 and position + size <= len(source)
    data = source[position + 8:position + size]
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        width, height = struct.unpack_from(">II", data, 16)
        if width == height and width <= 256:
            images[width] = data
    position += size
assert images
# Keep Apple's original 32/64/128/256 px PNG data, including its color metadata.
# Windows also requests these smaller sizes for its taskbar, menus, and DPI
# scaling. Derive each one from the nearest larger original representation.
originals = images.copy()
for width in (16, 20, 24, 40, 48, 96):
    source_width = min(size for size in originals if size >= width)
    with Image.open(BytesIO(originals[source_width])) as original:
        resized = original.convert("RGBA").resize((width, width), Image.Resampling.LANCZOS)
        encoded = BytesIO()
        resized.save(encoded, format="PNG", icc_profile=original.info.get("icc_profile"))
        images[width] = encoded.getvalue()
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + len(images) * 16
entries, payload = [], []
for width, data in sorted(images.items()):
    entries.append(struct.pack("<BBBBHHII", width % 256, width % 256, 0, 0, 1, 32, len(data), offset))
    payload.append(data)
    offset += len(data)
icon_directory = root / "windows/ShuiMan.Windows"
(icon_directory / "AppIcon.ico").write_bytes(header + b"".join(entries + payload))
# The 29 px WPF title bar has a separate PNG resource so WPF can display the
# original 64 px representation directly, instead of choosing an ICO frame.
(icon_directory / "AppIcon.png").write_bytes(originals[64])
print("Reused macOS artwork: ICO " + ", ".join(map(str, sorted(images))) + " px; title bar 64 px.")
