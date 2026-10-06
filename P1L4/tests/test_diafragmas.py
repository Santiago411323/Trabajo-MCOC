"""Membresía real, compatibilidad cinemática y conservación de resultados."""
import copy
import json
import math
from pathlib import Path
import subprocess
import sys

BASE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(BASE))
from exportar_diafragmas import groups_from_model
from seismic.transient_analysis import load_engine

engine=load_engine()
for path in [BASE/'desktop_model/estructura_p1l4_desktop.json',
             BASE/'unity_visualizador/Assets/Resources/estructura_p1l4_unity.json']:
    data=json.loads(path.read_text(encoding='utf-8'))
    actual=data['p1l4']['analysisModel']['diafragmas']
    # Capture arguments at the actual OpenSees call; do not infer membership from slabs.
    calls=[]; original=engine.ops.rigidDiaphragm
    def capture(axis,master,*slaves):
        calls.append((axis,master,sorted(slaves)))
        return original(axis,master,*slaves)
    engine.ops.rigidDiaphragm=capture
    try:
        assert groups_from_model(data,engine)==actual
    finally:
        engine.ops.rigidDiaphragm=original
    assert calls==[(g['normalAxis'],g['master'],g['slaveTags']) for g in actual]
    nodes={n['id']:n for n in data['nodes']}
    by_case={}
    for d in data['p1l4']['displacements']:by_case.setdefault(d['combo'],{})[d['node']]=d
    max_residual=0
    for g in actual:
        assert g['slaves']==len(set(g['slaveTags'])) and g['master'] not in g['slaveTags']
        assert g['constrainedDofs']==[1,2,6] and g['masterFixity']==[0,0,1,1,1,0]
        coords=engine.ops.nodeCoord(g['master'])
        assert all(math.isclose(a,b,abs_tol=1e-10) for a,b in zip(coords,[g['x'],g['y'],g['z']]))
        assert all(abs(nodes[n]['z']-g['z'])<1e-10 for n in g['slaveTags'])
        for case in ['G','Q','EX','EY','C1','C2','C3']:
            records=by_case[case]; count=g['slaves']
            rz=sum(records[n]['rz'] for n in g['slaveTags'])/count
            ux=sum(records[n]['ux']+records[n]['rz']*(nodes[n]['y']-g['y']) for n in g['slaveTags'])/count
            uy=sum(records[n]['uy']-records[n]['rz']*(nodes[n]['x']-g['x']) for n in g['slaveTags'])/count
            for n in g['slaveTags']:
                d=records[n];p=nodes[n]
                error=max(abs(d['ux']-(ux-rz*(p['y']-g['y']))),abs(d['uy']-(uy+rz*(p['x']-g['x']))))
                max_residual=max(error,max_residual)
                assert error<1e-7 and abs(d['rz']-rz)<1e-9
    # The migration must only enrich metadata, leaving every other field identical.
    rel=path.relative_to(BASE.parent).as_posix()
    before=json.loads(subprocess.check_output(['git','show','HEAD:'+rel],cwd=BASE.parent))
    snapshot=copy.deepcopy(data)
    snapshot['p1l4']['analysisModel']['diafragmas']=before['p1l4']['analysisModel']['diafragmas']
    assert snapshot==before, 'Se modificaron datos ajenos a los metadatos de diafragmas.'
    print(f'PASS {path.name}: {len(actual)} grupos, {sum(g["slaves"] for g in actual)} vínculos exactos; 7 casos; residuo máximo {max_residual:.3e} m; resto del JSON idéntico.')

# Contrast the recovered master motion with nodeDisp of actual auxiliary masters.
data=json.loads((BASE/'desktop_model/estructura_p1l4_desktop.json').read_text(encoding='utf-8'))
live=engine.transfer_live_load(data,data['Q_kN_m2'])
seismic=engine.build_seismic_cases(data,live,data.get('seismic_coefficient',.2))
for name in ('EX','EY'):
    loads=engine.vector_loads_from_dict(seismic['cargas_nodales_'+name])
    engine.build_model(data)
    engine.apply_nodal_loads(loads)
    engine.ops.system('BandGeneral'); engine.ops.numberer('RCM')
    engine.ops.constraints(engine.CONSTRAINT_HANDLER)
    engine.ops.integrator('LoadControl',1.0);engine.ops.algorithm('Linear');engine.ops.analysis('Static')
    assert engine.ops.analyze(1)==0
    for g in engine.DIAPHRAGMS:
        r={n:engine.ops.nodeDisp(n) for n in g['slaveTags']};count=g['slaves']
        ux=sum(d[0]+d[5]*(engine.ops.nodeCoord(n)[1]-g['y']) for n,d in r.items())/count
        uy=sum(d[1]-d[5]*(engine.ops.nodeCoord(n)[0]-g['x']) for n,d in r.items())/count
        rz=sum(d[5] for d in r.values())/count
        master=engine.ops.nodeDisp(g['master'])
        assert max(abs(ux-master[0]),abs(uy-master[1]),abs(rz-master[5]))<1e-10
    print('PASS maestro recuperado = nodeDisp OpenSees directo:',name)
