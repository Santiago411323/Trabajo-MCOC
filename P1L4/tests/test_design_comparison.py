"""Generate an isolated before/after fixture from real OpenSees responses.

No edits to the live model or model_edits.json. Test changes width and bar diameter.
"""
from pathlib import Path
import copy
import hashlib
import json
import math
import sys

BASE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(BASE))
from seismic.transient_analysis import load_engine
import exportar_resultados_unity as exporter

engine=load_engine()
source=BASE/'desktop_model/estructura_p1l4_desktop.json'
original_hash=hashlib.sha256(source.read_bytes()).hexdigest()
before=json.loads(source.read_text(encoding='utf-8'))
after=copy.deepcopy(before)
element=next(e for e in after['elements'] if e.get('elementTag')=='E1_72')
element['width_m']=.7
element['sectionId']=element['seccion']='EDIT_E1_72'
edit=dict(elementTag='E1_72',elementId=element['id'],elementType='viga',isWall=False,
          width_m=.7,height_m=element['height_m'],fc_MPa=25,fy_MPa=420,
          topBars=4,bottomBars=4,sideBarsEach=2,barDiameter_mm=28,cover_mm=50)
material=dict(sectionId='EDIT_E1_72',elementType='viga',b_m=.7,h_m=element['height_m'],
              fc_MPa=25,fy_MPa=420,steelBars=12,topBars=4,bottomBars=4,sideBarsEach=2,
              barDiameter_mm=28,cover_mm=50,Ast_mm2=12*math.pi*28**2/4,
              Es_MPa=200000,E_MPa=25000)
after['p1l4']['sectionMaterials'].append(material)
after['p1l4']['pmCurves'].append(exporter.custom_capacity_curve(edit,engine))
live=engine.transfer_live_load(after,after['Q_kN_m2'])
seismic=engine.build_seismic_cases(after,live,after['seismic_coefficient'])
sets={'G':engine.dead_nodal_loads(after),'Q':engine.live_load_set(live),
      'EX':engine.vector_loads_from_dict(seismic['cargas_nodales_EX']),
      'EY':engine.vector_loads_from_dict(seismic['cargas_nodales_EY'])}

def analyze(loads):
    engine.build_model(after);engine.apply_nodal_loads(loads)
    engine.ops.system('BandGeneral');engine.ops.numberer('RCM');engine.ops.constraints(engine.CONSTRAINT_HANDLER)
    engine.ops.integrator('LoadControl',1.0);engine.ops.algorithm('Linear');engine.ops.analysis('Static')
    assert engine.ops.analyze(1)==0
    forces={int(tag):list(engine.ops.eleResponse(tag,'localForce')) for tag in engine.ops.getEleTags()}
    disps={int(tag):list(engine.ops.nodeDisp(tag)) for tag in engine.ops.getNodeTags()}
    assert all(len(f)==12 and all(math.isfinite(x) for x in f) for f in forces.values())
    assert all(all(math.isfinite(x) for x in f) for f in disps.values())
    return forces,disps

after['p1l4']['elementForces']=[];after['p1l4']['displacements']=[]
base={}
for name,loads in sets.items():
    fs,ds=analyze(loads);base[name]=(fs,ds)
    after['p1l4']['elementForces'] += [dict(combo=name,id=tag,f=f) for tag,f in fs.items()]
    after['p1l4']['displacements'] += [dict(combo=name,node=tag,**dict(zip(('ux','uy','uz','rx','ry','rz'),d))) for tag,d in ds.items()]
lambdas=dict(G=1,Q=.5,EX=.3,EY=.2)
explicit,_=analyze(engine.combine_nodal_loads(sets,lambdas))
maximum=0
for tag,f in explicit.items():
    predicted=[sum(lambdas[n]*base[n][0][tag][j] for n in lambdas) for j in range(12)]
    maximum=max(maximum,max(abs(x-y) for x,y in zip(f,predicted)))
assert maximum<1e-7
output=BASE/'design_comparison_validation';output.mkdir(exist_ok=True)
write=lambda name,payload:(output/name).write_text(json.dumps(payload,ensure_ascii=False),encoding='utf-8')
write('before.json',before);write('after.json',after)
write('expected.json',dict(elementId=element['id'],elementTag='E1_72',forcesC1=explicit[element['id']],
                          widthBefore=.6,widthAfter=.7,diameterBefore=25,diameterAfter=28,
                          sourceHash=original_hash,maximumSuperpositionError=maximum))
assert original_hash==hashlib.sha256(source.read_bytes()).hexdigest()
assert before['nodes']==after['nodes'] and before['supports']==after['supports']
print('PASS: G/Q/EX/EY after change computed by OpenSees; C1 explicit vs superposition error',maximum)
print('PASS: before/after fixtures isolated; width .60 -> .70 m, steel 25 -> 28 mm; live JSON unchanged.')
