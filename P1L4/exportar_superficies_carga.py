"""Exporta los paneles cerrados por vigas y su reparto tributario."""
import json
from pathlib import Path
from slab_panels import rebuild_slabs, slab_metadata

ROOT=Path(__file__).resolve().parents[1]


def export(data):
    data['slabs']=rebuild_slabs(data)
    rows=slab_metadata(data,data['slabs'])
    output=ROOT/'P1L4/unity_visualizador/Assets/Resources/slab_load_surfaces.json'
    output.write_text(json.dumps(dict(slabs=rows),indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    return rows


if __name__=='__main__':
    data=json.loads((ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8-sig'))
    rows=export(data)
    print('Slabs:',len(rows),'Unmapped boundaries:',sum(not e['beams'] for s in rows for e in s['edges']))
