"""Auditoría independiente de geometría, resultados e aislamiento de escritorio."""
import hashlib
import json
import math
import sys
import zipfile
from collections import defaultdict
from pathlib import Path

root=Path(__file__).resolve().parents[2]
folder=Path(sys.argv[1]) if len(sys.argv)>1 else root/'P1L4/desktop_model'
backup=root/'revisiones/punto_inicio_alineacion_e2_20261006'
with zipfile.ZipFile(backup/'estado_previo.zip') as archive:
    old=json.loads(archive.read('P1L4/desktop_model/estructura_p1l4_desktop.json'))
model=json.loads((folder/'estructura_p1l4_desktop.json').read_text(encoding='utf8'))
nodes={n['id']:n for n in model['nodes']}
before={n['id']:n for n in old['nodes']}
members=model['elements']
by_tag={e['elementTag']:e for e in members}
assert len(by_tag)==len(members)
columns=[]
for level in range(1,6):
    for index in (4,5,6):
        tag=f'C{level*1000+index}'
        e=next(e for e in members if e.get('sourceId')==tag and e['type']=='columna')
        assert all(abs(nodes[e[k]]['y'])<1e-8 for k in ('nodeI','nodeJ'))
        columns.append(e['elementTag'])
for n in model['nodes']:
    o=before.get(n['id'])
    if o and n.get('origen')!='desktop_wall_opening' and any(abs(o[k]-n[k])>1e-8 for k in ('x','y','z')):
        assert o['x']==n['x'] and o['z']==n['z']
        assert abs(o['y']-1.65)<1e-8 and abs(n['y'])<1e-8
        assert n['x']<=-10
for wall_index,(a,b) in enumerate(zip(model['walls'],old['walls']),1):
    for key in ('nodeI','nodeJ'):
        expected=dict(before[b[key]])
        if wall_index in range(41,46) and key=='nodeJ':expected['y']=-.35
        assert tuple(nodes[a[key]][k] for k in ('x','y','z'))==tuple(expected[k] for k in ('x','y','z'))
    previous=dict(b)
    if wall_index in range(41,46):previous['longitud']=3.695
    assert {k:v for k,v in a.items() if k not in ('nodeI','nodeJ','analysisElements','demands')}=={k:v for k,v in previous.items() if k not in ('nodeI','nodeJ','analysisElements','demands')}
support_coordinates=lambda m,ns:sorted(tuple(ns[s['node']][k] for k in ('x','y','z')) for s in m['supports'])
old_supports=support_coordinates(old,before)
expected_supports=sorted((x,0. if x<-10 and abs(y-1.65)<1e-8 else -2.1975 if abs(x+10)<1e-8 and abs(y+1.8225)<1e-8 else y,z) for x,y,z in old_supports)
assert support_coordinates(model,nodes)==expected_supports
for tag,e in {e['elementTag']:e for e in old['elements']}.items():
    if e.get('sourceBuilding')=='edificio_1' and e['type'] in ('viga','columna') and tag in by_tag:
        n=by_tag[tag]
        assert (e['nodeI'],e['nodeJ'])==(n['nodeI'],n['nodeJ'])
        assert all(nodes[n[k]]==before[e[k]] for k in ('nodeI','nodeJ'))
for source in (47,95,139,183,225):
    assert f'E1_{source}' in by_tag
    assert f'E1_{source}.1' not in by_tag
    assert f'E1_{source}.2' not in by_tag
coordinates=lambda nid:tuple(round(nodes[nid][k],6) for k in ('x','y','z'))
segments=set()
incident=defaultdict(list)
for e in members:
    if e['type'] not in ('viga','columna'):continue
    a,b=coordinates(e['nodeI']),coordinates(e['nodeJ'])
    assert math.dist(a,b)>1e-5
    segment=tuple(sorted((a,b)))
    assert segment not in segments, ('Duplicate physical member',e['elementTag'])
    segments.add(segment)
    for nid in (e['nodeI'],e['nodeJ']):incident[coordinates(nid)].append(e)
# Every division on the aligned longitudinal beams meets a transverse beam or column.
for point,attached in incident.items():
    if point[0]>=-10 or abs(point[1])>1e-8:continue
    longitudinal=[e for e in attached if e['type']=='viga' and
                  abs(nodes[e['nodeI']]['y']-nodes[e['nodeJ']]['y'])<1e-8]
    if len(longitudinal)>1:
        assert any(e['type']=='columna' or abs(nodes[e['nodeI']]['y']-nodes[e['nodeJ']]['y'])>1e-8 for e in attached),point
changed_slabs=0
old_slabs={s['id']:s for s in old['slabs']}
for slab in model['slabs']:
    assert slab['x1']>slab['x0'] and slab['y1']>slab['y0']
    if slab!=old_slabs.get(slab['id']):changed_slabs+=1
    if slab['x0']<-10:
        assert abs(slab['y0']-1.65)>1e-8 and abs(slab['y1']-1.65)>1e-8
catalog=json.loads((folder/'slab_load_surfaces.json').read_text())['slabs']
assert {s['id'] for s in catalog}=={s['id'] for s in model['slabs']}
extra=model['p1l4'];original=old['p1l4']
assert extra['combinations']==original['combinations']
original_sections={s['sectionId']:s for s in original['sectionMaterials']}
for section in extra['sectionMaterials']:
    if section['sectionId']=='W_0.25x3.69':
        previous=original_sections['W_0.25x4.45']
        for key in ('materialName','fc_MPa','fy_MPa','E_MPa','b_m','barDiameter_mm','note'):
            assert section[key]==previous[key]
        assert abs(section['h_m']-3.695)<1e-8
    else:assert section==original_sections[section['sectionId']]
forces={(f['combo'],f['id']):f['f'] for f in extra['elementForces']}
assert all(math.isfinite(v) for row in forces.values() for v in row)
assert len(forces)==7*len(members)
error=0.
for combo in extra['combinations']:
    for e in members:
        expected=[sum(combo[c]*forces[(c,e['id'])][i] for c in ('G','Q','EX','EY')) for i in range(12)]
        error=max(error,max(abs(a-b) for a,b in zip(expected,forces[(combo['name'],e['id'])])))
assert error<1e-5,error
balance={k:max(abs(v) for v in row['desbalance_kN']) for k,row in extra['analysisModel']['equilibrio'].items()}
assert len(balance)==7 and max(balance.values())<1e-5
manifest=json.loads((backup/'manifest.json').read_text())
protected=[]
for path,digest in manifest.items():
    if (path.startswith('P1L3/') and path.endswith('.py')) or path in (
        'P1L4/exportar_resultados_unity.py','P1L4/slab_panels.py',
        'edificio_2/modelo_python/geometry_data.py','P1L4/seismic/transient_analysis.py',
        'P1L4/model_edits.json') or path.startswith('P1L4/unity_visualizador_ios/') or (
        path.startswith('P1L4/unity_visualizador/Assets/Resources/')):
        assert hashlib.sha256((root/path).read_bytes()).hexdigest()==digest,path
        protected.append(path)
report=dict(status='PASS',central_column_segments=len(columns),slabs_updated=changed_slabs,
    elements_before=len(old['elements']),elements_after=len(members),
    superposition_max_error_kN_or_kNm=error,equilibrium_max_error_kN=balance,
    unchanged_protected_files=len(protected),phone_resources_and_ios_source='unchanged')
(root/'revisiones/validacion_alineacion_e2.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf8')
print(json.dumps(report,indent=2))
