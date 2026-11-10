#!/usr/bin/env python3
"""Build a native app and independent background helper. No installation or Surge mutations."""
import argparse
import hashlib
import os
import pathlib
import platform
import plistlib
import re
import shutil
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--output', type=pathlib.Path, default=ROOT / 'artifacts')
p.add_argument('--version', default=(ROOT / 'VERSION').read_text().strip())
p.add_argument('--build-number', default=os.environ.get('GITHUB_RUN_NUMBER', '1'))
args = p.parse_args()
if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', args.version):
    p.error('version must be a three-part numeric version, such as 1.0.0')
if not re.fullmatch(r'[1-9]\d*', args.build_number):
    p.error('build number must be a positive integer')
if platform.system() != 'Darwin' or platform.machine() not in ('arm64', 'x86_64'):
    p.error('packaging requires an Apple Silicon or Intel Mac')
commit = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
rid = 'osx-arm64' if platform.machine() == 'arm64' else 'osx-x64'
app = args.output.resolve() / 'SurgeSub.app'
if app.exists():
    shutil.rmtree(app)
macos = app / 'Contents/MacOS'
resources = app / 'Contents/Resources'
macos.mkdir(parents=True)
resources.mkdir(parents=True)
for project in ('SurgeSub.App', 'SurgeSub.Agent'):
    out = args.output.resolve() / ('publish-' + project)
    if out.exists():
        shutil.rmtree(out)
    subprocess.run(['dotnet', 'publish', str(ROOT / 'src' / project / (project + '.csproj')),
                    '-c', 'Release', '-r', rid, '--self-contained', 'true', '-o', str(out),
                    '-p:Version=' + args.version, '-p:SourceRevisionId=' + commit], check=True)
    for item in out.iterdir():
        if item.name.endswith(('.pdb', '.dSYM')):
            continue
        target = macos / item.name
        if item.is_dir():
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)
info = {
    'CFBundleName': 'SurgeSub', 'CFBundleDisplayName': 'SurgeSub',
    'CFBundleIdentifier': 'com.arcforges.surgesub', 'CFBundleVersion': args.build_number,
    'CFBundleShortVersionString': args.version, 'CFBundlePackageType': 'APPL',
    'CFBundleExecutable': 'SurgeSub.App', 'LSMinimumSystemVersion': '13.0',
    'NSHighResolutionCapable': True,
    'SurgeSubSourceRevision': commit,
}
with (app / 'Contents/Info.plist').open('wb') as file:
    plistlib.dump(info, file)
subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(app)], check=True)
subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
zip_path = args.output.resolve() / ('SurgeSub-' + rid + '.zip')
zip_path.unlink(missing_ok=True)
subprocess.run(['ditto', '-c', '-k', '--keepParent', str(app), str(zip_path)], check=True)
dmg_path = args.output.resolve() / ('SurgeSub-' + rid + '.dmg')
with tempfile.TemporaryDirectory(prefix='surgesub-dmg-') as temporary:
    stage = pathlib.Path(temporary)
    shutil.copytree(app, stage / app.name, symlinks=True)
    (stage / 'Applications').symlink_to('/Applications', target_is_directory=True)
    (stage / '安装说明.txt').write_text(
        f'SurgeSub {args.version} ({rid})\n\n'
        '将 SurgeSub.app 拖到 Applications，再从应用程序目录打开。\n'
        '后台任务引用应用所在路径，启用后请不要移动应用。\n\n'
        '当前版本使用临时签名，尚未经过 Apple 公证。若 macOS 阻止打开，\n'
        '请先核对下载来源和 SHA-256，再到系统设置 → 隐私与安全性 → 仍要打开。\n'
        '已有版本请先退出界面、替换 App，再打开并点击生成并应用，以更新后台组件。\n\n'
        '不会在安装时自动切换 Surge 配置。\n'
        'https://github.com/deku2026/SurgeSub\n', encoding='utf-8')
    subprocess.run(['hdiutil', 'create', '-volname', 'SurgeSub ' + args.version,
                    '-srcfolder', str(stage), '-format', 'UDZO', '-fs', 'HFS+',
                    '-ov', str(dmg_path)], check=True)
subprocess.run(['hdiutil', 'verify', str(dmg_path)], check=True)
checksum_path = args.output.resolve() / ('SurgeSub-' + rid + '.sha256')
with checksum_path.open('w') as file:
    for asset in (dmg_path, zip_path):
        with asset.open('rb') as content:
            digest = hashlib.sha256()
            for chunk in iter(lambda: content.read(1024 * 1024), b''):
                digest.update(chunk)
        file.write(f'{digest.hexdigest()}  {asset.name}\n')
print(zip_path)
print(dmg_path)
print(checksum_path)
