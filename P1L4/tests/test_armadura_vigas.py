"""Contrato de armadura, conservación de resultados y edición por elemento."""
import json
import math
import subprocess
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
    assert m['steelBars']==12 and m['topBars']==m['bottomBars']==4
    assert m['sideBarsEach']==2 and m['barDiameter_mm']==25
    assert math.isclose(m['Ast_mm2'],12*math.pi*25**2/4)
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

# Ambos destinos deben conservar exactamente el análisis y geometría publicados.
root = Path(__file__).resolve().parents[2]
for relative in ('P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json',
                 'P1L4/desktop_model/estructura_p1l4_desktop.json'):
    current = exporter.load_json(root/relative)
    previous = json.loads(subprocess.check_output(['git','show','HEAD:'+relative], cwd=root))
    for key in previous:
        if key != 'p1l4': assert current[key] == previous[key], (relative,key)
    for key in previous['p1l4']:
        if key not in ('sectionMaterials','pmCurves'):
            assert current['p1l4'][key] == previous['p1l4'][key], (relative,key)
    old_materials = {m['sectionId']:m for m in previous['p1l4']['sectionMaterials']}
    capacities = {c['sectionId']:c for c in current['p1l4']['pmCurves']}
    for material in current['p1l4']['sectionMaterials']:
        old = old_materials[material['sectionId']]
        if material.get('elementType') != 'viga':
            assert material == old
            continue
        assert material['steelBars'] == 12
        assert material['topBars'] == material['bottomBars'] == 4
        assert material['sideBarsEach'] == 2 and material['barDiameter_mm'] == 25
        assert math.isclose(material['Ast_mm2'],12*math.pi*25**2/4)
        for key in ('stirrupCount','stirrupDiameter_mm','stirrupSpacing_mm','stirrupLegs',
                    'b_m','h_m','fc_MPa','fy_MPa','cover_mm'):
            assert material[key] == old[key], (relative,material['sectionId'],key)
        curve = capacities[material['sectionId']]
        assert curve['steelBars'] == 12 and math.isclose(curve['Ast_mm2'],material['Ast_mm2'])
        assert len(curve['momentCurvature']) == 400
        assert all(math.isfinite(p['M_kN_m']) for p in curve['momentCurvature'])
    edits=json.loads((root/'P1L4/model_edits.json').read_text(encoding='utf-8'))
    for edit in edits['elements']:
        if edit['elementType'] == 'viga':
            assert edit['topBars'] == edit['bottomBars'] == 4
            assert edit['sideBarsEach'] == 2 and edit['barDiameter_mm'] == 25
    print('PASS',relative,': 12 Ø25, curvas consistentes, estribos/análisis/geometría intactos.')
