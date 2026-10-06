"""Actualiza solo armadura/capacidad del JSON actual; no altera el análisis elástico.

Las fuerzas, cargas y desplazamientos existentes se preservan exactamente.
Para modificar dimensiones/materiales utilizar el reanálisis del editor Unity.
"""
import copy
import argparse
import math
import sys
from pathlib import Path
import exportar_resultados_unity as exporter


def enrich(data, cvm, reset_longitudinal=False):
    output=copy.deepcopy(data)
    results=output['p1l4']
    materials=results['sectionMaterials']
    curves=results['pmCurves']
    curve_ids={c['sectionId'] for c in curves}
    for material in materials:
        if material.get('elementType')!='viga':
            continue
        # Never overwrite previously edited steel.
        if material.get('steelBars',0)>0 and not reset_longitudinal:
            continue
        if reset_longitudinal and material.get('steelBars',0)>0:
            # Cambiar únicamente armadura longitudinal; conservar estribos editados.
            ast = 12 * math.pi * 25**2 / 4
            material.update(steelBars=12, barDiameter_mm=25.0, topBars=4,
                            bottomBars=4, sideBarsEach=2, Ast_mm2=ast,
                            rho_percent=100*ast/(material['b_m']*material['h_m']*1e6))
            material['note'] = (
                '12 Ø25: 4 superiores, 4 inferiores y 2 por cada lado. '
                f"{material['stirrupCount']} estribos dobles Ø{material['stirrupDiameter_mm']:g}"
                f"@{material['stirrupSpacing_mm']:g} mm ({material['stirrupLegs']} ramas). "
                'Sin confinamiento ni capacidad de corte evaluados.')
        else:
            material.update(exporter.beam_reinforcement_defaults(material))
        cid=material['sectionId']
        if reset_longitudinal:
            curves[:] = [c for c in curves if c['sectionId'] != cid]
        if reset_longitudinal or cid not in curve_ids:
            curves.append(exporter.beam_capacity_curve(material,cvm))
            curve_ids.add(cid)
        print('Armadura y capacidad:',cid,flush=True)
    return output


if __name__=='__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', type=Path, default=exporter.JSON_OUT)
    parser.add_argument('--reset-longitudinal', action='store_true',
                        help='Aplicar explícitamente 12 Ø25 a todas las vigas; conservar estribos.')
    args = parser.parse_args()
    root=Path(__file__).resolve().parent.parent
    sys.path.insert(0,str(root/'.venv/Lib/site-packages'))
    sys.path.insert(0,str(root/'P1L3'))
    import carga_viva_sismo as cvm
    data=exporter.load_json(args.model)
    output=enrich(data,cvm,args.reset_longitudinal)
    for key in data['p1l4']:
        if key not in ('sectionMaterials','pmCurves'):
            assert data['p1l4'][key]==output['p1l4'][key],key
    for key in data:
        if key!='p1l4':
            assert data[key]==output[key],key
    exporter.write_json(args.model,output)
    print('PASS: geometría, cargas, fuerzas y desplazamientos preservados.')
