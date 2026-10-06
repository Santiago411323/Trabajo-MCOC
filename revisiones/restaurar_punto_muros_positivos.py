"""Explicit manual rollback of the verified pre-relocation desktop checkpoint."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--restaurar',action='store_true',required=True,help='Overwrite checkpoint-covered files with their pre-relocation versions.')
parser.parse_args()
folder=ROOT/'revisiones/punto_inicio_muros_positivos_20261006'
manifest=json.loads((folder/'manifest.json').read_text())
with zipfile.ZipFile(folder/'estado_previo.zip') as z:
    assert z.testzip() is None
    for name,digest in manifest.items():
        target=(ROOT/name).resolve()
        assert target.is_relative_to(ROOT.resolve()),name
        with z.open(name) as stream:assert hashlib.file_digest(stream,'sha256').hexdigest()==digest,name
    for name in manifest:
        target=ROOT/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(z.read(name))
for name,digest in manifest.items():
    with (ROOT/name).open('rb') as stream:assert hashlib.file_digest(stream,'sha256').hexdigest()==digest,name
print('PASS: desktop model, pre-existing scripts and static/dynamic/LRFD results restored from verified checkpoint. New audit files remain for traceability.')
