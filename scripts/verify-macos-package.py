#!/usr/bin/env python3
"""Verify the installable DMG and ZIP without installing or changing Surge."""
import argparse
import hashlib
import pathlib
import plistlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--output', type=pathlib.Path, default=ROOT / 'artifacts')
p.add_argument('--rid', choices=['osx-arm64', 'osx-x64'], required=True)
args = p.parse_args()
output = args.output.resolve()
version = (ROOT / 'VERSION').read_text().strip()
commit = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
for line in (output / f'SurgeSub-{args.rid}.sha256').read_text().splitlines():
    expected, name = line.split('  ', 1)
    with (output / name).open('rb') as file:
        digest = hashlib.sha256()
        for chunk in iter(lambda: file.read(1024 * 1024), b''):
            digest.update(chunk)
        if digest.hexdigest() != expected:
            raise RuntimeError('Checksum mismatch: ' + name)


def verify_app(app):
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if info['CFBundleShortVersionString'] != version or info['SurgeSubSourceRevision'] != commit:
        raise RuntimeError('Packaged app version or source commit does not match this checkout')
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    architecture = 'arm64' if args.rid == 'osx-arm64' else 'x86_64'
    for binary in ('SurgeSub.App', 'SurgeSub.Agent'):
        path = app / 'Contents/MacOS' / binary
        actual = subprocess.check_output(['lipo', '-archs', str(path)], text=True).strip()
        if actual != architecture:
            raise RuntimeError(f'{binary}: expected {architecture}, got {actual}')
    subprocess.run([str(app / 'Contents/MacOS/SurgeSub.Agent'), '--help'], check=True, timeout=15)


with tempfile.TemporaryDirectory(prefix='surgesub-verify-') as temporary:
    root = pathlib.Path(temporary)
    subprocess.run(['ditto', '-x', '-k', str(output / f'SurgeSub-{args.rid}.zip'), str(root / 'zip')], check=True)
    verify_app(root / 'zip/SurgeSub.app')
    mount = root / 'mounted'
    mount.mkdir()
    subprocess.run(['hdiutil', 'attach', '-readonly', '-nobrowse', '-mountpoint', str(mount),
                    str(output / f'SurgeSub-{args.rid}.dmg')], check=True)
    try:
        if not (mount / 'Applications').is_symlink() or (mount / 'Applications').readlink() != pathlib.Path('/Applications'):
            raise RuntimeError('DMG is missing the Applications shortcut')
        if not (mount / '安装说明.txt').is_file():
            raise RuntimeError('DMG is missing installation instructions')
        verify_app(mount / 'SurgeSub.app')
    finally:
        subprocess.run(['hdiutil', 'detach', str(mount)], check=True)
print('DMG and ZIP verified: signatures, version, commit, architecture and helper startup.')
