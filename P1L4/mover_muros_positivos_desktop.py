"""Geometry-only wrapper: wall31..35 at Unity Z6.99, wall36..40 at Z4.045..6.99.

Uses the existing connectivity, wall, loading and OpenSees programs unchanged.
Retains the desktop E2 alignment and earlier wall-opening correction.
"""
import copy
from pathlib import Path

BASE=Path(__file__).resolve().parent
FAMILIES=('W_DPRIME_ELEVATOR_TOP_TO_CPRIME','W_CPRIME_ELEVATOR_SIDE')
DELTAS={FAMILIES[0]:11.035,FAMILIES[1]:8.09}

def translate(data):
    data=copy.deepcopy(data)
    selected=[w for w in data['walls'] if any((w.get('sourceId') or '').startswith(name+'_L') for name in FAMILIES)]
    assert len(selected)==10,'Expected five panels per wall family'
    nodes={n['id']:n for n in data['nodes']}
    affected={w[k] for w in selected for k in ('nodeI','nodeJ')}
    protected={e[k] for e in data['elements'] for k in ('nodeI','nodeJ')}
    protected|={w[k] for w in data['walls'] if w not in selected for k in ('nodeI','nodeJ')}
    assert not affected&protected,'Wall drawing nodes shared with an unrelated element; refuse blanket translation'
    audit=[]
    for w in selected:
        a,b=nodes[w['nodeI']],nodes[w['nodeJ']]
        assert abs(a['y']+4.045)<1e-6
        audit.append(dict(sourceId=w['sourceId'],before=[[n[k] for k in ('x','y','z')] for n in (a,b)]))
    changes={}
    for w in selected:
        name=next(name for name in FAMILIES if w['sourceId'].startswith(name+'_L'))
        for key in ('nodeI','nodeJ'):
            nid=w[key];assert nid not in changes or changes[nid]==DELTAS[name];changes[nid]=DELTAS[name]
    for nid,delta in changes.items():nodes[nid]['y']=round(nodes[nid]['y']+delta,6)
    for row,w in zip(audit,selected):row['after']=[[nodes[w[key]][k] for k in ('x','y','z')] for key in ('nodeI','nodeJ')]
    return data,dict(delta_unity_z_m_by_family=DELTAS,wallIds=list(range(31,41)),panels=audit,
        unchanged_slabs=True,unchanged_frames=True,connection='Shared far corner of31..35/36..40; join to51..55 at X=-10,Unity Z=6.99; opening faces corridor',
        note='Only two wall families moved; original elevator void and adjacent walls remain. No calculation code or diagram conventions changed.')

def main():
    import alinear_eje_edificio2 as alignment
    import actualizar_hueco_muros_desktop as desktop
    original=alignment.prepare
    def positive_prepare(raw):
        aligned,audit=original(raw)
        moved,positive=translate(aligned)
        audit['positiveWallRelocation']=positive
        return moved,audit
    alignment.prepare=positive_prepare
    wall_builder=desktop.cvm.agregar_muros
    def stable_wall_geometry(data,*args,**kwargs):
        model,report=wall_builder(data,*args,**kwargs)
        # Use the unchanged input length, avoiding a section-name change from subtraction roundoff.
        lengths={FAMILIES[1]:next(w['longitud'] for w in model['walls'] if (w.get('sourceId') or '').startswith(FAMILIES[1]+'_L'))}
        for e in model['elements']:
            if e['type']!='muro_eq' or e.get('sourceId') not in lengths:continue
            length=lengths[e['sourceId']];thickness=e['wallThickness_m']
            e['wallLength_m']=length
            if e['wallStrongAxis']=='My':e.update(width_m=thickness,height_m=length)
            else:e.update(width_m=length,height_m=thickness)
            e['sectionId']=f'MURO_{thickness:.2f}x{length:.2f}'
        return model,report
    desktop.cvm.agregar_muros=stable_wall_geometry
    desktop.main()

if __name__=='__main__':main()
