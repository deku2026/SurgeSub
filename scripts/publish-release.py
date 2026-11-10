#!/usr/bin/env python3
"""Publish validated artifacts together, keeping incomplete uploads in a draft."""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--assets', type=pathlib.Path, required=True)
p.add_argument('--tag', required=True)
p.add_argument('--commit', required=True)
args = p.parse_args()
version = (ROOT / 'VERSION').read_text().strip()
if args.tag != 'v' + version:
    p.error('tag must match VERSION')
assets = []
for rid in ('osx-arm64', 'osx-x64'):
    manifest = args.assets / f'SurgeSub-{rid}.sha256'
    entries = manifest.read_text().splitlines()
    expected_names = {f'SurgeSub-{rid}.dmg', f'SurgeSub-{rid}.zip'}
    if len(entries) != 2:
        raise RuntimeError('Invalid checksum manifest: ' + str(manifest))
    for line in entries:
        digest, name = line.split('  ', 1)
        if name not in expected_names:
            raise RuntimeError('Unexpected or duplicate release asset: ' + name)
        expected_names.remove(name)
        asset = args.assets / name
        with asset.open('rb') as file:
            actual = hashlib.sha256()
            for chunk in iter(lambda: file.read(1024 * 1024), b''):
                actual.update(chunk)
            if actual.hexdigest() != digest:
                raise RuntimeError('Checksum mismatch: ' + name)
        assets.append(asset)
    assets.append(manifest)
repo = os.environ['GITHUB_REPOSITORY']
result = subprocess.run(['gh', 'api', f'repos/{repo}/releases/tags/{args.tag}'], capture_output=True, text=True)
release = None
if result.returncode == 0:
    release = json.loads(result.stdout)
elif 'HTTP 404' not in result.stderr:
    raise RuntimeError('Unable to check the existing release: ' + result.stderr)
if release and not release['draft']:
    if release['target_commitish'] != args.commit or {a['name'] for a in release['assets']} != {a.name for a in assets}:
        raise RuntimeError('A different or incomplete published release already exists; refusing to replace it')
    print('This commit is already published with all expected assets.')
    raise SystemExit(0)
notes = f'''SurgeSub {version}

- Apple Silicon：下载 `SurgeSub-osx-arm64.dmg`。
- Intel Mac：下载 `SurgeSub-osx-x64.dmg`。
- 打开 DMG，把 SurgeSub.app 拖到 Applications。ZIP 为备用下载。
- 应用常驻顶部菜单栏，右键可打开设置、立即同步或退出；自动同步默认关闭。
- `.sha256` 文件用于校验对应架构的 DMG 与 ZIP（`shasum -a 256 -c 文件名.sha256`）。

需要 macOS 13+、Surge Mac 6.10+ 和支持 client-bundle 的 SubVault。
当前使用临时签名，尚未做 Apple Developer ID 签名和公证。首次被 macOS 阻止时，核对来源后到“系统设置 → 隐私与安全性 → 仍要打开”。
更新已有安装时，从菜单退出 SurgeSub、替换 App，再打开即可；需要更新配置时再点击“立即同步”。启动或打开设置窗口不会触发同步，只有主动勾选“自动同步”并应用才启用周期更新。

源代码提交：`{args.commit}`
'''
with tempfile.TemporaryDirectory(prefix='surgesub-release-') as temporary:
    notes_file = pathlib.Path(temporary) / 'notes.md'
    notes_file.write_text(notes)
    if not release:
        subprocess.run(['gh', 'release', 'create', args.tag, '--verify-tag', '--draft',
                        '--target', args.commit, '--title', 'SurgeSub ' + args.tag,
                        '--notes-file', str(notes_file)], check=True)
    else:
        if release['target_commitish'] != args.commit:
            raise RuntimeError('Existing draft belongs to a different commit')
        subprocess.run(['gh', 'release', 'edit', args.tag, '--notes-file', str(notes_file)], check=True)
    subprocess.run(['gh', 'release', 'upload', args.tag, *map(str, assets), '--clobber'], check=True)
    subprocess.run(['gh', 'release', 'edit', args.tag, '--draft=false', '--latest'], check=True)
