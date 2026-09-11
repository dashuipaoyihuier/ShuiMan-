#!/usr/bin/env python3
"""Make local contact sheets for human annotation. Never changes an original book."""
import argparse
import io
import json
import zipfile
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument('--book', required=True)
parser.add_argument('--pages', default='1-30')
parser.add_argument('--pairs', action='store_true')
parser.add_argument('--ltr', action='store_true')
parser.add_argument('--output', required=True)
parser.add_argument('--inventory', default='.build/corpus/inventory.json')
args = parser.parse_args()
books = json.loads(Path(args.inventory).read_text())['books']
book = next(b for b in books if b['file'] == args.book)
positions = []
for part in args.pages.split(','):
    if '-' in part:
        a,b = map(int,part.split('-')); positions.extend(range(a,b+1))
    else:
        positions.append(int(part))
columns = 4 if args.pairs else 5
cellw,cellh = (420, 320) if args.pairs else (280, 420)
sheet = Image.new('RGB',(columns*cellw, ((len(positions)+columns-1)//columns)*cellh),'#dddddd')
draw = ImageDraw.Draw(sheet)
with zipfile.ZipFile(args.book) as archive:
    for offset,position in enumerate(positions):
        indices = [position,position+1] if args.pairs else [position]
        if args.pairs and not args.ltr: indices.reverse()
        tile = Image.new('RGB',(cellw-12,cellh-35),'#888888')
        for side,index in enumerate(indices):
            info=book['pages'][index-1]['images'][0]
            with Image.open(io.BytesIO(archive.read(info['resource']))) as original:
                thumb=original.convert('RGB')
                targetw=(cellw-12)//len(indices)
                thumb.thumbnail((targetw,cellh-35))
                tile.paste(thumb,(side*targetw+(targetw-thumb.width)//2,(cellh-35-thumb.height)//2))
        x,y=(offset%columns)*cellw,(offset//columns)*cellh
        sheet.paste(tile,(x+6,y+25))
        draw.text((x+9,y+6),'/'.join(map(str,indices)),fill='black',font_size=16)
output=Path(args.output); output.parent.mkdir(parents=True,exist_ok=True); sheet.save(output)
print(output)
