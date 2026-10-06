"""Checkpoint verified before relocating desktop wall geometry."""
import hashlib
import json
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DEST=ROOT/'revisiones/punto_inicio_muros_positivos_20261006'
DEST.mkdir(exist_ok=True)
archive=DEST/'estado_previo.zip'
if archive.exists():
    raise SystemExit('Checkpoint already exists; refusing to overwrite it.')
folders=['P1L4/desktop_model','P1L4/unity_visualizador/Assets/Scripts','P1L4/unity_visualizador/Assets/Scenes',
         'P1L4/seismic/desktop_results','P1L4/lrfd_results']
files=set()
for folder in folders:
    files.update(p for p in (ROOT/folder).rglob('*') if p.is_file())
files.update(p for p in (ROOT/'P1L4').glob('*.py'))
files.update([ROOT/'P1L4/model_edits.json',ROOT/'revisiones/plan_muros_33_38_z_positivo.md'])
digest=lambda p:hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
manifest={}
with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1) as z:
    for p in sorted(files):
        name=p.relative_to(ROOT).as_posix();manifest[name]=digest(p);z.write(p,name)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for name,expected in manifest.items():
        with z.open(name) as stream:assert hashlib.file_digest(stream,'sha256').hexdigest()==expected,name
(DEST/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf8')
protected=[ROOT/'P1L3/carga_viva_sismo.py',ROOT/'P1L4/exportar_resultados_unity.py',ROOT/'P1L4/seismic/transient_analysis.py',
           ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json',
           ROOT/'P1L4/unity_visualizador_ios/Assets/Resources/estructura_p1l4_unity.json']
(DEST/'protected.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():digest(p) for p in protected},indent=2)+'\n',encoding='utf8')
print(f'PASS checkpoint: {len(manifest)} files, {archive.stat().st_size} bytes; CRC and SHA256 verified.',flush=True)
