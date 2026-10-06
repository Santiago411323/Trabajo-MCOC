"""Actualiza solo armadura/capacidad del JSON actual; no altera el análisis elástico.

Las fuerzas, cargas y desplazamientos existentes se preservan exactamente.
Para modificar dimensiones/materiales utilizar el reanálisis del editor Unity.
"""
import copy
import sys
from pathlib import Path
import exportar_resultados_unity as exporter


def enrich(data, cvm):
    output=copy.deepcopy(data)
    results=output['p1l4']
    materials=results['sectionMaterials']
    curves=results['pmCurves']
    curve_ids={c['sectionId'] for c in curves}
    for material in materials:
        if material.get('elementType')!='viga':
            continue
        # Never overwrite previously edited steel.
        if material.get('steelBars',0)>0:
            continue
        material.update(exporter.beam_reinforcement_defaults(material))
        cid=material['sectionId']
        if cid not in curve_ids:
            curves.append(exporter.beam_capacity_curve(material,cvm))
            curve_ids.add(cid)
        print('Armadura y capacidad:',cid,flush=True)
    return output


if __name__=='__main__':
    root=Path(__file__).resolve().parent.parent
    sys.path.insert(0,str(root/'.venv/Lib/site-packages'))
    sys.path.insert(0,str(root/'P1L3'))
    import carga_viva_sismo as cvm
    data=exporter.load_json(exporter.JSON_OUT)
    output=enrich(data,cvm)
    for key in data['p1l4']:
        if key not in ('sectionMaterials','pmCurves'):
            assert data['p1l4'][key]==output['p1l4'][key],key
    for key in data:
        if key!='p1l4':
            assert data[key]==output[key],key
    exporter.write_json(exporter.JSON_OUT,output)
    print('PASS: geometría, cargas, fuerzas y desplazamientos preservados.')
