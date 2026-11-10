import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.assets = Path(self.temp.name)
        self.calls = []
        self.existing = None
        self.fail_upload = False
        for rid in ('osx-arm64', 'osx-x64'):
            lines = []
            for extension in ('dmg', 'zip'):
                asset = self.assets / f'SurgeSub-{rid}.{extension}'
                asset.write_bytes((rid + extension).encode())
                lines.append(f'{hashlib.sha256(asset.read_bytes()).hexdigest()}  {asset.name}\n')
            (self.assets / f'SurgeSub-{rid}.sha256').write_text(''.join(lines))

    def gh(self, args, **kwargs):
        self.calls.append(args)
        if args[1] == 'api':
            if self.existing is None:
                return subprocess.CompletedProcess(args, 1, '', 'gh: Not Found (HTTP 404)')
            return subprocess.CompletedProcess(args, 0, json.dumps(self.existing), '')
        if args[1:3] == ['release', 'upload'] and self.fail_upload:
            raise subprocess.CalledProcessError(1, args)
        return subprocess.CompletedProcess(args, 0, '', '')

    def publish(self):
        argv = ['publish-release.py', '--assets', str(self.assets), '--tag',
                'v' + (ROOT / 'VERSION').read_text().strip(), '--commit', 'abc123']
        with patch.object(sys, 'argv', argv), patch.dict(os.environ, {'GITHUB_REPOSITORY': 'test/repo'}), patch('subprocess.run', self.gh):
            runpy.run_path(str(ROOT / 'scripts/publish-release.py'), run_name='__main__')

    def test_upload_all_architectures_before_publishing(self):
        self.publish()
        self.assertIn('--draft', self.calls[1])
        self.assertEqual(self.calls[2][1:3], ['release', 'upload'])
        self.assertEqual(sum(str(x).endswith(('.dmg', '.zip', '.sha256')) for x in self.calls[2]), 6)
        self.assertIn('--draft=false', self.calls[3])

    def test_upload_failure_never_publishes_draft(self):
        self.fail_upload = True
        with self.assertRaises(subprocess.CalledProcessError):
            self.publish()
        self.assertFalse(any('--draft=false' in args for args in self.calls))

    def test_corrupt_asset_never_contacts_github(self):
        (self.assets / 'SurgeSub-osx-x64.dmg').write_bytes(b'corrupt')
        with self.assertRaisesRegex(RuntimeError, 'Checksum mismatch'):
            self.publish()
        self.assertEqual(self.calls, [])

    def test_missing_architecture_never_contacts_github(self):
        (self.assets / 'SurgeSub-osx-x64.sha256').unlink()
        with self.assertRaises(FileNotFoundError):
            self.publish()
        self.assertEqual(self.calls, [])

    def test_complete_published_release_is_not_overwritten(self):
        self.existing = {'draft': False, 'target_commitish': 'abc123',
                         'assets': [{'name': p.name} for p in self.assets.iterdir()]}
        with self.assertRaises(SystemExit) as stopped:
            self.publish()
        self.assertEqual(stopped.exception.code, 0)
        self.assertEqual(len(self.calls), 1)

    def test_other_commit_release_is_not_overwritten(self):
        self.existing = {'draft': False, 'target_commitish': 'other', 'assets': []}
        with self.assertRaisesRegex(RuntimeError, 'refusing to replace'):
            self.publish()
        self.assertEqual(len(self.calls), 1)
