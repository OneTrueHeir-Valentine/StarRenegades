"""Build release archives from explicit file lists; no workspace-wide copying."""
import argparse, hashlib, json, zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent
VERSION='0.1.1'
DOCS=['README.txt','BUILD.txt','CHANGELOG.txt','RELEASE-NOTES.txt','THIRD-PARTY-NOTICES.txt','LICENSE']
THIRD=['ThirdParty/Doorstop-LICENSE.txt','ThirdParty/Harmony-LICENSE.txt','ThirdParty/MonoMod-LICENSE.txt','ThirdParty/Mono.Cecil-LICENSE.txt','ThirdParty/SR-Tweaker-LICENSE.txt','ThirdParty/UnityDoorstop-v4.0.0-source.zip']
CODE=['Bootstrap','Overlay','BuiltManifest','Activation','PreviewFix','ReflectionPreviewFix','DamageInspectionFix','ProjectedEffectFix','AchievementFix','Entrypoint','RuntimeProbe']
INSTALL=['Installer/Support.ps1','Installer/Install.ps1','Installer/Uninstall.ps1']
SOURCE=DOCS+THIRD+INSTALL+['Source/'+n+'.cs' for n in CODE]+[
 'Source/fixes.json','Source/core-fixes.json','Source/affix-fixes.json',
 'Installer/Install.cmd','Installer/Uninstall.cmd','Installer/doorstop_config.ini',
 'Tests/CoreTests.cs','Tests/PreviewTests.cs','Tests/PredictionTests.cs','Tests/ForecastFollowupTests.cs','Tests/AchievementTests.cs','Tests/ActivationTests.cs',
 'Tests/run-forecast-followup.ps1','Tests/RunAchievementTests.ps1','Tests/card-preview-cases.json','Tests/Installer.Tests.ps1','Tests/PackageTests.py',
 '.github/ISSUE_TEMPLATE/bug_report.yml','.gitignore','build.ps1','setup-dependencies.ps1','make_manifest.py','package.py']
PAYLOAD={'winhttp.dll':'dependencies/winhttp.dll','doorstop_config.ini':'Installer/doorstop_config.ini','SRBugfix/SRBugfix.dll':'bin/SRBugfix.dll','SRBugfix/0Harmony.dll':'dependencies/0Harmony.dll','SRBugfix/fixes.json':'Source/fixes.json'}
PINNED={'dependencies/winhttp.dll':'7e90398dd7154b41f76e0524aedb795a7bcbe2ed9a14e4ceee214235aa04e442','dependencies/0Harmony.dll':'9611251f080e4855cd9d5fa54b5e56034df25b70b374c49ba09550b08a1bf875'}
def digest(data):return hashlib.sha256(data).hexdigest()
def archive(path, entries):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
  for name,data in sorted(entries.items()):
   info=zipfile.ZipInfo(name,date_time=(2026,10,12,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o644<<16;z.writestr(info,data)
 path.with_suffix(path.suffix+'.sha256').write_text(digest(path.read_bytes())+'  '+path.name+'\n',encoding='ascii')
def build(output):
 output.mkdir(parents=True,exist_ok=True)
 for name,expected in PINNED.items():
  if digest((ROOT/name).read_bytes())!=expected:raise ValueError('Dependency checksum mismatch: '+name)
 player={name:(ROOT/source).read_bytes() for name,source in PAYLOAD.items()}
 package={'format':1,'version':VERSION,'files':{name:digest(data) for name,data in player.items()}}
 player['package.json']=(json.dumps(package,indent=2)+'\n').encode()
 for name in DOCS+THIRD+INSTALL:player[name]=(ROOT/name).read_bytes()
 for name in ['Install','Uninstall']:player[name+'.cmd']=(ROOT/'Installer'/ (name+'.cmd')).read_bytes()
 source={name:(ROOT/name).read_bytes() for name in SOURCE}
 archive(output/('Star-Renegades-Bugfix-Mod-v'+VERSION+'.zip'),player)
 archive(output/('Star-Renegades-Bugfix-Mod-v'+VERSION+'-source.zip'),source)
 print('Built player and source archives in',output)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=ROOT/'artifacts');build(p.parse_args().output.resolve())
