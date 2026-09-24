"""Auditoria de lectura: contrasta el JSON de Unity y el modelo P1L3.

No modifica apoyos, cargas ni resultados de produccion. --reanalyze reproduce
los siete casos en memoria y compara eleForce con eleResponse('localForce').
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
VIEWER = ROOT / 'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json'
BASE = ROOT / 'P1L2/unity_visualizador/Assets/Resources/estructura_completo_unity.json'


def sub(a, b):
    return [x - y for x, y in zip(a, b)]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def norm(v):
    return math.sqrt(dot(v, v))


def xyz(node):
    return [node[k] for k in ('x', 'y', 'z')]


def axes(element, nodes):
    delta = sub(xyz(nodes[element['nodeJ']]), xyz(nodes[element['nodeI']]))
    length = norm(delta)
    x = [v / length for v in delta]
    reference = [1, 0, 0] if abs(delta[2])/length > .90 else [0, 0, 1]
    y = cross(reference, x)
    y = [v / norm(y) for v in y]
    return length, (x, y, cross(x, y))


def to_local(raw, basis):
    return [dot(raw[start:start+3], axis) for start in (0, 3, 6, 9) for axis in basis]


def equilibrium(raw, length):
    # No eleLoad in P1L3: all element loads are zero between its end nodes.
    return [raw[0]+raw[6], raw[1]+raw[7], raw[2]+raw[8], raw[3]+raw[9],
            raw[4]+raw[10]-length*raw[8], raw[5]+raw[11]+length*raw[7]]


def panel_values(raw, length, t):
    # Exact formulas used by the current selected-beam panel (My,Mz,Vy,Vz,N).
    x = t * length
    qy, qz = (raw[1]+raw[7])/length, (raw[2]+raw[8])/length
    return [raw[4]+raw[2]*x-.5*qz*x*x, raw[5]-raw[1]*x+.5*qy*x*x,
            raw[1]-qy*x, raw[2]-qz*x, -raw[0]]


def topology(data):
    nodes = {n['id']: n for n in data['nodes']}
    supports = {s['node'] for s in data['supports']}
    graph = defaultdict(set)
    for e in data['elements']:
        graph[e['nodeI']].add(e['nodeJ'])
        graph[e['nodeJ']].add(e['nodeI'])
    seen, components = set(), []
    for seed in sorted(graph):
        if seed in seen:
            continue
        stack, comp = [seed], set()
        seen.add(seed)
        while stack:
            node = stack.pop()
            comp.add(node)
            for nxt in graph[node] - seen:
                seen.add(nxt)
                stack.append(nxt)
        members = [e for e in data['elements'] if e['nodeI'] in comp]
        components.append({
            'nodes': sorted(comp), 'element_ids': [e['id'] for e in members],
            'element_tags': [e.get('elementTag', str(e['id'])) for e in members],
            'support_nodes': sorted(comp & supports),
            'automatic_anchor': None if comp & supports else min(comp, key=lambda n: nodes[n]['z']),
        })
    coords = defaultdict(list)
    for node in nodes.values():
        coords[tuple(round(v, 6) for v in xyz(node))].append(node['id'])
    col_visual_changes = []
    for e in data['elements']:
        if e['type'] != 'columna' or e.get('sourceBuilding') != 'edificio_2':
            continue
        zi, zj = nodes[e['nodeI']]['z'], nodes[e['nodeJ']]['z']
        if abs(zj-zi) < .3:
            col_visual_changes.append({'id': e['id'], 'effect': 'hidden_base_stub'})
        else:
            snapped = [min((-4, 0, 4, 8, 12, 16), key=lambda level: abs(z-level)) for z in (zi, zj)]
            if any(abs(z-s) > 1e-6 for z, s in zip((zi, zj), snapped)):
                col_visual_changes.append({'id': e['id'], 'z_analysis': [zi, zj], 'z_visual': snapped})
    return {
        'components': components,
        'unsupported_components': sum(not c['support_nodes'] for c in components),
        'artificial_anchors': [c['automatic_anchor'] for c in components if not c['support_nodes']],
        'unsupported_element_count': sum(len(c['element_ids']) for c in components if not c['support_nodes']),
        'isolated_nodes_fixed_automatically': sorted(set(nodes)-set(graph)-supports),
        'endpoint_count': len(data['elements'])*2,
        'endpoints_with_declared_support': sum(e[k] in supports for e in data['elements'] for k in ('nodeI', 'nodeJ')),
        'coincident_node_groups': [v for v in coords.values() if len(v) > 1],
        'visual_column_changes': col_visual_changes,
        'support_dof_patterns': dict(Counter(''.join(str(s.get(k, 0)) for k in ('ux','uy','uz','rx','ry','rz')) for s in data['supports'])),
    }


def saved_results(data):
    nodes = {n['id']: n for n in data['nodes']}
    elements = {e['id']: e for e in data['elements']}
    summary = defaultdict(lambda: {'records': 0, 'global_treated_as_local_failures': 0,
                                  'rotated_local_failures': 0, 'wrong_panel_records': 0,
                                  'wrong_components_by_type': {'viga': 0, 'columna': 0},
                                  'max_correct_moment_residual_kNm': 0.0})
    details, examples = [], []
    for record in data['p1l4']['elementForces']:
        e, raw = elements[record['id']], record['f']
        length, basis = axes(e, nodes)
        local = to_local(raw, basis)
        residual_bad, residual_good = equilibrium(raw, length), equilibrium(local, length)
        bad = max(abs(v) for v in residual_bad) > .01
        good = max(abs(v) for v in residual_good) <= .01
        error = max(abs(a-b) for t in (0, .25, .5, .75, 1)
                    for a, b in zip(panel_values(raw, length, t), panel_values(local, length, t)))
        s = summary[record['combo']]
        s['records'] += 1
        s['global_treated_as_local_failures'] += bad
        s['rotated_local_failures'] += not good
        s['wrong_panel_records'] += error > .01
        if error > .01:
            s['wrong_components_by_type'][e['type']] += 1
        s['max_correct_moment_residual_kNm'] = max(s['max_correct_moment_residual_kNm'], *map(abs, residual_good[4:]))
        detail = {'id': e['id'], 'tag': e.get('elementTag'), 'type': e['type'], 'combo': record['combo'],
                  'max_panel_component_error': error, 'wrong_moment_residual': residual_bad[4:],
                  'correct_moment_residual': residual_good[4:]}
        details.append(detail)
        if (e.get('elementTag') == 'B3003_V60/80' and record['combo'] in ('G', 'C1')) or (e['id'] in (84,272) and record['combo'] == 'C1'):
            examples.append({**detail, 'nodes': [nodes[e['nodeI']], nodes[e['nodeJ']]],
                             'length': length, 'raw_global': raw, 'local_end_actions': local,
                             'current_panel_I_mid_J': [panel_values(raw, length, t) for t in (0,.5,1)],
                             'correct_panel_I_mid_J': [panel_values(local, length, t) for t in (0,.5,1)]})
    return {'by_case': dict(summary), 'examples': examples, 'all_elements': details}


def reanalyze(data):
    try:
        import openseespy.opensees as ops
    except ImportError:
        # Reuse the project's installed packages when its old Python launcher is missing.
        sys.path.insert(0, str(ROOT / '.venv/Lib/site-packages'))
        import openseespy.opensees as ops
    sys.path.insert(0, str(ROOT / 'P1L3'))
    import carga_viva_sismo as cvm
    live = cvm.transfer_live_load(data, data['Q_kN_m2'])
    seismic = cvm.build_seismic_cases(data, live, data['seismic_coefficient'])
    load_sets = {'G': cvm.dead_nodal_loads(data), 'Q': cvm.vector_loads_from_dict(live['cargas_nodales_Q']),
                 'EX': cvm.vector_loads_from_dict(seismic['cargas_nodales_EX']),
                 'EY': cvm.vector_loads_from_dict(seismic['cargas_nodales_EY'])}
    cases = dict(load_sets)
    for c in data['p1l4']['combinations']:
        cases[c['name']] = cvm.combine_nodal_loads(load_sets, c)
    saved = {(r['combo'], r['id']): r['f'] for r in data['p1l4']['elementForces']}
    nodes = {n['id']: n for n in data['nodes']}
    report = {}
    declared = {s['node'] for s in data['supports']}
    for case, loads in cases.items():
        print('Reanalizando', case, flush=True)
        result = cvm.run_and_extract(data, loads)
        if not result['ok']:
            raise RuntimeError(f'OpenSees no converge: {case}')
        max_saved_error = max_local_error = 0.0
        for e in data['elements']:
            raw = result['element_forces'][e['id']]
            _, basis = axes(e, nodes)
            local = list(ops.eleResponse(e['id'], 'localForce'))
            max_saved_error = max(max_saved_error, *(abs(a-b) for a,b in zip(raw, saved[(case,e['id'])])))
            max_local_error = max(max_local_error, *(abs(a-b) for a,b in zip(to_local(raw,basis), local)))
        actual_fixed = set(ops.getFixedNodes())
        extra_reactions = {str(n): list(ops.nodeReaction(n)) for n in sorted(actual_fixed-declared)
                           if max(map(abs, ops.nodeReaction(n))) > .001}
        sum_r = [sum(ops.nodeReaction(n)[d] for n in actual_fixed) for d in range(3)]
        sum_load = [sum(v[d] for v in loads.values()) for d in range(3)]
        report[case] = {'ok': True, 'max_saved_global_force_difference': max_saved_error,
                        'max_rotated_vs_opensees_local_difference': max_local_error,
                        'fixed_nodes_in_opensees': len(actual_fixed), 'extra_nonzero_reactions': extra_reactions,
                        'sum_loads': sum_load, 'sum_all_reactions': sum_r,
                        'sum_declared_reactions': result['reactions'],
                        'global_force_balance': [a+b for a,b in zip(sum_load, sum_r)]}
    # Independent benchmark: changing the load application changes the diagram.
    benchmarks = {}
    for mode in ('end_nodal', 'uniform_element'):
        ops.wipe()
        ops.model('basic', '-ndm', 3, '-ndf', 6)
        ops.node(1, 0, 0, 0)
        ops.node(2, 6, 0, 0)
        ops.fix(1, 1,1,1,1,1,1)
        # Leave the unloaded axial DOF free so the solver has a nonempty system;
        # both bending rotations and transverse displacements remain fixed.
        ops.fix(2, 0,1,1,1,1,1)
        ops.geomTransf('Linear', 1, 0,0,1)
        ops.element('elasticBeamColumn', 1, 1,2, .48, 25e6, 25e6/2.4, .04, .0256, .0144, 1)
        ops.timeSeries('Linear', 1)
        ops.pattern('Plain', 1, 1)
        if mode == 'end_nodal':
            for node in (1,2):
                ops.load(node, 0,0,-30,0,0,0)
        else:
            ops.eleLoad('-ele',1,'-type','-beamUniform',0,-10,0)
        ops.system('BandGeneral')
        ops.numberer('RCM')
        ops.constraints('Plain')
        ops.integrator('LoadControl',1)
        ops.algorithm('Linear')
        ops.analysis('Static')
        code = ops.analyze(1)
        benchmarks[mode] = {'ok': code == 0, 'local_forces': list(ops.eleResponse(1,'localForce'))}
    ops.wipe()
    assert all(c['max_saved_global_force_difference'] < 1e-6 for c in report.values())
    assert all(c['max_rotated_vs_opensees_local_difference'] < 1e-6 for c in report.values())
    assert all(max(map(abs, c['global_force_balance'])) < 1e-6 for c in report.values())
    assert max(map(abs, benchmarks['end_nodal']['local_forces'])) < 1e-8
    assert abs(benchmarks['uniform_element']['local_forces'][2] - 30) < 1e-8
    assert abs(benchmarks['uniform_element']['local_forces'][4] + 30) < 1e-8
    return {'cases': report, 'fixed_beam_L6_q10_benchmark': benchmarks, 'opensees_version': ops.version()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reanalyze', action='store_true')
    args = parser.parse_args()
    data = json.loads(VIEWER.read_text(encoding='utf-8'))
    base = json.loads(BASE.read_text(encoding='utf-8'))
    audit = {'source_sha256': hashlib.sha256(VIEWER.read_bytes()).hexdigest(),
             'counts': {k: len(data[k]) for k in ('nodes','elements','supports','walls','diaphragmList')},
             'base_geometry_matches_viewer': {k: base[k] == data[k] for k in ('nodes','elements','supports')},
             'topology': topology(data), 'results': saved_results(data)}
    if args.reanalyze:
        audit['opensees_recheck'] = reanalyze(data)
    dest = ROOT / 'reports/auditoria_diagramas.json'
    dest.write_text(json.dumps(audit, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k: v for k,v in audit.items() if k not in ('topology','results','opensees_recheck')}, indent=2))
    print(json.dumps(audit['results']['by_case'], indent=2))
    print('Componentes sin apoyo:', audit['topology']['unsupported_components'])
    print('Elementos sin apoyo declarado en su componente:', audit['topology']['unsupported_element_count'])
    print('Auditoria guardada en', dest)


if __name__ == '__main__':
    main()
