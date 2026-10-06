"""Entrada geométrica de escritorio: eje 2 de E2 a Y local 7.25, sin invertirlo.

Reutiliza íntegramente el generador original y su reparto tributario.
No modifica los archivos de cálculo ni los modelos usados por los teléfonos.
"""
import copy
import json
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def prepare(raw):
    sys.path.insert(0,str(ROOT/'edificio_2/modelo_python'))
    import geometry_data as geometry
    old=geometry.create_geometry()
    refresh=geometry.refresh_grids
    def aligned_grids():
        refresh()
        geometry.grid_y['2']=7.25
    geometry.refresh_grids=aligned_grids
    try:
        new=geometry.create_geometry()
    finally:
        geometry.refresh_grids=refresh
    old_nodes={n['id']:n for n in old['nodes']}
    new_nodes={n['id']:n for n in new['nodes']}
    assert old_nodes.keys()==new_nodes.keys(), 'Original node identities changed'
    data=copy.deepcopy(raw)
    standalone=json.loads((ROOT/'edificio_2/unity_visualizador/Assets/Resources/estructura_edificio_ingenieria_unity.json').read_text(encoding='utf8'))
    standalone_members={str(e['elementTag']):e for e in standalone['elements']}
    mapping={}
    for e in data['elements']:
        if e.get('sourceBuilding')!='edificio_2':continue
        original=standalone_members[e['elementTag']]
        for key in ('nodeI','nodeJ'):
            source=original[key]
            assert source not in mapping or mapping[source]==e[key]
            mapping[source]=e[key]
    by_id={n['id']:n for n in data['nodes']}
    moved=[]
    for source,target in mapping.items():
        before,after=old_nodes[source],new_nodes[source]
        assert before['x']==after['x'] and before['z']==after['z']
        if abs(before['y']-after['y'])<1e-9:continue
        assert abs(before['y']-8.9)<1e-9 and abs(after['y']-7.25)<1e-9
        node=by_id[target]
        assert abs(node['y']-1.65)<1e-9
        node['y']=0.0
        moved.append(target)
    loads={r['beam_id']:r for r in new['beam_tributary_loads']}
    for e in data['elements']:
        if e['type']!='viga' or e.get('sourceBuilding')!='edificio_2':continue
        row=loads.get(e['elementTag'])
        area=row['tributary_area_m2'] if row else 0.
        e['areaTributaria']=area
        e['deadLoad']=row['loads_kN']['D'] if row else 0.
        e['liveLoad']=row['loads_kN']['L'] if row else 0.
        e['factoredLoad14D']=row['load_combinations_kN']['U_1_4D'] if row else 0.
        e['factoredLoad12D16L']=row['load_combinations_kN']['U_1_2D_1_6L'] if row else 0.
        e['cargaTributaria']=e['factoredLoad12D16L']
        for legacy in ('axialI','axialJ','shearI','shearJ','momentI','momentJ'):
            e.pop(legacy,None)
    # E1 slab inputs stay intact. E2 panels and architectural voids come from
    # the original generator, with the new axis and unchanged void specifications.
    data['slabs']=[s for s in data['slabs'] if min(s['x0'],s['x1'])>=-10-1e-8]
    for slab in new['rigid_diaphragms']:
        data['slabs'].append(dict(id=str(slab['id']),nivel=slab['level'],
            x0=round(slab['x1']-41.475,6),x1=round(slab['x2']-41.475,6),
            y0=round(slab['y1']-7.25,6),y1=round(slab['y2']-7.25,6),z=round(slab['z']+4.17,6)))
    checks=new['tributary_checks']
    assert all(abs(r['area_error_m2'])<1e-7 and abs(r['D_error_kN'])<1e-7 and abs(r['L_error_kN'])<1e-7 for r in checks.values())
    audit=dict(axis_before=1.65,axis_after=0.,moved_nodes=moved,
        unchanged_outer_axes=[-7.25,8.9],original_generator='edificio_2/modelo_python/geometry_data.py',
        original_parameter='grid_y[2]: 8.90 -> 7.25',tributary_checks=checks,
        column_stacks=['C1004..C5004','C1005..C5005','C1006..C5006'])
    return data,audit
