"""Generate independent localForce references without changing production JSONs."""
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.venv/Lib/site-packages'))
sys.path.insert(0, str(ROOT / 'P1L3'))
import carga_viva_sismo as cvm


def main(output):
    data = json.loads((ROOT / 'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json').read_text(encoding='utf-8'))
    live = cvm.transfer_live_load(data, data['Q_kN_m2'])
    seismic = cvm.build_seismic_cases(data, live, data['seismic_coefficient'])
    loads = {'G': cvm.dead_nodal_loads(data),
             'Q': cvm.vector_loads_from_dict(live['cargas_nodales_Q']),
             'EX': cvm.vector_loads_from_dict(seismic['cargas_nodales_EX']),
             'EY': cvm.vector_loads_from_dict(seismic['cargas_nodales_EY'])}
    cases = dict(loads)
    for combo in data['p1l4']['combinations']:
        cases[combo['name']] = cvm.combine_nodal_loads(loads, combo)
    nodes = cvm.node_map(data)
    result = {'records': [], 'fixedNodes': []}
    for case, nodal_loads in cases.items():
        analysis = cvm.run_and_extract(data, nodal_loads)
        assert analysis['ok'], case
        for e in data['elements']:
            # Independent reference from OpenSees, never from the Unity converter.
            f = list(cvm.ops.eleResponse(e['id'], 'localForce'))
            length = cvm.element_length(e, nodes)
            assert len(f) == 12
            # This model has ONLY nodal loads. Check that constant N,V and linear M
            # reproduce both end actions, rather than assuming a distributed load.
            assert abs(f[0]+f[6]) < 1e-6
            assert abs(f[1]+f[7]) < 1e-6
            assert abs(f[2]+f[8]) < 1e-6
            assert abs(f[4]+f[2]*length+f[10]) < 1e-6
            assert abs(f[5]-f[1]*length+f[11]) < 1e-6
            points = [[-f[0], f[1], f[2], f[3], f[4]+f[2]*length*t, f[5]-f[1]*length*t]
                      for t in (0, .25, .5, .75, 1)]
            result['records'].append({'combo': case, 'id': e['id'], 'local': f,
                                      'length': length, 'points': points})
        result['fixedNodes'] = sorted(cvm.ops.getFixedNodes())
    cvm.ops.wipe()
    Path(output).write_text(json.dumps(result), encoding='utf-8')
    print(f"OpenSees: {len(result['records'])} referencias locales; {len(result['fixedNodes'])} nodos restringidos.")


if __name__ == '__main__':
    main(sys.argv[1])
