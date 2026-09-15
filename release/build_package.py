# H2-Revit package builder; Apache-2.0.
from pathlib import Path
import json,shutil,hashlib,xml.etree.ElementTree as ET,urllib.request
repo=Path(__file__).resolve().parents[1]
import subprocess
subprocess.run(['dotnet','build',str(repo/'src/plugin-r26/RvtMcp.Plugin.R26.csproj'),'-c','Release','-p:RvtMcpSkipDeploy=true','-p:DebugType=None','-p:DebugSymbols=false'],check=True)
subprocess.run(['dotnet','publish',str(repo/'src/server/RvtMcp.Server.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',str(repo/'artifacts/server')],check=True)
package=(repo/'artifacts/H2-Revit-v1.0-Revit2026-win-x64');package.mkdir(parents=True,exist_ok=True)
for name in ['Install.ps1','Install.cmd','Uninstall.cmd']:
 shutil.copy2(repo/'release'/name,package/name)
payload=package/'payload';plugin=payload/'plugin';server=payload/'server'
plugin.mkdir(parents=True,exist_ok=True);server.mkdir(parents=True,exist_ok=True)
build=(repo/'src/plugin-r26/bin/Release/net8.0-windows7.0')
for p in build.glob('*.dll'):
 assert not p.name.lower().startswith('revitapi')
 shutil.copy2(p,plugin/p.name)
shutil.copy2(build/'runtimes/win-x64/native/e_sqlite3.dll',plugin/'e_sqlite3.dll')
shutil.copy2(repo/'artifacts/server/RvtMcp.Server.exe',server/'H2-Revit-MCP.exe')
for name in ['LICENSE','THIRD-PARTY-NOTICES.md']:
 shutil.copy2(repo/name,payload/name);shutil.copy2(repo/name,package/name)
licenses=payload/'licenses';licenses.mkdir(exist_ok=True)
cache=Path.home()/'.nuget/packages'
packages={}
for assets in [repo/'src/plugin-r26/obj/project.assets.json',repo/'src/server/obj/project.assets.json']:
 data=json.loads(Path(assets).read_text())
 for name,info in data['libraries'].items():
  if info['type']=='package' and not name.lower().startswith('nice3point.revit.api.'):
   packages[name]=cache/info['path']
for family in ['microsoft.netcore.app.runtime.win-x64','microsoft.aspnetcore.app.runtime.win-x64']:
 for p in (cache/family).iterdir():
  if p.is_dir():packages[family+'/'+p.name]=p
rows=[];missing=[]
for name,path in sorted(packages.items()):
 nuspec=next(path.glob('*.nuspec'));xml=ET.parse(nuspec)
 def field(n):return next((e for e in xml.iter() if e.tag.split('}')[-1]==n),None)
 lic=field('license');copy=field('copyright');author=field('authors');url=field('projectUrl')
 text=lic.text if lic is not None else 'See package license files'
 out=licenses/name.replace('/','-');out.mkdir(exist_ok=True)
 found=[]
 for p in path.rglob('*'):
  if p.is_file() and any(x in p.name.lower() for x in ['license','notice','copying','copyright']) and p.suffix.lower() in ['.txt','.md','']:
   target=out/p.name
   shutil.copy2(p,target);found.append(p.name)
 if not any('license' in n.lower() or 'copying' in n.lower() for n in found) and lic is not None and lic.attrib.get('type')=='expression':
  expr=lic.text
  request=urllib.request.Request('https://raw.githubusercontent.com/spdx/license-list-data/main/text/'+expr+'.txt',headers={'User-Agent':'H2-Revit-license-bundle'})
  try:
   license_text=urllib.request.urlopen(request,timeout=30).read()
   (out/'LICENSE.txt').write_bytes(license_text);found.append('LICENSE.txt')
  except Exception as e:missing.append((name,text,str(e)))
 elif not found:missing.append((name,text,'No license text found'))
 copyright_text=copy.text if copy is not None else ''
 author_text=author.text if author is not None else ''
 (out/'ATTRIBUTION.txt').write_text(f'{name}\nAuthors: {author_text}\nCopyright: {copyright_text}\nLicense: {text}\nProject: {url.text if url is not None else ""}\n',encoding='utf-8')
 rows.append({'package':name,'license':text,'authors':author_text,'copyright':copyright_text,'files':found})
(licenses/'inventory.json').write_text(json.dumps(rows,indent=2,ensure_ascii=False),encoding='utf-8')
(licenses/'README.md').write_text('# Third-party package licenses\n\nIncludes runtime dependencies and conservative additional build dependency notices. Revit API binaries are not included.\n',encoding='utf-8')
assert not missing,missing
hashes={p.relative_to(payload).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(payload.rglob('*')) if p.is_file()}
(package/'payload-sha256.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
print(json.dumps({'package':str(package),'license_packages':len(rows),'payload_files':len(hashes)}))

shutil.copy2(repo/'README.md',package/'README.md')
shutil.copy2(repo/'release/RELEASE-NOTES.md',package/'RELEASE-NOTES.md')
shutil.copy2(repo/'docs/H2-VALIDATION.md',package/'VALIDATION.md')
(package/'docs').mkdir(exist_ok=True)
shutil.copy2(repo/'docs/h2-connection-manager.png',package/'docs/h2-connection-manager.png')
archive=shutil.make_archive(str(package),'zip',root_dir=package.parent,base_dir=package.name)
digest=hashlib.sha256(Path(archive).read_bytes()).hexdigest()
Path(archive+'.sha256').write_text(digest+'  '+Path(archive).name+'\n',encoding='ascii')
print('Release archive: '+archive)
