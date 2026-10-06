"""Empaqueta ocho corridas completas, sin recalcular ni modificar resultados."""
from pathlib import Path
import gzip
import hashlib
import json
import struct
import argparse

ROOT=Path(__file__).resolve().parents[2]
MODEL=ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json'

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',type=Path,default=Path(__file__).resolve().parent/'results')
    args=parser.parse_args()
    model_hash=hashlib.sha256(MODEL.read_bytes()).hexdigest()
    outputs=[ROOT/'P1L4'/p/'Assets/Resources/MobileSeismic' for p in ('unity_visualizador','unity_visualizador_ios')]
    for folder in outputs: folder.mkdir(exist_ok=True)
    total=0
    for direction in ('X','Y'):
        for intensity in (25,50,100,150):
            name=f'el_centro_1940_ns_{direction}_i{intensity:03d}'
            source=args.source/f'{name}.json'
            raw=source.read_bytes();meta=json.loads(raw)
            assert meta['modelHash']==model_hash and meta['complete'] and not meta['truncated']
            payload=source.with_name(meta['binaryFile']).read_bytes()
            assert hashlib.sha256(payload).hexdigest()==meta['binarySha256']
            assert payload[:8]==b'MCOCSIS1'
            assert struct.unpack('<iii',payload[8:20])==(len(meta['nodeTags']),len(meta['members']),meta['frameCount'])
            assert len(payload)==20+4*meta['stride']*meta['frameCount']
            packed=gzip.compress(payload,compresslevel=6,mtime=0)
            for folder in outputs:
                (folder/f'{name}.json').write_bytes(raw)
                (folder/f'{name}_payload.bytes').write_bytes(packed)
            total+=len(packed)
    assert MODEL.read_bytes()==(ROOT/'P1L4/unity_visualizador_ios/Assets/Resources/estructura_p1l4_unity.json').read_bytes()
    print(f'PASS: ocho casos completos; hashes verificados; {total/1024/1024:.1f} MiB comprimidos por aplicación.')

if __name__=='__main__':main()
