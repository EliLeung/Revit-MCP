"""Isolated Windows installer tests. No real Revit registration is changed."""
from pathlib import Path
import argparse, hashlib, json, subprocess, tempfile, shutil

args = argparse.ArgumentParser()
args.add_argument('--package', required=True)
options = args.parse_args()
package = Path(options.package).resolve()
with tempfile.TemporaryDirectory(prefix='h2-revit-installer-') as directory:
    root = Path(directory)
    sandbox = root / 'sandbox'
    def run(*extra, ok=True, source=package):
        result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(source/'Install.ps1'), '-TestRoot', str(sandbox), *extra], text=True, capture_output=True)
        assert (result.returncode == 0) == ok, result.stdout + result.stderr
        return result
    run('-WhatIf')
    assert not sandbox.exists(), 'Dry-run wrote files'
    run()
    state_path = sandbox/'LocalAppData/H2-Revit/install-state.json'
    state = json.loads(state_path.read_text(encoding='utf-8-sig'))
    target = Path(state['target'])
    assert target.exists() and 'H2-Revit' in target.read_text()
    run()
    run('-Uninstall')
    assert not target.exists()
    run('-Uninstall')
    print('PASS real package dry-run, install, idempotent install, fresh uninstall')
    # Existing upstream registration is backed up and restored exactly in content.
    original = '<?xml version="1.0"?><RevitAddIns><AddIn Type="Application"><Name>PriorPlugin</Name><Assembly>prior.dll</Assembly><AddInId>{5e077288-82fd-4b2f-9f4e-a1849c38bb00}</AddInId></AddIn></RevitAddIns>'
    original_bytes = b'\xef\xbb\xbf' + original.encode('utf-8')
    target.write_bytes(original_bytes)
    run()
    run('-Uninstall')
    assert target.read_bytes() == original_bytes
    print('PASS existing registration backup and restore')
    run()
    installed_bytes = target.read_bytes()
    target.write_bytes(installed_bytes + b'<!--later user change-->')
    run('-Uninstall', ok=False)
    assert 'later user change' in target.read_text()
    target.write_bytes(installed_bytes)
    run('-Uninstall')
    print('PASS restore protects later user changes')
    # A tiny package fixture exercises integrity failure without altering the actual package.
    fixture = root/'corrupt-package'; fixture.mkdir()
    shutil.copy2(package/'Install.ps1', fixture/'Install.ps1')
    (fixture/'payload').mkdir()
    (fixture/'payload/test.bin').write_bytes(b'changed')
    (fixture/'payload-sha256.json').write_text(json.dumps({'test.bin': hashlib.sha256(b'expected').hexdigest()}))
    run(ok=False, source=fixture)
    assert target.read_bytes() == original_bytes
    print('PASS corrupted payload refused without changing registration')
