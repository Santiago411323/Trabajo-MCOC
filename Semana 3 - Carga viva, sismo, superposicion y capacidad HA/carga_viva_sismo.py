#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Carga viva Q y sismo pseudoestatico EX/EY para el edificio completo.

Usa la geometria tributaria ya generada para el edificio completo, pero guarda
los resultados dentro de la carpeta Semana 3 para mantener trazabilidad semanal.

Ejemplos:
  python "Semana 3 - Carga viva, sismo, superposicion y capacidad HA/carga_viva_sismo.py" --sc-kg-m2 500
  python "Semana 3 - Carga viva, sismo, superposicion y capacidad HA/carga_viva_sismo.py" --sc-kg-m2 500 --id B3002_V60/80
  python "Semana 3 - Carga viva, sismo, superposicion y capacidad HA/carga_viva_sismo.py" --sc-kg-m2 500 --id L1
"""

import argparse
import json
import math
import sys
from pathlib import Path

try:
    import openseespy.opensees as ops
except ImportError:
    ops = None

try:
    import matplotlib.pyplot as plt
except ImportError:
    plt = None


BASE_DIR = Path(__file__).resolve().parent
ROOT_DIR = BASE_DIR.parent
JSON_PATH = ROOT_DIR / "P1L2" / "unity_visualizador" / "Assets" / "Resources" / "estructura_completo_unity.json"
OUTPUT_PATH = BASE_DIR / "resultados" / "carga_viva_sismo.json"
UNITY_RESULTS_PATH = ROOT_DIR / "P1L2" / "unity_visualizador" / "Assets" / "Resources" / "semana3_resultados_unity.json"

DEFAULT_Q_Q = 4.903325
DEFAULT_SEISMIC_COEFF = 0.20
FLOOR_GROUP_TOL_M = 0.25
G_ACCEL = 9.80665
E_CONCRETE = 25_000_000.0
NU_CONCRETE = 0.20
G_CONCRETE = E_CONCRETE / (2.0 * (1.0 + NU_CONCRETE))
DEFAULT_LAMBDAS = {"G": 1.0, "Q": 0.5, "EX": 1.0, "EY": 0.3}
BAR_DIAMETER_MM = 25.0


def load_json(path):
    with open(path, encoding="utf-8") as file:
        return json.load(file)


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as file:
        json.dump(data, file, indent=2, ensure_ascii=False)


def kg_m2_to_kn_m2(kg_m2):
    return kg_m2 * 9.80665 / 1000.0


def node_map(data):
    return {node["id"]: node for node in data.get("nodes", [])}


def element_length(element, nodes):
    ni = nodes[element["nodeI"]]
    nj = nodes[element["nodeJ"]]
    return math.dist((ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]))


def section_properties(width, height):
    area = width * height
    iy = width * height**3 / 12.0
    iz = height * width**3 / 12.0
    j = iy + iz
    return area, iy, iz, j


def element_mid_z(element, nodes):
    ni = nodes[element["nodeI"]]
    nj = nodes[element["nodeJ"]]
    return round(0.5 * (ni["z"] + nj["z"]), 3)


def element_tag(element):
    return element.get("elementTag") or element.get("sourceId") or element.get("id")


def normalize_id(value):
    return str(value).strip().lower()


def id_matches(value, wanted):
    return normalize_id(value) == normalize_id(wanted)


def transfer_live_load(data, q_q):
    nodes = node_map(data)
    nodal_loads = {node_id: {"Fx": 0.0, "Fy": 0.0, "Fz": 0.0} for node_id in nodes}
    beams = []
    by_floor = {}
    total_area = 0.0
    total_q = 0.0

    for element in data.get("elements", []):
        if element.get("type") != "viga":
            continue
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue

        area = float(element.get("areaTributaria") or element.get("tributaryArea") or 0.0)
        if area <= 0.0:
            continue

        q_total = q_q * area
        length = element_length(element, nodes)
        q_lineal = q_total / length if length > 0.0 else 0.0
        floor = element_mid_z(element, nodes)

        nodal_loads[element["nodeI"]]["Fz"] -= 0.5 * q_total
        nodal_loads[element["nodeJ"]]["Fz"] -= 0.5 * q_total

        total_area += area
        total_q += q_total
        by_floor.setdefault(str(floor), {"area_m2": 0.0, "Q_kN": 0.0, "beams": 0})
        by_floor[str(floor)]["area_m2"] += area
        by_floor[str(floor)]["Q_kN"] += q_total
        by_floor[str(floor)]["beams"] += 1

        beams.append({
            "id": element["id"],
            "elementTag": element_tag(element),
            "sourceBuilding": element.get("sourceBuilding"),
            "floor_z_m": floor,
            "area_tributaria_m2": area,
            "q_Q_kN_m2": q_q,
            "Q_total_kN": q_total,
            "Q_lineal_kN_m": q_lineal,
            "nodeI": element["nodeI"],
            "nodeJ": element["nodeJ"],
            "nodal_Fz_each_kN": -0.5 * q_total,
        })

    expected = q_q * total_area
    return {
        "q_Q_kN_m2": q_q,
        "area_total_m2": total_area,
        "Q_transferida_kN": total_q,
        "q_Q_por_A_kN": expected,
        "error_conservacion_kN": total_q - expected,
        "error_relativo": (total_q - expected) / expected if expected else 0.0,
        "por_piso": by_floor,
        "vigas": beams,
        "cargas_nodales_Q": {str(k): v for k, v in nodal_loads.items() if abs(v["Fz"]) > 1e-12},
    }


def dead_load_by_floor(data):
    nodes = node_map(data)
    by_floor = {}
    for element in data.get("elements", []):
        if element.get("type") != "viga":
            continue
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue
        floor = str(element_mid_z(element, nodes))
        by_floor[floor] = by_floor.get(floor, 0.0) + float(element.get("deadLoad") or 0.0)
    return by_floor


def floor_nodes_at_z(nodes, z):
    return [node for node in nodes.values() if abs(round(node["z"], 3) - float(z)) < 1e-6]


def closest_node_to_xy(nodes, x, y):
    return min(nodes, key=lambda node: (node["x"] - x) ** 2 + (node["y"] - y) ** 2)


def build_seismic_cases(data, live_transfer, seismic_coeff):
    nodes = node_map(data)
    dead = dead_load_by_floor(data)
    live = {floor: values["Q_kN"] for floor, values in live_transfer["por_piso"].items()}
    floor_names = floor_name_by_z(data)
    floor_groups = group_close_floors(set(dead) | set(live), FLOOR_GROUP_TOL_M)
    ex_nodal = {}
    ey_nodal = {}
    floor_rows = []

    for floor_group in floor_groups:
        floor_nodes = floor_nodes_for_group(nodes, floor_group)
        if not floor_nodes:
            continue
        floor = weighted_floor_z(floor_group, dead, live)
        cm_x = sum(node["x"] for node in floor_nodes) / len(floor_nodes)
        cm_y = sum(node["y"] for node in floor_nodes) / len(floor_nodes)
        application_node = closest_node_to_xy(floor_nodes, cm_x, cm_y)
        d = sum(dead.get(level, 0.0) for level in floor_group)
        q = sum(live.get(level, 0.0) for level in floor_group)
        seismic_weight = d + 0.5 * q
        lateral = seismic_coeff * seismic_weight

        ex_nodal[str(application_node["id"])] = {"Fx": lateral, "Fy": 0.0, "Fz": 0.0}
        ey_nodal[str(application_node["id"])] = {"Fx": 0.0, "Fy": lateral, "Fz": 0.0}
        floor_rows.append({
            "piso": floor_label(floor_group, floor, floor_names),
            "floor_z_m": float(floor),
            "niveles_agrupados_z_m": [float(level) for level in floor_group],
            "D_kN": d,
            "Q_kN": q,
            "W_sismico_D_plus_0_5Q_kN": seismic_weight,
            "masa_equivalente_kN_s2_m": seismic_weight / G_ACCEL,
            "coeficiente_sismico": seismic_coeff,
            "F_EX_kN": lateral,
            "F_EY_kN": lateral,
            "centro_masa_estimado": {"x": cm_x, "y": cm_y, "z": float(floor)},
            "nodo_aplicacion": application_node["id"],
            "torsion_EX_respecto_CM_kN_m": lateral * (application_node["y"] - cm_y),
            "torsion_EY_respecto_CM_kN_m": -lateral * (application_node["x"] - cm_x),
        })

    total_ex = sum(row["F_EX_kN"] for row in floor_rows)
    total_ey = sum(row["F_EY_kN"] for row in floor_rows)
    return {
        "coeficiente_sismico": seismic_coeff,
        "hipotesis_masa": "W_sismico = D + 0.5Q",
        "pisos": floor_rows,
        "carga_lateral_total_EX_kN": total_ex,
        "carga_lateral_total_EY_kN": total_ey,
        "corte_basal_EX_kN": total_ex,
        "corte_basal_EY_kN": total_ey,
        "cargas_nodales_EX": ex_nodal,
        "cargas_nodales_EY": ey_nodal,
        "verificaciones": {
            "carga_lateral_total_igual_corte_basal_EX": abs(total_ex - sum(v["Fx"] for v in ex_nodal.values())) < 1e-9,
            "carga_lateral_total_igual_corte_basal_EY": abs(total_ey - sum(v["Fy"] for v in ey_nodal.values())) < 1e-9,
            "sentido_deformada_esperado_EX": "+X para coeficiente positivo",
            "sentido_deformada_esperado_EY": "+Y para coeficiente positivo",
            "torsion": "Se reporta como F por excentricidad del nodo de aplicacion respecto del CM estimado.",
        },
    }


def floor_name_by_z(data):
    names = {}
    for slab in data.get("slabs", []):
        z = slab.get("z")
        name = slab.get("nivel")
        if z is None or not name:
            continue
        key = str(round(float(z), 3))
        names.setdefault(key, str(name))
    return names


def group_close_floors(floors, tolerance):
    groups = []
    for floor in sorted((float(value) for value in floors), key=float):
        if not groups or abs(floor - groups[-1][-1]) > tolerance:
            groups.append([floor])
        else:
            groups[-1].append(floor)
    return [[str(round(level, 3)) for level in group] for group in groups]


def weighted_floor_z(floor_group, dead, live):
    total_weight = sum(dead.get(level, 0.0) + live.get(level, 0.0) for level in floor_group)
    if total_weight <= 0.0:
        return sum(float(level) for level in floor_group) / len(floor_group)
    return sum(float(level) * (dead.get(level, 0.0) + live.get(level, 0.0)) for level in floor_group) / total_weight


def floor_nodes_for_group(nodes, floor_group):
    levels = [float(level) for level in floor_group]
    return [
        node for node in nodes.values()
        if any(abs(round(node["z"], 3) - level) < 1e-6 for level in levels)
    ]


def floor_label(floor_group, floor, floor_names):
    z = float(floor)
    names = []
    for level in floor_group:
        name = floor_names.get(str(round(float(level), 3)))
        if name and name not in names:
            names.append(name)
    name = " / ".join(names)
    if name:
        return f"{name} (z={z:.3f} m)"
    return f"Piso z={z:.3f} m"


def slab_area(slab):
    return abs((float(slab["x1"]) - float(slab["x0"])) * (float(slab["y1"]) - float(slab["y0"])))


def query_beam(live_transfer, wanted_id):
    for beam in live_transfer["vigas"]:
        if any(id_matches(value, wanted_id) for value in (beam.get("id"), beam.get("elementTag"))):
            return {"tipo": "viga", "consulta": wanted_id, **beam}
    return None


def query_slab(data, q_q, wanted_id):
    wanted = normalize_id(wanted_id)
    for slab in data.get("slabs", []):
        slab_id = str(slab.get("id"))
        slab_number = slab_id[1:] if slab_id[:1].lower() in ("l", "s") else slab_id
        ids = [slab_id, f"L{slab_number}", f"S{slab_number}"]
        if slab_number.isdigit():
            ids.append(slab_number)
        if not any(normalize_id(value) == wanted for value in ids):
            continue

        area = slab_area(slab)
        return {
            "tipo": "losa",
            "consulta": wanted_id,
            "id": slab_id,
            "nivel": slab.get("nivel"),
            "x0": slab.get("x0"),
            "y0": slab.get("y0"),
            "x1": slab.get("x1"),
            "y1": slab.get("y1"),
            "z": slab.get("z"),
            "area_m2": area,
            "q_Q_kN_m2": q_q,
            "Q_total_kN": q_q * area,
            "nota": "Carga viva superficial de esta losa. La transferencia final se verifica sobre las vigas/nodos tributarios.",
        }
    return None


def query_item(data, live_transfer, q_q, wanted_id):
    return query_beam(live_transfer, wanted_id) or query_slab(data, q_q, wanted_id) or {
        "error": "No se encontro el ID solicitado.",
        "id_buscado": wanted_id,
        "ayuda": "Vigas: ID numerico o elementTag, ej. 359 o B3002_V60/80. Losas: L1, L2... o D101, D102...",
    }


def build_result(data, q_q, seismic_coeff):
    live_transfer = transfer_live_load(data, q_q)
    seismic = build_seismic_cases(data, live_transfer, seismic_coeff)
    result = {
        "inputs": {
            "json_edificio_completo": str(JSON_PATH),
            "q_Q_kN_m2": q_q,
            "coeficiente_sismico": seismic_coeff,
            "convencion": "Fz negativo hacia abajo; EX positivo en X; EY positivo en Y.",
        },
        "parte_A_carga_viva_Q": live_transfer,
        "parte_B_sismo_pseudoestatico": seismic,
    }
    write_json(OUTPUT_PATH, result)
    return live_transfer, seismic, result


def print_global_summary(live_transfer, seismic):
    print("\nResultado global del edificio completo")
    print(f"q_Q = {live_transfer['q_Q_kN_m2']:.6f} kN/m2")
    print(f"Area tributaria A = {live_transfer['area_total_m2']:.3f} m2")
    print(f"sum(Q transferida) = {live_transfer['Q_transferida_kN']:.3f} kN")
    print(f"q_Q*A = {live_transfer['q_Q_por_A_kN']:.3f} kN")
    print(f"error = {live_transfer['error_conservacion_kN']:.6e} kN")
    print(f"carga lateral total EX = {seismic['carga_lateral_total_EX_kN']:.3f} kN")
    print(f"carga lateral total EY = {seismic['carga_lateral_total_EY_kN']:.3f} kN")
    print(f"corte basal EX = {seismic['corte_basal_EX_kN']:.3f} kN")
    print(f"corte basal EY = {seismic['corte_basal_EY_kN']:.3f} kN")
    print(f"salida = {OUTPUT_PATH}")


def print_seismic_by_floor(seismic):
    print("\nSismo pseudoestatico EX/EY por piso")
    for index, row in enumerate(seismic["pisos"], start=1):
        cm = row["centro_masa_estimado"]
        print(f"\nPiso {index}: {row['piso']}")
        print(f"  D_piso                         = {row['D_kN']:.3f} kN")
        print(f"  Q_piso                         = {row['Q_kN']:.3f} kN")
        print(f"  W_sismico = D + 0.5Q           = {row['W_sismico_D_plus_0_5Q_kN']:.3f} kN")
        print(f"  Masa equivalente               = {row['masa_equivalente_kN_s2_m']:.3f} kN*s2/m")
        print(f"  Coeficiente sismico            = {row['coeficiente_sismico']:.3f}")
        print(f"  Fuerza EX                      = {row['F_EX_kN']:.3f} kN")
        print(f"  Fuerza EY                      = {row['F_EY_kN']:.3f} kN")
        print(f"  Centro de masa estimado        = x {cm['x']:.3f} m, y {cm['y']:.3f} m")
        print(f"  Nodo de aplicacion             = {row['nodo_aplicacion']}")
        print(f"  Torsion EX respecto del CM      = {row['torsion_EX_respecto_CM_kN_m']:.3f} kN*m")
        print(f"  Torsion EY respecto del CM      = {row['torsion_EY_respecto_CM_kN_m']:.3f} kN*m")
    print("\nTotales del caso sismico")
    print(f"Carga lateral total EX = {seismic['carga_lateral_total_EX_kN']:.3f} kN")
    print(f"Carga lateral total EY = {seismic['carga_lateral_total_EY_kN']:.3f} kN")
    print(f"Corte basal EX         = {seismic['corte_basal_EX_kN']:.3f} kN")
    print(f"Corte basal EY         = {seismic['corte_basal_EY_kN']:.3f} kN")
    print(f"salida = {OUTPUT_PATH}")


def print_id_examples(data, live_transfer):
    print("\nEjemplos de IDs que puedes pedir")
    print("Vigas: usar ID numerico o elementTag")
    for beam in live_transfer["vigas"][:10]:
        print(f"  {beam['id']}  |  {beam['elementTag']}")

    print("\nLosas: usar ID de losa")
    for slab in data.get("slabs", [])[:10]:
        print(f"  {slab.get('id')}  |  nivel={slab.get('nivel')}  z={slab.get('z')}")


def build_model(data):
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para correr la Parte C.")

    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)
    nodes = node_map(data)
    connected_nodes = set()
    adjacency = {node_id: set() for node_id in nodes}

    for element in data.get("elements", []):
        ni = element.get("nodeI")
        nj = element.get("nodeJ")
        if ni not in nodes or nj not in nodes:
            continue
        connected_nodes.add(ni)
        connected_nodes.add(nj)
        adjacency[ni].add(nj)
        adjacency[nj].add(ni)

    for node in nodes.values():
        ops.node(node["id"], node["x"], node["y"], node["z"])

    support_nodes = set()
    for support in data.get("supports", []):
        node = support.get("node")
        if node in nodes:
            support_nodes.add(node)
            ops.fix(node, support.get("ux", 0), support.get("uy", 0), support.get("uz", 0), support.get("rx", 0), support.get("ry", 0), support.get("rz", 0))

    ops.geomTransf("Linear", 1, 0.0, 0.0, 1.0)
    ops.geomTransf("Linear", 2, 1.0, 0.0, 0.0)
    for element in data.get("elements", []):
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue
        width = float(element.get("width_m") or 0.60)
        height = float(element.get("height_m") or 0.80)
        area, iy, iz, j = section_properties(width, height)
        ni = nodes[element["nodeI"]]
        nj = nodes[element["nodeJ"]]
        length = element_length(element, nodes)
        dz = abs(nj["z"] - ni["z"])
        transf = 2 if length > 0.0 and dz / length > 0.90 else 1
        ops.element("elasticBeamColumn", element["id"], element["nodeI"], element["nodeJ"], area, E_CONCRETE, G_CONCRETE, j, iy, iz, transf)

    for node_id in nodes:
        if node_id not in connected_nodes and node_id not in support_nodes:
            ops.fix(node_id, 1, 1, 1, 1, 1, 1)

    visited = set()
    for node_id in sorted(connected_nodes):
        if node_id in visited:
            continue
        stack = [node_id]
        component = []
        visited.add(node_id)
        while stack:
            current = stack.pop()
            component.append(current)
            for nxt in adjacency.get(current, ()):
                if nxt not in visited:
                    visited.add(nxt)
                    stack.append(nxt)
        if not any(node in support_nodes for node in component):
            anchor = min(component, key=lambda n: nodes[n]["z"])
            ops.fix(anchor, 1, 1, 1, 1, 1, 1)

    return nodes


def dead_nodal_loads(data):
    nodes = node_map(data)
    nodal = {node_id: [0.0, 0.0, 0.0] for node_id in nodes}
    for element in data.get("elements", []):
        if element.get("type") != "viga":
            continue
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue
        total = float(element.get("deadLoad") or 0.0)
        nodal[element["nodeI"]][2] -= 0.5 * total
        nodal[element["nodeJ"]][2] -= 0.5 * total
    return nodal


def vector_loads_from_dict(loads):
    return {int(node): [float(vec.get("Fx", 0.0)), float(vec.get("Fy", 0.0)), float(vec.get("Fz", 0.0))] for node, vec in loads.items()}


def combine_nodal_loads(load_sets, lambdas):
    node_ids = sorted({node for loads in load_sets.values() for node in loads})
    combined = {node: [0.0, 0.0, 0.0] for node in node_ids}
    for case, loads in load_sets.items():
        factor = lambdas.get(case, 0.0)
        for node, vec in loads.items():
            combined[node][0] += factor * vec[0]
            combined[node][1] += factor * vec[1]
            combined[node][2] += factor * vec[2]
    return combined


def apply_nodal_loads(nodal_loads):
    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)
    for node, load in nodal_loads.items():
        if max(abs(load[0]), abs(load[1]), abs(load[2])) < 1e-12:
            continue
        ops.load(node, load[0], load[1], load[2], 0.0, 0.0, 0.0)


def analyze_case(data, nodal_loads, control_node, element_id):
    nodes = build_model(data)
    apply_nodal_loads(nodal_loads)
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints("Plain")
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    ok = ops.analyze(1)
    ops.reactions()

    reactions = [0.0, 0.0, 0.0]
    for support in data.get("supports", []):
        node = support.get("node")
        if node in nodes:
            reaction = ops.nodeReaction(node)
            reactions[0] += reaction[0]
            reactions[1] += reaction[1]
            reactions[2] += reaction[2]

    displacement = ops.nodeDisp(control_node)
    try:
        element_force = ops.eleForce(element_id)
    except Exception:
        element_force = []
    return {
        "ok": ok,
        "control_node": control_node,
        "control_displacement_ux_uy_uz_m": displacement[:3],
        "sum_reactions_Fx_Fy_Fz_kN": reactions,
        "element_id": element_id,
        "element_force_sample": element_force[:6],
    }


def max_abs(values):
    return max((abs(value) for value in values), default=0.0)


def superposition_check(data, live_transfer, seismic, lambdas):
    nodes = node_map(data)
    connected = {element["nodeI"] for element in data.get("elements", [])} | {element["nodeJ"] for element in data.get("elements", [])}
    control_node = max((node for node in nodes.values() if node["id"] in connected), key=lambda n: (n["z"], n["x"] ** 2 + n["y"] ** 2))["id"]
    element_id = next(element["id"] for element in data["elements"] if element.get("type") == "viga")
    load_sets = {
        "G": dead_nodal_loads(data),
        "Q": vector_loads_from_dict(live_transfer["cargas_nodales_Q"]),
        "EX": vector_loads_from_dict(seismic["cargas_nodales_EX"]),
        "EY": vector_loads_from_dict(seismic["cargas_nodales_EY"]),
    }
    cases = {name: analyze_case(data, loads, control_node, element_id) for name, loads in load_sets.items()}
    explicit = analyze_case(data, combine_nodal_loads(load_sets, lambdas), control_node, element_id)

    predicted_disp = [0.0, 0.0, 0.0]
    predicted_react = [0.0, 0.0, 0.0]
    predicted_force = [0.0] * len(cases["G"].get("element_force_sample", []))
    for case, result in cases.items():
        factor = lambdas[case]
        for i in range(3):
            predicted_disp[i] += factor * result["control_displacement_ux_uy_uz_m"][i]
            predicted_react[i] += factor * result["sum_reactions_Fx_Fy_Fz_kN"][i]
        for i, value in enumerate(result.get("element_force_sample", [])):
            predicted_force[i] += factor * value

    disp_error = [explicit["control_displacement_ux_uy_uz_m"][i] - predicted_disp[i] for i in range(3)]
    reaction_error = [explicit["sum_reactions_Fx_Fy_Fz_kN"][i] - predicted_react[i] for i in range(3)]
    force_error = [explicit.get("element_force_sample", [])[i] - predicted_force[i] for i in range(min(len(explicit.get("element_force_sample", [])), len(predicted_force)))]
    return {
        "lambdas": lambdas,
        "combinacion": "R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY",
        "control_node": control_node,
        "element_id": element_id,
        "case_results": cases,
        "superposed_prediction": {
            "control_displacement_ux_uy_uz_m": predicted_disp,
            "sum_reactions_Fx_Fy_Fz_kN": predicted_react,
            "element_force_sample": predicted_force,
        },
        "explicit_combination": explicit,
        "errors": {
            "disp_abs_m": disp_error,
            "reaction_abs_kN": reaction_error,
            "element_force_abs": force_error,
            "max_disp_abs_m": max_abs(disp_error),
            "max_reaction_abs_kN": max_abs(reaction_error),
            "max_element_force_abs": max_abs(force_error),
        },
    }


def print_superposition(superposition):
    print("\nParte C - Superposicion")
    print("R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY")
    print("Lambdas:")
    for case, factor in superposition["lambdas"].items():
        print(f"  lambda_{case} = {factor:.3f}")
    print(f"\nNodo de control = {superposition['control_node']}")
    print(f"Elemento de control = {superposition['element_id']}")

    predicted = superposition["superposed_prediction"]
    explicit = superposition["explicit_combination"]
    errors = superposition["errors"]
    print("\nDesplazamiento nodo control ux, uy, uz [m]")
    print(f"  Superposicion = {[round(v, 12) for v in predicted['control_displacement_ux_uy_uz_m']]}")
    print(f"  OpenSees expl. = {[round(v, 12) for v in explicit['control_displacement_ux_uy_uz_m']]}")
    print(f"  Error abs max  = {errors['max_disp_abs_m']:.3e} m")

    print("\nReacciones globales Fx, Fy, Fz [kN]")
    print(f"  Superposicion = {[round(v, 9) for v in predicted['sum_reactions_Fx_Fy_Fz_kN']]}")
    print(f"  OpenSees expl. = {[round(v, 9) for v in explicit['sum_reactions_Fx_Fy_Fz_kN']]}")
    print(f"  Error abs max  = {errors['max_reaction_abs_kN']:.3e} kN")

    print("\nFuerza interna elemento control, primeros 6 valores")
    print(f"  Superposicion = {[round(v, 9) for v in predicted['element_force_sample']]}")
    print(f"  OpenSees expl. = {[round(v, 9) for v in explicit['element_force_sample']]}")
    print(f"  Error abs max  = {errors['max_element_force_abs']:.3e}")


def concrete_stress(eps, fc):
    if eps <= 0.0:
        return 0.0
    return min(25_000_000.0 * eps, 0.85 * fc)


def steel_stress(eps, fy, es):
    return max(-fy, min(fy, es * eps))


def make_column_fibers():
    b = h = 0.70
    cover = 0.05
    fc = 25_000.0
    fy = 420_000.0
    es = 200_000_000.0
    bar_area = math.pi * (BAR_DIAMETER_MM / 1000.0) ** 2 / 4.0
    fibers = []
    nx = ny = 20
    for ix in range(nx):
        x = -b / 2.0 + (ix + 0.5) * b / nx
        for iy in range(ny):
            y = -h / 2.0 + (iy + 0.5) * h / ny
            fibers.append({"type": "concrete", "x": x, "y": y, "area": b / nx * h / ny})

    rebar_xy = [
        (-b / 2 + cover, -h / 2 + cover),
        (0.0, -h / 2 + cover),
        (b / 2 - cover, -h / 2 + cover),
        (-b / 2 + cover, 0.0),
        (b / 2 - cover, 0.0),
        (-b / 2 + cover, h / 2 - cover),
        (0.0, h / 2 - cover),
        (b / 2 - cover, h / 2 - cover),
    ]
    for x, y in rebar_xy:
        fibers.append({"type": "steel", "x": x, "y": y, "area": bar_area})
    return {"b": b, "h": h, "cover": cover, "fc": fc, "fy": fy, "Es": es, "bar_area_m2": bar_area, "fibers": fibers, "rebar_xy": rebar_xy}


def bar_diameter_mm(bar_area_m2):
    area_mm2 = bar_area_m2 * 1_000_000.0
    return math.sqrt(4.0 * area_mm2 / math.pi)


def define_opensees_fiber_section():
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para declarar la Fiber Section.")

    ops.wipe()
    ops.model("basic", "-ndm", 2, "-ndf", 3)
    concrete_tag = 1
    steel_tag = 2
    section_tag = 1
    fc = -25_000.0
    epsc0 = -0.002
    fcu = -20_000.0
    epscu = -0.003
    fy = 420_000.0
    es = 200_000_000.0
    b = h = 0.70
    cover = 0.05
    bar_area = math.pi * (BAR_DIAMETER_MM / 1000.0) ** 2 / 4.0

    ops.uniaxialMaterial("Concrete01", concrete_tag, fc, epsc0, fcu, epscu)
    ops.uniaxialMaterial("Steel01", steel_tag, fy, es, 0.01)
    ops.section("Fiber", section_tag)
    ops.patch("rect", concrete_tag, 20, 20, -h / 2, -b / 2, h / 2, b / 2)
    y_bot = -h / 2 + cover
    y_mid = 0.0
    y_top = h / 2 - cover
    z_left = -b / 2 + cover
    z_right = b / 2 - cover
    ops.layer("straight", steel_tag, 3, bar_area, y_bot, z_left, y_bot, z_right)
    ops.layer("straight", steel_tag, 2, bar_area, y_mid, z_left, y_mid, z_right)
    ops.layer("straight", steel_tag, 3, bar_area, y_top, z_left, y_top, z_right)
    return {
        "section_tag": section_tag,
        "concrete_material": {"tag": concrete_tag, "type": "Concrete01", "fc_kN_m2": fc, "epsc0": epsc0, "fcu_kN_m2": fcu, "epscu": epscu},
        "steel_material": {"tag": steel_tag, "type": "Steel01", "fy_kN_m2": fy, "Es_kN_m2": es, "b": 0.01},
        "patch": "rect concrete 20 x 20",
        "reinforcement": "3 barras abajo, 2 al medio, 3 arriba; diametro 25 mm por barra",
    }


def section_response(section, eps0, phi):
    p = 0.0
    m = 0.0
    max_steel_strain = 0.0
    max_concrete_strain = 0.0
    for fiber in section["fibers"]:
        eps = eps0 - phi * fiber["y"]
        if fiber["type"] == "concrete":
            stress = concrete_stress(eps, section["fc"])
            max_concrete_strain = max(max_concrete_strain, eps)
        else:
            stress = steel_stress(eps, section["fy"], section["Es"])
            max_steel_strain = max(max_steel_strain, abs(eps))
        force = stress * fiber["area"]
        p += force
        m += force * fiber["y"]
    return p, m, max_steel_strain, max_concrete_strain


def solve_eps0_for_p(section, phi, target_p):
    lo, hi = -0.02, 0.02
    p_lo, _, _, _ = section_response(section, lo, phi)
    p_hi, _, _, _ = section_response(section, hi, phi)
    while p_lo > target_p and lo > -1.0:
        lo *= 2.0
        p_lo, _, _, _ = section_response(section, lo, phi)
    while p_hi < target_p and hi < 1.0:
        hi *= 2.0
        p_hi, _, _, _ = section_response(section, hi, phi)
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        p, _, _, _ = section_response(section, mid, phi)
        if p < target_p:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def fiber_section_capacity():
    section = make_column_fibers()
    opensees_section = define_opensees_fiber_section()
    phis = [i * 0.00010 for i in range(1, 801)]
    steel_yield_strain = section["fy"] / section["Es"]
    mphi = []
    first_yield = None
    for phi in phis:
        eps0 = solve_eps0_for_p(section, phi, 0.0)
        p, m, max_steel_strain, max_concrete_strain = section_response(section, eps0, phi)
        row = {
            "phi_1_m": phi,
            "P_kN": p,
            "M_kN_m": abs(m),
            "eps0": eps0,
            "max_steel_strain": max_steel_strain,
            "max_concrete_strain": max_concrete_strain,
            "steel_yielded": max_steel_strain >= steel_yield_strain,
        }
        if first_yield is None and row["steel_yielded"]:
            first_yield = row.copy()
        mphi.append(row)

    ag = section["b"] * section["h"]
    ast = len(section["rebar_xy"]) * section["bar_area_m2"]
    bar_area_mm2 = section["bar_area_m2"] * 1_000_000.0
    ast_mm2 = ast * 1_000_000.0
    po = 0.85 * section["fc"] * (ag - ast) + section["fy"] * ast
    pm_targets = [0.0, 0.15 * po, 0.30 * po, 0.60 * po, po]
    pm = []
    for target in pm_targets:
        best = None
        for phi in phis:
            eps0 = solve_eps0_for_p(section, phi, target)
            p, m, _, _ = section_response(section, eps0, phi)
            candidate = {"P_kN": p, "M_kN_m": abs(m), "phi_1_m": phi, "P_objetivo_kN": target}
            if best is None or candidate["M_kN_m"] > best["M_kN_m"]:
                best = candidate
        pm.append(best)

    return {
        "section": {
            "id": "COL70/70_FIBER",
            "opensees_section_tag": opensees_section["section_tag"],
            "b_m": section["b"],
            "h_m": section["h"],
            "b_mm": section["b"] * 1000.0,
            "h_mm": section["h"] * 1000.0,
            "recubrimiento_m": section["cover"],
            "recubrimiento_mm": section["cover"] * 1000.0,
            "Ag_m2": ag,
            "Ag_mm2": ag * 1_000_000.0,
            "Ast_m2": ast,
            "Ast_mm2": ast_mm2,
            "cuantia_refuerzo": ast / ag,
            "fc_MPa": section["fc"] / 1000.0,
            "fy_MPa": section["fy"] / 1000.0,
            "Es_MPa": section["Es"] / 1000.0,
            "concrete_fibers": 400,
            "steel_bars": len(section["rebar_xy"]),
            "bar_area_m2": section["bar_area_m2"],
            "bar_area_mm2": bar_area_mm2,
            "bar_diameter_mm": bar_diameter_mm(section["bar_area_m2"]),
            "steel_yield_strain": steel_yield_strain,
            "Po_kN": po,
        },
        "opensees_definition": opensees_section,
        "reinforcement_coordinates_xy_m": [{"x": x, "y": y} for x, y in section["rebar_xy"]],
        "reinforcement_coordinates_xy_mm": [{"x": x * 1000.0, "y": y * 1000.0} for x, y in section["rebar_xy"]],
        "m_phi": mphi,
        "m_phi_first_steel_yield": first_yield,
        "p_m_points": pm,
        "interpretacion": "La curva M-phi se extendio hasta curvaturas altas para pasar el tramo elastico y capturar la fluencia del acero. Luego el momento tiende a estabilizarse porque el acero queda plastificado y el hormigon comprimido controla la respuesta.",
    }


def plot_capacity(capacity):
    if plt is None:
        raise RuntimeError("Falta instalar matplotlib para generar los graficos de capacidad HA.")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    section = make_column_fibers()
    concrete = [fiber for fiber in section["fibers"] if fiber["type"] == "concrete"]
    steel = [fiber for fiber in section["fibers"] if fiber["type"] == "steel"]

    plt.figure(figsize=(5, 5))
    plt.scatter([f["x"] for f in concrete], [f["y"] for f in concrete], s=8, c="#8fb3ff", label="Hormigon")
    plt.scatter([f["x"] for f in steel], [f["y"] for f in steel], s=70, c="#cc3333", label="Acero")
    plt.axis("equal")
    plt.xlabel("x [m]")
    plt.ylabel("y [m]")
    plt.title("Discretizacion Fiber COL70/70")
    plt.legend()
    plt.grid(True, alpha=0.3)
    plt.tight_layout()
    plt.savefig(OUTPUT_PATH.parent / "fiber_COL70_70.png", dpi=160)
    plt.close()

    plt.figure(figsize=(7, 4))
    plt.plot([p["phi_1_m"] for p in capacity["m_phi"]], [p["M_kN_m"] for p in capacity["m_phi"]], "b-")
    first_yield = capacity.get("m_phi_first_steel_yield")
    if first_yield:
        plt.plot(first_yield["phi_1_m"], first_yield["M_kN_m"], "ro", label="Primera fluencia acero")
        plt.axvline(first_yield["phi_1_m"], color="r", linestyle="--", linewidth=0.9, alpha=0.7)
    plt.xlabel("Curvatura phi [1/m]")
    plt.ylabel("Momento [kN m]")
    plt.title("M-phi COL70/70 fiber completa (P=0 aprox.)")
    if first_yield:
        plt.legend()
    plt.grid(True, alpha=0.3)
    plt.tight_layout()
    plt.savefig(OUTPUT_PATH.parent / "M_phi_COL70_70.png", dpi=160)
    plt.close()

    plt.figure(figsize=(5, 5))
    plt.plot([p["M_kN_m"] for p in capacity["p_m_points"]], [p["P_kN"] for p in capacity["p_m_points"]], "ro-")
    plt.xlabel("M [kN m]")
    plt.ylabel("P [kN]")
    plt.title("Primeros puntos P-M COL70/70 fiber")
    plt.grid(True, alpha=0.3)
    plt.tight_layout()
    plt.savefig(OUTPUT_PATH.parent / "P_M_COL70_70.png", dpi=160)
    plt.close()


def export_capacity_for_unity(capacity):
    section = capacity["section"]
    unity_data = {
        "capacityTitle": "Parte D - Capacidad HA COL70/70_FIBER",
        "sectionId": section["id"],
        "b_m": section["b_m"],
        "h_m": section["h_m"],
        "fc_MPa": section["fc_MPa"],
        "fy_MPa": section["fy_MPa"],
        "steelBars": section["steel_bars"],
        "barArea_m2": section["bar_area_m2"],
        "barArea_mm2": section["bar_area_mm2"],
        "barDiameter_mm": section["bar_diameter_mm"],
        "Ast_m2": section["Ast_m2"],
        "Ast_mm2": section["Ast_mm2"],
        "rho_percent": 100.0 * section["cuantia_refuerzo"],
        "Po_kN": section["Po_kN"],
        "interpretation": capacity["interpretacion"],
        "pmPoints": [
            {
                "label": f"Punto {index}",
                "P_kN": point["P_kN"],
                "M_kN_m": point["M_kN_m"],
                "phi_1_m": point["phi_1_m"],
            }
            for index, point in enumerate(capacity["p_m_points"], start=1)
        ],
    }
    write_json(UNITY_RESULTS_PATH, unity_data)


def print_capacity(capacity):
    section = capacity["section"]
    print("\nParte D - Capacidad HA")
    print(f"Seccion Fiber: {section['id']}")
    print("\nDiscretizacion")
    print(f"  b x h                     = {section['b_m']:.2f} x {section['h_m']:.2f} m")
    print(f"  b x h                     = {section['b_mm']:.0f} x {section['h_mm']:.0f} mm")
    print(f"  Fibras de hormigon        = {section['concrete_fibers']} (20 x 20)")
    print(f"  Barras de acero           = {section['steel_bars']}")
    print(f"  Recubrimiento             = {section['recubrimiento_m']:.3f} m")
    print(f"  Recubrimiento             = {section['recubrimiento_mm']:.0f} mm")
    print("\nMateriales")
    print(f"  Hormigon Concrete01       = fc' {section['fc_MPa']:.1f} MPa")
    print(f"  Acero Steel01             = fy {section['fy_MPa']:.1f} MPa, Es {section['Es_MPa']:.0f} MPa")
    print("\nRefuerzo")
    print(f"  Area por barra            = {section['bar_area_m2']:.6f} m2")
    print(f"  Area por barra            = {section['bar_area_mm2']:.1f} mm2")
    print(f"  Diametro equivalente barra = {section['bar_diameter_mm']:.1f} mm")
    print(f"  Ast total                 = {section['Ast_m2']:.6f} m2")
    print(f"  Ast total                 = {section['Ast_mm2']:.1f} mm2")
    print(f"  Ag bruta                  = {section['Ag_mm2']:.1f} mm2")
    print(f"  Cuantia                   = {100.0 * section['cuantia_refuerzo']:.3f} %")
    print(f"  Po aproximado             = {section['Po_kN']:.3f} kN")
    print("\nPrimeros puntos curva P-M")
    for index, point in enumerate(capacity["p_m_points"], start=1):
        print(f"  Punto {index}: P = {point['P_kN']:.3f} kN, M = {point['M_kN_m']:.3f} kN*m, phi = {point['phi_1_m']:.6f} 1/m")
    print("\nInterpretacion")
    print(f"  {capacity['interpretacion']}")
    first_yield = capacity.get("m_phi_first_steel_yield")
    if first_yield:
        print("\nCurva momento-curvatura M-phi")
        print(f"  Curvatura maxima analizada       = {capacity['m_phi'][-1]['phi_1_m']:.6f} 1/m")
        print(f"  Deformacion de fluencia acero    = {section['steel_yield_strain']:.6f}")
        print(f"  Primera fluencia del acero       = phi {first_yield['phi_1_m']:.6f} 1/m, M {first_yield['M_kN_m']:.3f} kN*m")
    print("\nGraficos generados")
    print(f"  {OUTPUT_PATH.parent / 'fiber_COL70_70.png'}")
    print(f"  {OUTPUT_PATH.parent / 'M_phi_COL70_70.png'}")
    print(f"  {OUTPUT_PATH.parent / 'P_M_COL70_70.png'}")


def ask_float(prompt, default):
    value = input(f"{prompt} [{default}]: ").strip()
    if not value:
        return default
    return float(value.replace(",", "."))


def ask_lambdas():
    print("\nCoeficientes para R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY")
    return {
        "G": ask_float("lambda_G", DEFAULT_LAMBDAS["G"]),
        "Q": ask_float("lambda_Q", DEFAULT_LAMBDAS["Q"]),
        "EX": ask_float("lambda_EX", DEFAULT_LAMBDAS["EX"]),
        "EY": ask_float("lambda_EY", DEFAULT_LAMBDAS["EY"]),
    }


def interactive_menu():
    data = load_json(JSON_PATH)
    sc_kg_m2 = ask_float("Sobrecarga SC en kg/m2", 500.0)
    seismic_coeff = ask_float("Coeficiente sismico pseudoestatico", DEFAULT_SEISMIC_COEFF)
    q_q = kg_m2_to_kn_m2(sc_kg_m2)
    live_transfer, seismic, _ = build_result(data, q_q, seismic_coeff)

    while True:
        print("\nSemana 3 - Que resultado quieres ver?")
        print("1. Resumen global Q + sismo              (sin ID)")
        print("2. Area tributaria y Q de una viga       (con ID de viga)")
        print("3. Area y Q superficial de una losa      (con ID de losa)")
        print("4. Sismo EX/EY por piso                  (sin ID)")
        print("5. Superposicion R y verificacion        (sin ID, pide lambdas)")
        print("6. Capacidad HA Fiber Section            (sin ID)")
        print("7. Ejemplos de IDs disponibles           (sin ID)")
        print("8. Ruta del JSON completo de resultados  (sin ID)")
        print("0. Salir")
        option = input("Elige una opcion: ").strip()

        if option == "0":
            return
        if option == "1":
            print_global_summary(live_transfer, seismic)
        elif option == "2":
            wanted_id = input("ID de la viga, ej. B3002_V60/80 o 359: ").strip()
            print(json.dumps(query_beam(live_transfer, wanted_id) or query_item(data, live_transfer, q_q, wanted_id), indent=2, ensure_ascii=False))
        elif option == "3":
            wanted_id = input("ID de la losa, ej. L1, L2 o D101: ").strip()
            print(json.dumps(query_slab(data, q_q, wanted_id) or query_item(data, live_transfer, q_q, wanted_id), indent=2, ensure_ascii=False))
        elif option == "4":
            print_seismic_by_floor(seismic)
        elif option == "5":
            lambdas = ask_lambdas()
            superposition = superposition_check(data, live_transfer, seismic, lambdas)
            with open(OUTPUT_PATH, encoding="utf-8") as file:
                result = json.load(file)
            result["parte_C_superposicion"] = superposition
            write_json(OUTPUT_PATH, result)
            print_superposition(superposition)
        elif option == "6":
            capacity = fiber_section_capacity()
            plot_capacity(capacity)
            export_capacity_for_unity(capacity)
            with open(OUTPUT_PATH, encoding="utf-8") as file:
                result = json.load(file)
            result["parte_D_capacidad_HA"] = capacity
            write_json(OUTPUT_PATH, result)
            print_capacity(capacity)
        elif option == "7":
            print_id_examples(data, live_transfer)
        elif option == "8":
            print(f"\nJSON completo: {OUTPUT_PATH}")
        else:
            print("Opcion no valida.")


def main():
    parser = argparse.ArgumentParser(description="Carga viva Q y sismo pseudoestatico del edificio completo")
    parser.add_argument("--qQ", type=float, default=DEFAULT_Q_Q, help="Carga viva superficial en kN/m2")
    parser.add_argument("--sc-kg-m2", type=float, help="Sobrecarga en kg/m2; reemplaza --qQ")
    parser.add_argument("--coef-sismo", type=float, default=DEFAULT_SEISMIC_COEFF, help="Coeficiente sismico pseudoestatico")
    parser.add_argument("--id", help="ID de viga o losa para consulta puntual")
    parser.add_argument("--menu", action="store_true", help="Abre un menu interactivo de resultados")
    parser.add_argument("--superposicion", action="store_true", help="Corre Parte C: superposicion y comparacion OpenSees explicita")
    parser.add_argument("--lambdaG", type=float, default=DEFAULT_LAMBDAS["G"], help="Factor lambda_G")
    parser.add_argument("--lambdaQ", type=float, default=DEFAULT_LAMBDAS["Q"], help="Factor lambda_Q")
    parser.add_argument("--lambdaEX", type=float, default=DEFAULT_LAMBDAS["EX"], help="Factor lambda_EX")
    parser.add_argument("--lambdaEY", type=float, default=DEFAULT_LAMBDAS["EY"], help="Factor lambda_EY")
    parser.add_argument("--capacidad-ha", action="store_true", help="Corre Parte D: Fiber Section HA, M-phi y puntos P-M")
    args = parser.parse_args()

    if args.menu or len(sys.argv) == 1:
        interactive_menu()
        return

    q_q = kg_m2_to_kn_m2(args.sc_kg_m2) if args.sc_kg_m2 is not None else args.qQ
    data = load_json(JSON_PATH)
    live_transfer, seismic, _ = build_result(data, q_q, args.coef_sismo)

    if args.superposicion:
        lambdas = {"G": args.lambdaG, "Q": args.lambdaQ, "EX": args.lambdaEX, "EY": args.lambdaEY}
        superposition = superposition_check(data, live_transfer, seismic, lambdas)
        with open(OUTPUT_PATH, encoding="utf-8") as file:
            result = json.load(file)
        result["parte_C_superposicion"] = superposition
        write_json(OUTPUT_PATH, result)
        print_superposition(superposition)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.capacidad_ha:
        capacity = fiber_section_capacity()
        plot_capacity(capacity)
        export_capacity_for_unity(capacity)
        with open(OUTPUT_PATH, encoding="utf-8") as file:
            result = json.load(file)
        result["parte_D_capacidad_HA"] = capacity
        write_json(OUTPUT_PATH, result)
        print_capacity(capacity)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.id:
        print(json.dumps(query_item(data, live_transfer, q_q, args.id), indent=2, ensure_ascii=False))
        print(f"salida = {OUTPUT_PATH}")
        return

    print("Carga viva y sismo pseudoestatico generados")
    print_global_summary(live_transfer, seismic)


if __name__ == "__main__":
    main()
