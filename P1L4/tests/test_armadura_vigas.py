"""Contrato de armadura, conservación de resultados y edición por elemento."""
import json
import math
import sys
import tempfile
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import exportar_resultados_unity as exporter

data=exporter.load_json(exporter.JSON_OUT)
materials={m['sectionId']:m for m in data['p1l4']['sectionMaterials']}
curves={c['sectionId']:c for c in data['p1l4']['pmCurves']}
for cid in ('V60/80','V40/80','V30/80','V30/45'):
    m=materials[cid];curve=curves[cid]
    assert m['steelBars']==4 and m['topBars']==m['bottomBars']==2
    assert m['sideBarsEach']==0 and m['barDiameter_mm']==10
    assert math.isclose(m['Ast_mm2'],4*math.pi*10**2/4)
    assert m['stirrupCount']==17 and m['stirrupSpacing_mm']==100 and m['stirrupLegs']==4
    assert math.isclose((m['stirrupCount']-1)*m['stirrupSpacing_mm']/1000,1.6)
    assert math.isclose(curve['points'][-1]['P_kN'],-m['Ast_mm2']*m['fy_MPa']/1000)
    expected=.85*m['fc_MPa']*1000*(m['b_m']*m['h_m']-m['Ast_mm2']/1e6)+m['fy_MPa']*m['Ast_mm2']/1000
    assert math.isclose(curve['Po_kN'],expected)
    assert len(curve['momentCurvature'])==400
    assert all(math.isfinite(p['M_kN_m']) for p in curve['momentCurvature'])
    print(cid,'PASS As=',round(m['Ast_mm2'],3),'mm2; M máximo=',round(max(p['M_kN_m'] for p in curve['momentCurvature']),3),'kN·m')

beam=next(e for e in data['elements'] if e.get('type')=='viga')
m=materials[beam['sectionId']]
edit=dict(m,elementId=beam['id'],elementTag=beam['elementTag'],elementType='viga',
          width_m=m['b_m'],height_m=m['h_m'],topBars=3,bottomBars=4,sideBarsEach=0,
          barDiameter_mm=16,stirrupCount=25,stirrupDiameter_mm=12,stirrupSpacing_mm=150,stirrupLegs=6)
with tempfile.TemporaryDirectory() as tmp:
    path=Path(tmp)/'model_edits.json';path.write_text(json.dumps({'elements':[edit]}))
    before={e['id']:dict(e) for e in data['elements']}
    custom,applied=exporter.apply_model_edits(data,path)
    assert len(applied)==1 and custom[0]['steelBars']==7
    assert custom[0]['stirrupCount']==25 and custom[0]['stirrupLegs']==6
    assert custom[0]['stirrupSpacing_mm']==150 and custom[0]['stirrupDiameter_mm']==12
    assert math.isclose(custom[0]['Ast_mm2'],7*math.pi*16**2/4)
    for e in data['elements']:
        if e['id']!=beam['id']: assert e==before[e['id']]
    assert before[beam['id']]['nodeI']==beam['nodeI'] and before[beam['id']]['nodeJ']==beam['nodeJ']
print('PASS edición individual: barras/estribos transmitidos; resto de elementos intacto.')
