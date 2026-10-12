from pathlib import Path
import sys, zipfile, hashlib, json, re
root=Path(sys.argv[1])
player=root/'Star-Renegades-Bugfix-Mod-v0.1.0.zip'
source=root/'Star-Renegades-Bugfix-Mod-v0.1.0-source.zip'
def scan(z, kind):
    names=z.namelist()
    assert len(names)==len(set(names)), 'Duplicate archive paths'
    forbidden=['assembly-csharp.dll','data.unity3d','verify.flag','rebalance.json','receipt.json','.git/','.superpowers/','decompiled','rollback','bugfix.log','probe-values','__pycache__']
    for name in names:
        assert not name.startswith('/') and '..' not in Path(name).parts, 'Archive traversal'
        assert not any(f in name.lower() for f in forbidden), 'Private or game file: '+name
        if name.endswith(('.cs','.py','.ps1','.md','.yml','.json','.cmd')):
            data=z.read(name).decode('utf-8-sig')
            assert not re.search(r'C:[/\\]Users[/\\](payto|[^/\\]+[/\\]Documents)',data,re.I), 'Private path: '+name
            assert '\u2014' not in data and '\u2013' not in data, 'Prose dash: '+name
    if kind=='player':
        package=json.loads(z.read('package.json'))
        assert set(package['files'])=={'winhttp.dll','doorstop_config.ini','SRBugfix/SRBugfix.dll','SRBugfix/0Harmony.dll','SRBugfix/fixes.json'}
        for name,digest in package['files'].items(): assert hashlib.sha256(z.read(name)).hexdigest()==digest, 'Payload checksum: '+name
        manifest=json.loads(z.read('SRBugfix/fixes.json'))
        assert len(manifest['changes'])==346 and len(manifest['additions'])==36
        assert not any(n.startswith(('Tests/','Source/')) for n in names), 'Developer files in player ZIP'
    else:
        assert 'Source/ReflectionPreviewFix.cs' in names and 'Installer/Support.ps1' in names
        assert not any(n.endswith('.dll') for n in names), 'Binary in source ZIP'
    assert 'ThirdParty/UnityDoorstop-v4.0.0-source.zip' in names, 'Missing Doorstop source'
    with zipfile.ZipFile(__import__('io').BytesIO(z.read('ThirdParty/UnityDoorstop-v4.0.0-source.zip'))) as dep:
        assert 'UnityDoorstop-4.0.0/xmake.lua' in dep.namelist()
        assert 'UnityDoorstop-4.0.0/src/windows/proxy/proxy.h' in dep.namelist()
for path,kind in [(player,'player'),(source,'source')]:
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None
        scan(z,kind)
    digest=hashlib.sha256(path.read_bytes()).hexdigest()
    assert path.with_suffix(path.suffix+'.sha256').read_text().split()[0]==digest
    print('PASS',kind,'archive integrity, content boundaries, source/notices and checksums')
# Prove the audit catches accidental game data even if someone appends it to a ZIP.
import io
buffer=io.BytesIO()
with zipfile.ZipFile(player) as original, zipfile.ZipFile(buffer,'w') as bad:
    for name in original.namelist():bad.writestr(name,original.read(name))
    bad.writestr('Assembly-CSharp.dll',b'fixture')
try:
    with zipfile.ZipFile(buffer) as bad:scan(bad,'player')
except AssertionError:print('PASS injected forbidden game assembly rejected')
else:raise AssertionError('Injected private data accepted')
