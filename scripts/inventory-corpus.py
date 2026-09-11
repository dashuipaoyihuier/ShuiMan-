#!/usr/bin/env python3
"""Read-only EPUB inventory independent of the Swift parser. Requires Pillow and lxml.

Comic pixels remain in ignored local output; only structure and source hashes are reported.
"""
import argparse
import hashlib
import json
import posixpath
import re
import zipfile
from collections import Counter
from pathlib import Path
from urllib.parse import unquote, urlsplit

from lxml import etree
from PIL import Image


def local(element):
    return element.tag.rsplit('}', 1)[-1] if isinstance(element.tag, str) else ''


def xml(data):
    return etree.fromstring(data, etree.XMLParser(resolve_entities=False, no_network=True, recover=True))


def resolve(parent, ref):
    parts = urlsplit(ref)
    if parts.scheme or parts.netloc or parts.path.startswith('/'):
        raise ValueError('external resource')
    path = posixpath.normpath(posixpath.join(posixpath.dirname(parent), unquote(parts.path)))
    if path.startswith('../'):
        raise ValueError('escaped resource')
    return path


def inspect(path):
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    with zipfile.ZipFile(path) as archive:
        container = xml(archive.read('META-INF/container.xml'))
        package_path = next(e.get('full-path') for e in container.iter() if local(e) == 'rootfile')
        package = xml(archive.read(package_path))
        manifest = {e.get('id'): dict(e.attrib) for e in package.iter() if local(e) == 'item'}
        spine = next(e for e in package.iter() if local(e) == 'spine')
        styles = {}
        for item in manifest.values():
            if item.get('media-type') == 'text/css':
                name = resolve(package_path, item['href'])
                styles[name] = archive.read(name).decode('utf-8', errors='replace')
        pages = []
        for occurrence, item in enumerate(spine):
            if local(item) != 'itemref' or item.get('linear') == 'no':
                continue
            unit = {'position': len(pages)+1, 'occurrence': occurrence}
            try:
                entry = manifest[item.get('idref')]
                resource = resolve(package_path, entry['href'])
                unit.update(resource=resource, properties=item.get('properties', ''))
                content = xml(archive.read(resource))
                images = []
                for element in content.iter():
                    kind = local(element)
                    if kind not in ('img', 'image'):
                        continue
                    reference = element.get('src') or element.get('href') or element.get('{http://www.w3.org/1999/xlink}href')
                    if not reference:
                        continue
                    imgpath = resolve(resource, reference)
                    with archive.open(imgpath) as image_file:
                        image = Image.open(image_file)
                        width, height = image.size
                    images.append({'resource': imgpath, 'width': width, 'height': height,
                                   'class': element.get('class', ''), 'style': element.get('style', ''), 'tag': kind})
                body = next((e for e in content.iter() if local(e) == 'body'), content)
                text = ''.join(body.itertext()).strip()
                unit.update(images=images, body_text_length=len(text), svg_count=sum(local(e) == 'svg' for e in content.iter()),
                            script_count=sum(local(e) == 'script' for e in content.iter()))
            except Exception as error:
                unit['error'] = str(error)
            pages.append(unit)
        title = next((''.join(e.itertext()) for e in package.iter() if local(e) == 'title'), path.stem)
        return {'file': path.name, 'sha256': digest, 'bytes': path.stat().st_size, 'title': title,
                'package_path': package_path, 'version': package.get('version'), 'direction': spine.get('page-progression-direction'),
                'spine_count': len(pages), 'archive_entries': len(archive.infolist()),
                'css_hash': hashlib.sha256('\n'.join(styles.values()).encode()).hexdigest(), 'styles': styles,
                'metadata': [dict(e.attrib, text=(e.text or '').strip()) for e in package.iter() if local(e) == 'meta'],
                'image_counts': dict(Counter(len(p.get('images', [])) for p in pages)),
                'svg_pages': sum(p.get('svg_count', 0) > 0 for p in pages),
                'wide_positions': [p['position'] for p in pages if any(i['width']/i['height'] >= 1.2 for i in p.get('images', []))],
                'two_page_positions': [p['position'] for p in pages if any('twoPage' in i['class'] for i in p.get('images', []))],
                'errors': sum('error' in p for p in pages), 'pages': pages}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('root', nargs='?', default='.')
    parser.add_argument('--output', default='.build/corpus/inventory.json')
    parser.add_argument('--recursive', action='store_true')
    args = parser.parse_args()
    result = []
    for path in sorted(Path(args.root).glob('**/*.epub' if args.recursive else '*.epub')):
        book = inspect(path)
        if args.recursive: book['file'] = str(path.resolve())
        result.append(book)
        print(f"{book['file']}: spine={book['spine_count']} v={book['version']} svg={book['svg_pages']} "
              f"wide={len(book['wide_positions'])} twoPage={len(book['two_page_positions'])} errors={book['errors']}", flush=True)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'books': result}, ensure_ascii=False, indent=2)+'\n')
    print(f"TOTAL: {len(result)} books, {sum(b['spine_count'] for b in result)} reading units; {output}")


if __name__ == '__main__':
    main()
