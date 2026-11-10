#!/usr/bin/env python3
"""Build a native app and independent background helper. No installation or Surge mutations."""
import argparse
import pathlib
import platform
import plistlib
import shutil
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--output', type=pathlib.Path, default=ROOT / 'artifacts')
args = p.parse_args()
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
    subprocess.run(['dotnet', 'publish', str(ROOT / 'src' / project / (project + '.csproj')),
                    '-c', 'Release', '-r', rid, '--self-contained', 'true', '-o', str(out)], check=True)
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
    'CFBundleIdentifier': 'com.arcforges.surgesub', 'CFBundleVersion': '1',
    'CFBundleShortVersionString': '1.0.0', 'CFBundlePackageType': 'APPL',
    'CFBundleExecutable': 'SurgeSub.App', 'LSMinimumSystemVersion': '13.0',
    'NSHighResolutionCapable': True,
}
with (app / 'Contents/Info.plist').open('wb') as file:
    plistlib.dump(info, file)
subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(app)], check=True)
zip_path = args.output.resolve() / ('SurgeSub-' + rid + '.zip')
subprocess.run(['ditto', '-c', '-k', '--keepParent', str(app), str(zip_path)], check=True)
print(zip_path)
