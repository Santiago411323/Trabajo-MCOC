"""Independent geometry, connectivity, static equilibrium and isolation audit."""
import hashlib
import json
import math
import sys
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
folder=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'P1L4/desktop_model'
backup=ROOT/'revisiones/punto_inicio_muros_positivos_20261006'
with zipfile.ZipFile(backup/'estado_previo.zip') as z:
    old=json.loads(z.read('P1L4/desktop_model/estructura_p1l4_desktop.json'))
new=json.loads((folder/'estructura_p1l4_desktop.json').read_text(encoding='utf8'))
nodes={n['id']:n for n in new['nodes']};before={n['id']:n for n in old['nodes']}
coords=lambda n:tuple(n[k] for k in ('x','y','z'))
close=lambda a,b:all(abs(x-y)<1e-7 for x,y in zip(a,b))
for previous,current in zip(old['walls'],new['walls']):
    assert previous['id']==current['id']
    assert previous['longitud']==current['longitud'] and previous['grosor']==current['grosor']
    delta=11.035 if 31<=current['id']<=35 else 8.09 if 36<=current['id']<=40 else 0
    for key in ('nodeI','nodeJ'):
        p=before[previous[key]];assert close(coords(nodes[current[key]]),(p['x'],p['y']+delta,p['z'])),current['id']
    assert previous['panelZ']==current['panelZ']
assert old['slabs']==new['slabs'],'Unrequested slab/void change'
members={e['elementTag']:e for e in new['elements']}
assert len(members)==len(new['elements'])
for previous in old['elements']:
    if previous['type'] not in ('viga','columna'):continue
    current=members[previous['elementTag']]
    assert previous==current,'Frame member changed: '+previous['elementTag']
    for key in ('nodeI','nodeJ'):assert coords(before[previous[key]])==coords(nodes[current[key]])
families=('W_DPRIME_ELEVATOR_TOP_TO_CPRIME','W_CPRIME_ELEVATOR_SIDE')
for previous in old['elements']:
    if previous['type']!='muro_eq' or previous.get('sourceId') not in families:continue
    current=members[previous['elementTag']]
    assert current['sectionId']==previous['sectionId']
    for key in ('nodeI','nodeJ'):
        delta=11.035 if previous['sourceId']==families[0] else 8.09
        p=before[previous[key]];assert close(coords(nodes[current[key]]),(p['x'],p['y']+delta,p['z']))
for x,names in [(-12.645,families),(-10,(families[0],'W_DPRIME_OPENING_TO_3'))]:
    for level in (0,4,8,12,16):
        candidates=[nid for nid,n in nodes.items() if close(coords(n),(x,6.99,level))]
        assert any(all(any(e['type']=='brazo_rigido' and e.get('sourceId')==name and e['nodeJ']==nid for e in new['elements']) for name in names) for nid in candidates),('Disconnected corner',x,level)
expected=[]
for support in old['supports']:
    p=before[support['node']];delta=11.035 if families[0] in support['type'] else 8.09 if families[1] in support['type'] else 0
    expected.append((p['x'],round(p['y']+delta,6),p['z']))
assert sorted(expected)==sorted((n['x'],round(n['y'],6),n['z']) for n in (nodes[s['node']] for s in new['supports'])),'Supports changed outside selected walls'
extras=new['p1l4'];assert extras['combinations']==old['p1l4']['combinations']
forces={(r['combo'],r['id']):r['f'] for r in extras['elementForces']}
assert len(forces)==7*len(new['elements']) and all(math.isfinite(v) for f in forces.values() for v in f)
error=0
for combo in extras['combinations']:
    for e in new['elements']:
        expected=[sum(combo[k]*forces[(k,e['id'])][i] for k in ('G','Q','EX','EY')) for i in range(12)]
        error=max(error,max(abs(a-b) for a,b in zip(expected,forces[(combo['name'],e['id'])])))
assert error<1e-5,error
balance={k:max(abs(v) for v in row['desbalance_kN']) for k,row in extras['analysisModel']['equilibrio'].items()}
assert len(balance)==7 and max(balance.values())<1e-5,balance
for path,digest in json.loads((backup/'protected.json').read_text()).items():
    with (ROOT/path).open('rb') as stream:assert hashlib.file_digest(stream,'sha256').hexdigest()==digest,path
report=dict(status='PASS',translated_wall_ids=list(range(31,41)),wall31_to35_unity_z=6.99,wall36_to40_unity_z=[4.045,6.99],opening_faces_corridor=True,common_corners_connected=True,
    frames_and_slabs_unchanged=True,solver_and_phone_resources_unchanged=True,superposition_max_error=error,equilibrium=balance,
    model_sha256=hashlib.sha256((folder/'estructura_p1l4_desktop.json').read_bytes()).hexdigest())
(ROOT/'revisiones/validacion_muros_positivos.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf8')
print(json.dumps(report,indent=2),flush=True)
