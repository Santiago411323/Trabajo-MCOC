"""Equilibrio y superposición contrastados con un análisis OpenSees directo."""
import hashlib
import json
import math
from pathlib import Path
import sys

base=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(base))
import lrfd_analysis as lab

model=base/'desktop_model/estructura_p1l4_desktop.json'
dataset=base/'lrfd_results/validation_results.json'
data=json.loads(model.read_text(encoding='utf-8'));result=json.loads(dataset.read_text(encoding='utf-8'))
original=hashlib.sha256(model.read_bytes()).hexdigest()
assert result['modelHash']==original
assert {v['u'] for v in result['variants']}==set(range(1,8))
assert len(result['variants'])==22
assert result['equilibriumError']<.02
assert all(math.isfinite(x) for variant in result['variants'] for row in variant['forces'] for x in row['f'])
cvm=lab.load_engine()
g=lab.source_case(data,'G',cvm)
base_forces={row['id']:row['f'] for row in g['forces']}
u1=next(v for v in result['variants'] if v['u']==1)
for row in u1['forces']:
    assert max(abs(a-1.4*b) for a,b in zip(row['f'],base_forces[row['id']]))<1e-7
catalog=json.loads((model.parent/'slab_load_surfaces.json').read_text(encoding='utf-8'))
roof,area=lab.roof_load(data,catalog,cvm)
assert math.isclose(area,result['roofArea'])
wx,_=lab.wind_load(data,0,1,cvm);wy,_=lab.wind_load(data,1,-1,cvm)
q=cvm.live_load_set(cvm.transfer_live_load(data,data['Q_kN_m2']))
sets={'G':cvm.dead_nodal_loads(data),'Q':q,'Roof':roof,'WXp':wx,'WYm':wy}
recipe=next((r for r in lab.recipes(result['scenario']) if r[0]==4 and 'A=Lr' in r[1]))
direct=lab.run_case(data,cvm.combine_nodal_loads(sets,recipe[2]),cvm)
combined=next(v for v in result['variants'] if v['label']==recipe[1])
comparison={row['id']:row['f'] for row in direct['forces']}
max_error=0
for row in combined['forces']:
    expected=comparison[row['id']]
    error=max(abs(a-b) for a,b in zip(row['f'],expected));max_error=max(max_error,error)
    assert error<max(.03,max(map(abs,expected))*1e-5),(row['id'],error)
assert hashlib.sha256(model.read_bytes()).hexdigest()==original
cvm.ops.wipe()
print('PASS: 22 variantes U1–U7, equilibrio, U1 exacta y U4 contrastada con OpenSees directo; error máximo',max_error,'kN/kN·m. JSON original intacto.')
