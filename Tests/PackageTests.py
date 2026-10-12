from pathlib import Path
import sys, zipfile, hashlib, json, re
root=Path(sys.argv[1])
player=root/'Star-Renegades-Bugfix-Mod-v0.1.1.zip'
source=root/'Star-Renegades-Bugfix-Mod-v0.1.1-source.zip'
common={'README.txt','BUILD.txt','CHANGELOG.txt','RELEASE-NOTES.txt','THIRD-PARTY-NOTICES.txt','LICENSE',
        'ThirdParty/Doorstop-LICENSE.txt','ThirdParty/Harmony-LICENSE.txt','ThirdParty/MonoMod-LICENSE.txt',
        'ThirdParty/Mono.Cecil-LICENSE.txt','ThirdParty/SR-Tweaker-LICENSE.txt','ThirdParty/UnityDoorstop-v4.0.0-source.zip',
        'Installer/Support.ps1','Installer/Install.ps1','Installer/Uninstall.ps1'}
expected={
    'player':common|{'Install.cmd','Uninstall.cmd','package.json','winhttp.dll','doorstop_config.ini',
                     'SRBugfix/SRBugfix.dll','SRBugfix/0Harmony.dll','SRBugfix/fixes.json'},
    'source':common|{'Source/'+n+'.cs' for n in ['Bootstrap','Overlay','BuiltManifest','Activation','PreviewFix',
                     'ReflectionPreviewFix','DamageInspectionFix','ProjectedEffectFix','AchievementFix','Entrypoint','RuntimeProbe']}|
             {'Source/fixes.json','Source/core-fixes.json','Source/affix-fixes.json',
              'Installer/Install.cmd','Installer/Uninstall.cmd','Installer/doorstop_config.ini',
              'Tests/CoreTests.cs','Tests/PreviewTests.cs','Tests/PredictionTests.cs','Tests/ForecastFollowupTests.cs','Tests/AchievementTests.cs','Tests/ActivationTests.cs',
              'Tests/run-forecast-followup.ps1','Tests/RunAchievementTests.ps1','Tests/card-preview-cases.json',
              'Tests/Installer.Tests.ps1','Tests/PackageTests.py','.github/ISSUE_TEMPLATE/bug_report.yml',
              '.gitignore','build.ps1','setup-dependencies.ps1','make_manifest.py','package.py'}
}
def scan(z, kind):
    names=z.namelist()
    assert len(names)==len(set(names)), 'Duplicate archive paths'
    assert set(names)==expected[kind], 'Archive entries differ from the complete public allowlist'
    assert not any(name.lower().endswith('.md') for name in names), 'Use plain-text documentation in this release'
    forbidden=['assembly-csharp.dll','data.unity3d','verify.flag','rebalance.json','receipt.json','.git/','.superpowers/','decompiled','rollback','bugfix.log','probe-values','__pycache__']
    for name in names:
        assert not name.startswith('/') and '..' not in Path(name).parts, 'Archive traversal'
        assert not any(f in name.lower() for f in forbidden), 'Private or game file: '+name
        if name.endswith(('.cs','.py','.ps1','.txt','.yml','.json','.cmd')):
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
# An unlisted developer executable must be rejected in either archive.
for path,kind in [(player,'player'),(source,'source')]:
    buffer=io.BytesIO()
    with zipfile.ZipFile(path) as original, zipfile.ZipFile(buffer,'w') as bad:
        for name in original.namelist():bad.writestr(name,original.read(name))
        bad.writestr('bin/CoreTests.exe',b'fixture')
    try:
        with zipfile.ZipFile(buffer) as bad:scan(bad,kind)
    except AssertionError:print('PASS',kind,'injected developer executable rejected')
    else:raise AssertionError(kind+' archive accepted an unlisted developer executable')
