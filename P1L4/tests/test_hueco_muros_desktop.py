"""Comprobación independiente del modelo recalculado y las conexiones conservadas."""
import json
from pathlib import Path

root=Path(__file__).resolve().parents[2]
old=json.loads((root/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8-sig'))
new=json.loads((root/'P1L4/desktop_model/estructura_p1l4_desktop.json').read_text(encoding='utf-8'))
nodes={n['id']:n for n in new['nodes']}
original={n['id']:n for n in old['nodes']}
for left_id,right_id in zip(range(41,46),range(51,56)):
    left,right=new['walls'][left_id-1],new['walls'][right_id-1]
    assert abs(nodes[left['nodeI']]['y']+4.045)<1e-8
    assert abs(nodes[right['nodeJ']]['y']-8.9)<1e-8
    assert abs(nodes[left['nodeJ']]['y']+.35)<1e-8
    assert abs(nodes[right['nodeI']]['y']-2.8)<1e-8
    assert abs(left['longitud']-3.695)<1e-8
    assert abs(right['longitud']-6.1)<1e-8
    assert abs(nodes[right['nodeI']]['y']-.35-2.45)<1e-8
for member in old['elements']:
    if member['type'] not in ('viga','columna'):continue
    # E2 is now intentionally aligned; its geometry has a separate regression audit.
    if member.get('sourceBuilding')=='edificio_2' or member.get('sourceId') in (47,95,139,183,225):continue
    current=next(e for e in new['elements'] if e['id']==member['id'])
    assert (current['nodeI'],current['nodeJ'],current['elementTag'])==(member['nodeI'],member['nodeJ'],member['elementTag'])
    assert nodes[current['nodeI']]==original[member['nodeI']]
    assert nodes[current['nodeJ']]==original[member['nodeJ']]
assert sorted(nodes[s['node']]['z'] for s in new['supports'])==sorted(original[s['node']]['z'] for s in old['supports'])
extras=new['p1l4'];forces={(f['combo'],f['id']):f['f'] for f in extras['elementForces']}
error=0
for combo in extras['combinations']:
    for member in new['elements']:
        expected=[sum(combo[case]*forces[(case,member['id'])][i] for case in ('G','Q','EX','EY')) for i in range(12)]
        actual=forces[(combo['name'],member['id'])]
        error=max(error,max(abs(a-b) for a,b in zip(expected,actual)))
assert error<1e-5, error
equilibrium=extras['analysisModel']['equilibrio']
assert all(max(abs(v) for v in e['desbalance_kN'])<1e-5 for e in equilibrium.values())
assert (root/'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json').read_bytes()==(root/'P1L4/unity_visualizador_ios/Assets/Resources/estructura_p1l4_unity.json').read_bytes()
print(f'PASS: five left walls terminate at column face Z=-0.35; right walls and outer endpoints/columns/beams/support elevations preserved; G/Q/EX/EY/C1-C3 equilibrium; superposition error {error:.3e}; phone model resources unchanged.')
