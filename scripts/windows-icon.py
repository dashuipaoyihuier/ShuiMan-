"""Wrap existing PNG icon representations in ICO without resizing or changing artwork."""
from pathlib import Path
import struct

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
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + len(images) * 16
entries, payload = [], []
for width, data in sorted(images.items()):
    entries.append(struct.pack("<BBBBHHII", width % 256, width % 256, 0, 0, 1, 32, len(data), offset))
    payload.append(data)
    offset += len(data)
(root / "windows/ShuiMan.Windows/AppIcon.ico").write_bytes(header + b"".join(entries + payload))
