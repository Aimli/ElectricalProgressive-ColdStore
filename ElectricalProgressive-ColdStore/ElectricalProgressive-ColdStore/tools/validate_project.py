#!/usr/bin/env python3
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
warnings: list[str] = []


def check_json() -> None:
    for path in ROOT.rglob('*.json'):
        try:
            json.loads(path.read_text(encoding='utf-8'))
        except Exception as exc:
            errors.append(f'JSON parse failed: {path.relative_to(ROOT)}: {exc}')


def check_xml() -> None:
    for path in ROOT.glob('*.csproj'):
        try:
            ET.parse(path)
        except Exception as exc:
            errors.append(f'XML parse failed: {path.name}: {exc}')


def check_assets() -> None:
    blocktype_dir = ROOT / 'assets/electricalprogressivecoldstore/blocktypes'
    shape_dir = ROOT / 'assets/electricalprogressivecoldstore/shapes'
    texture_dir = ROOT / 'assets/electricalprogressivecoldstore/textures'

    for path in blocktype_dir.glob('*.json'):
        doc = json.loads(path.read_text(encoding='utf-8'))
        refs: list[str] = []
        if isinstance(doc.get('shape'), dict):
            refs.append(doc['shape'].get('base', ''))
        for value in (doc.get('shapeByType') or {}).values():
            if isinstance(value, dict):
                refs.append(value.get('base', ''))

        for ref in filter(None, refs):
            if ref.startswith('electricalprogressivecoldstore:'):
                rel = ref.split(':', 1)[1] + '.json'
                shape_path = shape_dir / rel
                if not shape_path.exists():
                    errors.append(f'Missing shape {rel}, referenced by {path.name}')

        for tex in (doc.get('textures') or {}).values():
            if not isinstance(tex, dict):
                continue
            ref = tex.get('base', '')
            if ref.startswith('electricalprogressivecoldstore:'):
                rel = ref.split(':', 1)[1] + '.png'
                texture_path = texture_dir / rel
                if not texture_path.exists():
                    errors.append(f'Missing texture {rel}, referenced by {path.name}')

    png_signature = b'\x89PNG\r\n\x1a\n'
    for path in texture_dir.rglob('*.png'):
        try:
            data = path.read_bytes()
            if len(data) < 24 or not data.startswith(png_signature):
                raise ValueError('invalid PNG signature or truncated file')
        except Exception as exc:
            errors.append(f'Invalid PNG {path.relative_to(ROOT)}: {exc}')


def strip_csharp(text: str) -> str:
    out: list[str] = []
    i = 0
    n = len(text)
    state = 'code'
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ''

        if state == 'code':
            if c == '/' and nxt == '/':
                state = 'line-comment'; i += 2; continue
            if c == '/' and nxt == '*':
                state = 'block-comment'; i += 2; continue
            if c == '@' and nxt == '"':
                state = 'verbatim-string'; i += 2; out.append('""'); continue
            if c == '"':
                state = 'string'; i += 1; out.append('""'); continue
            if c == "'":
                state = 'char'; i += 1; out.append("''"); continue
            out.append(c); i += 1; continue

        if state == 'line-comment':
            if c == '\n': state = 'code'; out.append('\n')
            i += 1; continue
        if state == 'block-comment':
            if c == '*' and nxt == '/': state = 'code'; i += 2
            else: i += 1
            continue
        if state == 'string':
            if c == '\\': i += 2
            elif c == '"': state = 'code'; i += 1
            else: i += 1
            continue
        if state == 'verbatim-string':
            if c == '"' and nxt == '"': i += 2
            elif c == '"': state = 'code'; i += 1
            else: i += 1
            continue
        if state == 'char':
            if c == '\\': i += 2
            elif c == "'": state = 'code'; i += 1
            else: i += 1
            continue
    return ''.join(out)


def check_csharp_delimiters() -> None:
    pairs = {'(': ')', '{': '}', '[': ']'}
    reverse = {v: k for k, v in pairs.items()}
    for path in ROOT.rglob('*.cs'):
        clean = strip_csharp(path.read_text(encoding='utf-8'))
        stack: list[tuple[str, int]] = []
        for idx, ch in enumerate(clean):
            if ch in pairs:
                stack.append((ch, idx))
            elif ch in reverse:
                if not stack or stack[-1][0] != reverse[ch]:
                    errors.append(f'C# delimiter mismatch in {path.relative_to(ROOT)} near offset {idx}')
                    break
                stack.pop()
        else:
            if stack:
                errors.append(f'C# unclosed delimiter in {path.relative_to(ROOT)}: {stack[-1][0]}')


def check_registration_links() -> None:
    mod_source = (ROOT / 'ElectricalProgressiveColdStoreMod.cs').read_text(encoding='utf-8')
    registrations = set(re.findall(r'Register(?:Block|BlockEntity|BlockEntityBehavior)Class\("([^"]+)"', mod_source))
    for path in (ROOT / 'assets/electricalprogressivecoldstore/blocktypes').glob('*.json'):
        doc = json.loads(path.read_text(encoding='utf-8'))
        for key in ('class', 'entityClass'):
            value = doc.get(key)
            if value and value not in registrations and value not in {'Block'}:
                errors.append(f'{path.name}: {key}={value} is not registered')
        for behavior in doc.get('entityBehaviors') or []:
            value = behavior.get('name')
            if value and value not in registrations and value != 'ElectricalProgressive':
                errors.append(f'{path.name}: entity behavior {value} is not registered')


def main() -> int:
    check_json()
    check_xml()
    check_assets()
    check_csharp_delimiters()
    check_registration_links()

    print(f'Project: {ROOT}')
    print(f'C# files: {len(list(ROOT.rglob("*.cs")))}')
    print(f'JSON files: {len(list(ROOT.rglob("*.json")))}')
    print(f'PNG files: {len(list(ROOT.rglob("*.png")))}')
    print(f'Errors: {len(errors)}')
    print(f'Warnings: {len(warnings)}')
    for item in errors:
        print('ERROR:', item)
    for item in warnings:
        print('WARNING:', item)
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
