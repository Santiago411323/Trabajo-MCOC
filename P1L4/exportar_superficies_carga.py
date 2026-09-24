"""Export slab load metadata without changing/recomputing G/Q analysis results."""
import json
from pathlib import Path
from mobile_slab_worker import edges, transfer

ROOT=Path(__file__).resolve().parents[1]


def export(data):
    source=json.loads((ROOT/'edificio_2/modelo_python/structural_geometry.json').read_text(encoding='utf-8-sig'))
    profiles={s['id']:s.get('load_profile') for s in source['rigid_diaphragms']}
    rows=[]
    for s in data['slabs']:
        profile=('ROOF' if abs(s['z']-16)<.01 else 'FLOOR') if s['id'].startswith('L') else profiles.get(s['id'])
        additional={'FLOOR':260,'ROOF':200,'ELEVATOR_ROOF':1500}.get(profile)
        if additional is None: raise ValueError('Perfil no identificado: '+s['id'])
        descriptions=[]
        for name,a,b,area in edges(s):
            # Edge midpoint belongs to its own region (avoids partition-boundary ties).
            x,y=(a[0]+b[0])/2,(a[1]+b[1])/2
            try:
                _,receivers,_=transfer(data,s['id'],x,y,1)
                beams=sorted({r['beam'] for r in receivers})
                message=''
            except ValueError as exc: beams=[];message=str(exc)
            descriptions.append(dict(side=name,area=area,beams=beams,message=message))
        rows.append(dict(id=s['id'],profile=profile,thickness=.15,unitWeight=2500*.00980665,
                         finishes=additional*.00980665,qG=(375+additional)*.00980665,edges=descriptions))
    output=ROOT/'P1L4/unity_visualizador/Assets/Resources/slab_load_surfaces.json'
    output.write_text(json.dumps(dict(slabs=rows),indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    return rows


if __name__=='__main__':
    data=json.loads((ROOT/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8-sig'))
    rows=export(data)
    print('Slabs:',len(rows),'Unmapped edge midpoints:',sum(not e['beams'] for s in rows for e in s['edges']))
