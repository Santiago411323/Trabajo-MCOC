#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Carga viva Q y sismo pseudoestatico EX/EY para el edificio completo.

Usa la geometria tributaria ya generada para el edificio completo, pero guarda
los resultados dentro de la carpeta Semana 3 para mantener trazabilidad semanal.

Ejemplos:
  python "P1L3/carga_viva_sismo.py" --sc-kg-m2 500
  python "P1L3/carga_viva_sismo.py" --sc-kg-m2 500 --id B3002_V60/80
  python "P1L3/carga_viva_sismo.py" --sc-kg-m2 500 --id L1
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
# Torsion accidental NCh433 (metodo estatico): momento en cada piso
# M_k = F_k * e_k, e_k = 0.10 * b_k * Z_k / H, con b_k la dimension de la
# planta perpendicular al sismo. Se aplica con signo + dentro de EX y EY
# (las combinaciones C1-C3 cambian el signo junto con el sismo).
TORSION_ACCIDENTAL = True
TORSION_ACCIDENTAL_FACTOR = 0.10
DEFAULT_SEISMIC_COEFF = 0.20
FLOOR_GROUP_TOL_M = 0.25
G_ACCEL = 9.80665
E_CONCRETE = 25_000_000.0
NU_CONCRETE = 0.20
G_CONCRETE = E_CONCRETE / (2.0 * (1.0 + NU_CONCRETE))
DEFAULT_LAMBDAS = {"G": 1.0, "Q": 0.5, "EX": 1.0, "EY": 0.3}

OUT_DIR = BASE_DIR / "resultados"

# Combinaciones sismicas NCh433 (Parte C): C1/C2/C3 con +-0.30 EX y +-0.20 EY.
COMBINACIONES_NCH433 = {
    "C1": {"G": 1.00, "Q": 0.50, "EX": 0.30, "EY": 0.20},
    "C2": {"G": 1.00, "Q": 0.50, "EX": 0.30, "EY": -0.20},
    "C3": {"G": 1.00, "Q": 0.50, "EX": -0.30, "EY": 0.20},
}
MALLAS_SENSIBILIDAD = [10, 20, 40]

# Parametros del analisis de fibra H-30 (columna 70x70 y muro).
# Corresponden a part_d_fiber / part_e_wall_pm de P1L2 (mismos numeros del informe).
_FIB_FC = 30000.0          # f'c concreto [kN/m2] (30 MPa)
_FIB_EPS_C0 = 0.0020
_FIB_EPS_CU = 0.0035
_FIB_FY = 420000.0         # fy acero [kN/m2] (420 MPa)
_FIB_ES = 2.0e8            # Es acero [kN/m2] (200 GPa)
_FIB_EH = 0.01             # endurecimiento (Steel01)
_FIB_COVER = 0.0525        # centro de barra al borde (columna 70x70)
_B = 0.70                  # lado de la columna [m]
_WALL_T = 0.25             # espesor del muro [m]
_WALL_L = 7.60             # longitud en el plano [m]
_WALL_COVER = 0.030        # recubrimiento al centro de la barra [m]
_WALL_DBAR = 0.012         # diametro de barra vertical [m]
_WALL_S = 0.20             # espaciamiento @200 mm por capa

# Configuracion simple de la armadura de la columna COL70/70.
# Cambia estas lineas para modificar diametro, barras y fibras de hormigon.
BAR_DIAMETER_MM = 25.0
REBAR_BARS_INFERIOR = 3
REBAR_BARS_CENTRO = 2
REBAR_BARS_SUPERIOR = 3
CONCRETE_FIBERS_X = 20
CONCRETE_FIBERS_Y = 20


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


def torsion_constant_rect(width, height):
    """Constante de torsion de Saint-Venant de un rectangulo (Roark):
    J = a b^3 [1/3 - 0.21 (b/a)(1 - b^4/(12 a^4))], a >= b.
    (Antes se usaba J = Iy + Iz, el momento polar, que sobreestima la rigidez
    torsional de una viga 30x80 unas 2.5 veces.)"""
    a, b = max(width, height), min(width, height)
    return a * b**3 * (1.0 / 3.0 - 0.21 * (b / a) * (1.0 - b**4 / (12.0 * a**4)))


def section_properties(width, height):
    area = width * height
    iy = width * height**3 / 12.0
    iz = height * width**3 / 12.0
    j = torsion_constant_rect(width, height)
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


# ==================================================================
# CORRECCION DE CONECTIVIDAD (vigas continuas que no se cortan)
# ==================================================================
# El JSON base trae vigas que pasan "de largo" por nodos donde llega otra
# viga transversal (uniones en T) y pares de vigas que se cruzan en planta al
# mismo nivel sin nodo comun (uniones en X). En OpenSees dos barras solo
# comparten grados de libertad si comparten un nodo, asi que esas vigas
# transversales quedaban desconectadas (P1L3 les ponia un empotramiento
# automatico) o con un extremo libre. Aqui se parten las barras en esos puntos.
TOL_CONECTIVIDAD_M = 0.02
# Solo se parte con nodos del mismo edificio: edificio_1 y edificio_2 se
# mantienen como estructuras independientes (niveles distintos en x=-10).
CONECTIVIDAD_MISMO_EDIFICIO = True
_CAMPOS_ESCALABLES = ("deadLoad", "liveLoad", "areaTributaria", "cargaTributaria",
                      "factoredLoad14D", "factoredLoad12D16L")
_CAMPOS_LEGADO_POR_BARRA = ("axialI", "axialJ", "shearI", "shearJ", "momentI", "momentJ")


def _seg_param(p, a, b, tol):
    """Parametro t en (0,L) si p cae dentro del segmento a-b (distancia < tol)."""
    d = [b[k] - a[k] for k in range(3)]
    length = math.sqrt(sum(v * v for v in d))
    if length <= 2.0 * tol:
        return None
    u = [v / length for v in d]
    w = [p[k] - a[k] for k in range(3)]
    t = sum(w[k] * u[k] for k in range(3))
    if t <= tol or t >= length - tol:
        return None
    perp = math.sqrt(sum((w[k] - t * u[k]) ** 2 for k in range(3)))
    return t if perp < tol else None


def corregir_conectividad(data, tol=TOL_CONECTIVIDAD_M):
    """Devuelve (data_corregida, reporte). No modifica el dict original.

    1) Cruces en X viga-viga del mismo nivel y edificio: crea un nodo en la
       interseccion (o reutiliza uno existente a menos de tol).
    2) Toda barra (viga o columna) que contenga en su interior un nodo usado
       por otra barra del mismo edificio se parte en ese nodo.
    Los tramos conservan seccion y propiedades; las cargas totales (D, L,
    area tributaria) se reparten proporcionalmente a la longitud, por lo que
    la carga total se conserva. El primer tramo conserva el id original.
    """
    import copy

    data = copy.deepcopy(data)
    levels_snap = alinear_niveles_edificio2(data) if ALINEAR_NIVELES_E2 else {}
    duplicates = eliminar_columnas_duplicadas(data, tol)
    nodes = node_map(data)
    xyz = {nid: (float(n["x"]), float(n["y"]), float(n["z"])) for nid, n in nodes.items()}
    elements = [e for e in data.get("elements", []) if e.get("nodeI") in nodes and e.get("nodeJ") in nodes]
    building_of_node = {}
    for e in elements:
        for key in ("nodeI", "nodeJ"):
            building_of_node.setdefault(e[key], set()).add(e.get("sourceBuilding"))
    next_node = max(nodes) + 1
    new_nodes = []

    def near_node(p, building):
        for nid, q in xyz.items():
            if nid not in building_of_node:
                continue
            if CONECTIVIDAD_MISMO_EDIFICIO and building not in building_of_node[nid]:
                continue
            if math.dist(p, q) < tol:
                return nid
        return None

    # 1) cruces en X
    beams = [e for e in elements if e.get("type") == "viga"]
    crossings = []
    for i, b1 in enumerate(beams):
        a1, c1 = xyz[b1["nodeI"]], xyz[b1["nodeJ"]]
        if abs(a1[2] - c1[2]) > tol:
            continue
        for b2 in beams[i + 1:]:
            if CONECTIVIDAD_MISMO_EDIFICIO and b1.get("sourceBuilding") != b2.get("sourceBuilding"):
                continue
            a2, c2 = xyz[b2["nodeI"]], xyz[b2["nodeJ"]]
            if abs(a2[2] - c2[2]) > tol or abs(a1[2] - a2[2]) > tol:
                continue
            d1 = (c1[0] - a1[0], c1[1] - a1[1])
            d2 = (c2[0] - a2[0], c2[1] - a2[1])
            den = d1[0] * d2[1] - d1[1] * d2[0]
            if abs(den) < 1e-9:
                continue
            r = (a2[0] - a1[0], a2[1] - a1[1])
            t = (r[0] * d2[1] - r[1] * d2[0]) / den
            s = (r[0] * d1[1] - r[1] * d1[0]) / den
            l1 = math.hypot(*d1)
            l2 = math.hypot(*d2)
            if not (tol / l1 < t < 1 - tol / l1 and tol / l2 < s < 1 - tol / l2):
                continue
            p = (a1[0] + t * d1[0], a1[1] + t * d1[1], 0.5 * (a1[2] + a2[2]))
            building = b1.get("sourceBuilding")
            nid = near_node(p, building)
            if nid is None:
                nid = next_node
                next_node += 1
                xyz[nid] = p
                node = {"id": nid, "x": round(p[0], 6), "y": round(p[1], 6), "z": round(p[2], 6),
                        "origen": "cruce_vigas"}
                data["nodes"].append(node)
                new_nodes.append(node)
            building_of_node.setdefault(nid, set()).add(building)
            crossings.append({"vigas": [element_tag(b1), element_tag(b2)], "nodo": nid,
                              "x": round(p[0], 3), "y": round(p[1], 3), "z": round(p[2], 3)})

    # 2) partir barras en nodos interiores del mismo edificio
    used = set(building_of_node)
    splits = []

    def auto_interior(e):
        a, b = xyz[e["nodeI"]], xyz[e["nodeJ"]]
        building = e.get("sourceBuilding")
        found = []
        for nid in used:
            if nid in (e["nodeI"], e["nodeJ"]):
                continue
            if CONECTIVIDAD_MISMO_EDIFICIO and building not in building_of_node.get(nid, ()):
                continue
            if _seg_param(xyz[nid], a, b, tol) is not None:
                found.append(nid)
        return found

    _split_elements(data, xyz, auto_interior, splits)

    # 3) union entre edificios en x=-10 (extremos libres que apoyan en otro edificio)
    junction = _unir_extremos_libres(data, xyz, tol, splits)

    report = {
        "tolerancia_m": tol,
        "solo_mismo_edificio": CONECTIVIDAD_MISMO_EDIFICIO,
        "nodos_nuevos_en_cruces": len(new_nodes),
        "cruces_X": crossings,
        "barras_partidas": len(splits),
        "barras_resultantes": len(data["elements"]),
        "union_entre_edificios": junction,
        "columnas_duplicadas_eliminadas": duplicates,
        "niveles_edificio2_alineados": levels_snap,
        "detalle": splits,
    }
    data["connectivityFix"] = {k: v for k, v in report.items() if k != "detalle"}
    return data, report



# El edificio 2 real tiene pisos de 3.96 m (4.16 m el subterraneo) y el
# edificio 1 parametrico de 4.00 m; la union alinea base (z=-4) y techo (z=16)
# y deja los pisos intermedios desfasados 0.16/0.12/0.08/0.04 m. Con dos losas
# a 4-16 cm una de otra unidas por barras muy cortas, el modelo transmitia
# cortes artificiales enormes por esas barras (>16 000 kN) y OpenSees no admite
# un diafragma con nodos a distinta cota. Se alinean los niveles del edificio 2
# con los del edificio 1 (lo mismo que ya hace el visor Unity al dibujar).
ALINEAR_NIVELES_E2 = True
ALINEAR_NIVELES_TOL_M = 0.25


def alinear_niveles_edificio2(data):
    nodes = node_map(data)
    e1_levels = sorted({round(nodes[e[k]]["z"], 6) for e in data.get("elements", [])
                        if e.get("sourceBuilding") == "edificio_1" for k in ("nodeI", "nodeJ") if e.get(k) in nodes})
    e2_nodes = {e[k] for e in data.get("elements", []) if e.get("sourceBuilding") == "edificio_2"
                for k in ("nodeI", "nodeJ")}
    e2_nodes |= {w[k] for w in data.get("walls", []) if w.get("sourceBuilding") == "edificio_2" for k in ("nodeI", "nodeJ")}
    moved = {}
    for nid in e2_nodes:
        node = nodes.get(nid)
        if node is None:
            continue
        z = float(node["z"])
        target = min(e1_levels, key=lambda lv: abs(lv - z))
        if 1e-9 < abs(target - z) <= ALINEAR_NIVELES_TOL_M:
            moved[str(round(z, 3))] = target
            node["z"] = target
    # nodos sueltos del edificio 2 (esquinas de paneles de muro/losa) a esas mismas cotas
    for node in nodes.values():
        key = str(round(float(node["z"]), 3))
        if key in moved:
            node["z"] = moved[key]
    for slab in data.get("slabs", []):
        z = float(slab.get("z", 0.0))
        target = min(e1_levels, key=lambda lv: abs(lv - z))
        if 1e-9 < abs(target - z) <= ALINEAR_NIVELES_TOL_M:
            slab["z"] = target
    return dict(sorted(moved.items(), key=lambda kv: float(kv[0])))


def eliminar_columnas_duplicadas(data, tol=TOL_CONECTIVIDAD_M):
    """El script de union (P1L2/scripts/unificar_edificios.py) pega la columna
    derecha del edificio 2 exactamente sobre el eje x=-10 del edificio 1. En
    (-10, -7.25) ambos edificios tienen su propia pila de columnas en el mismo
    lugar: es una sola columna fisica contada dos veces. Se conserva la del
    edificio 1 y se eliminan las columnas del edificio 2 que se superponen
    (misma planta, rango de z superpuesto). Las vigas del edificio 2 que
    llegaban a ellas se unen luego a la columna del edificio 1."""
    nodes = node_map(data)
    cols = [e for e in data.get("elements", []) if e.get("type") == "columna"
            and e.get("nodeI") in nodes and e.get("nodeJ") in nodes]

    def plan_z(e):
        a, b = nodes[e["nodeI"]], nodes[e["nodeJ"]]
        return (a["x"], a["y"]), (min(a["z"], b["z"]), max(a["z"], b["z"]))

    e1 = [(plan_z(c), c) for c in cols if c.get("sourceBuilding") == "edificio_1"]
    removed = []
    for c in cols:
        if c.get("sourceBuilding") != "edificio_2":
            continue
        (p, (z0, z1)) = plan_z(c)
        for (q, (w0, w1)), _ in e1:
            if math.dist(p, q) < tol and min(z1, w1) - max(z0, w0) > tol:
                removed.append(c)
                break
    removed_ids = {c["id"] for c in removed}
    data["elements"] = [e for e in data["elements"] if e["id"] not in removed_ids]
    used = {e[k] for e in data["elements"] for k in ("nodeI", "nodeJ")}
    dropped_supports = [s["node"] for s in data.get("supports", []) if s["node"] not in used]
    data["supports"] = [s for s in data.get("supports", []) if s["node"] in used]
    return {"columnas": [element_tag(c) for c in removed], "apoyos_quitados": dropped_supports}


def _split_elements(data, xyz, interior_fn, splits):
    """Parte cada barra en los nodos que devuelve interior_fn(e)."""
    next_elem = max(e["id"] for e in data.get("elements", [])) + 1
    out_elements = []
    for e in data.get("elements", []):
        if e.get("nodeI") not in xyz or e.get("nodeJ") not in xyz:
            out_elements.append(e)
            continue
        a, b = xyz[e["nodeI"]], xyz[e["nodeJ"]]
        interior = []
        for nid in set(interior_fn(e)):
            t = _seg_param(xyz[nid], a, b, TOL_CONECTIVIDAD_M)
            if t is not None:
                interior.append((t, nid))
        if not interior:
            out_elements.append(e)
            continue
        interior.sort()
        chain = [e["nodeI"]] + [nid for _, nid in interior] + [e["nodeJ"]]
        total_len = math.dist(a, b)
        parent_tag = e.get("parentTag") or element_tag(e)
        parent_id = e.get("parentId", e["id"])
        tag = element_tag(e)
        pieces = []
        for k in range(len(chain) - 1):
            seg = dict(e)
            seg_len = math.dist(xyz[chain[k]], xyz[chain[k + 1]])
            frac = seg_len / total_len
            seg["id"] = e["id"] if k == 0 else next_elem
            if k > 0:
                next_elem += 1
            seg["nodeI"], seg["nodeJ"] = chain[k], chain[k + 1]
            seg["elementTag"] = f"{tag}.{k + 1}"
            seg["parentId"] = parent_id
            seg["parentTag"] = parent_tag
            for field in _CAMPOS_ESCALABLES:
                if field in seg and seg[field] is not None:
                    seg[field] = float(seg[field]) * frac
            for field in _CAMPOS_LEGADO_POR_BARRA:
                seg.pop(field, None)
            out_elements.append(seg)
            pieces.append({"id": seg["id"], "tag": seg["elementTag"], "nodeI": seg["nodeI"],
                           "nodeJ": seg["nodeJ"], "L_m": round(seg_len, 4)})
        splits.append({"original": tag, "id_original": e["id"], "tipo": e.get("type"),
                       "edificio": e.get("sourceBuilding"), "tramos": pieces})
    data["elements"] = out_elements


# Desnivel maximo que se cubre con un enlace rigido entre edificios
# (edificio_2 esta 0.16/0.12/0.08/0.04 m sobre los niveles de edificio_1).
UNION_DESNIVEL_MAX_M = 0.25
ENLACE_RIGIDO_FACTOR = 1.0  # seccion 70x70 de 0.04-0.16 m: ya es rigida frente a las vigas


def _unir_extremos_libres(data, xyz, tol, splits):
    """Conecta extremos libres de vigas (grado 1, sin apoyo) con la barra de
    OTRO edificio sobre la que descansan:
      a) nodo de otro edificio en el mismo punto -> se reutiliza ese nodo;
      b) punto interior de una barra de otro edificio -> se parte esa barra;
      c) barra horizontal de otro edificio a <= UNION_DESNIVEL_MAX_M en la
         vertical -> nodo nuevo en esa barra + enlace rigido vertical.
    """
    degree = {}
    building_of_node = {}
    for e in data["elements"]:
        for key in ("nodeI", "nodeJ"):
            degree[e[key]] = degree.get(e[key], 0) + 1
            building_of_node.setdefault(e[key], set()).add(e.get("sourceBuilding"))
    supports = {s.get("node") for s in data.get("supports", [])}
    free = sorted(n for n, d in degree.items() if d == 1 and n not in supports)
    next_node = max(n["id"] for n in data["nodes"]) + 1
    next_elem = max(e["id"] for e in data["elements"]) + 1
    forced = {}
    remap = {}
    links = []
    actions = []
    unresolved = []
    for nid in free:
        p = xyz[nid]
        own = building_of_node[nid]
        owner = next(e for e in data["elements"] if nid in (e["nodeI"], e["nodeJ"]))
        if owner.get("type") != "viga":
            unresolved.append({"nodo": nid, "motivo": "extremo libre que no es de viga"})
            continue
        # a) nodo coincidente de otro edificio
        target = None
        for other, q in xyz.items():
            if other == nid or other not in building_of_node or building_of_node[other] & own:
                continue
            if math.dist(p, q) < tol:
                target = other
                break
        if target is not None:
            remap[nid] = target
            actions.append({"nodo": nid, "accion": "fusion con nodo de otro edificio", "nodo_destino": target,
                            "viga": element_tag(owner)})
            continue
        # b) interior de barra de otro edificio
        host = None
        for e in data["elements"]:
            if e.get("sourceBuilding") in own:
                continue
            if _seg_param(p, xyz[e["nodeI"]], xyz[e["nodeJ"]], tol) is not None:
                host = e
                break
        if host is not None:
            forced.setdefault(host["id"], set()).add(nid)
            actions.append({"nodo": nid, "accion": "barra de otro edificio partida en el nodo",
                            "barra": element_tag(host), "viga": element_tag(owner)})
            continue
        # c) viga horizontal de otro edificio justo arriba/abajo
        best = None
        for e in data["elements"]:
            if e.get("sourceBuilding") in own or e.get("type") != "viga":
                continue
            a, b = xyz[e["nodeI"]], xyz[e["nodeJ"]]
            if abs(a[2] - b[2]) > tol:
                continue
            dz = a[2] - p[2]
            if abs(dz) > UNION_DESNIVEL_MAX_M or abs(dz) < tol:
                continue
            q = (p[0], p[1], a[2])
            if math.dist(q, a) < tol:
                cand = ("nodo", e["nodeI"], q, e)
            elif math.dist(q, b) < tol:
                cand = ("nodo", e["nodeJ"], q, e)
            elif _seg_param(q, a, b, tol) is not None:
                cand = ("interior", None, q, e)
            else:
                continue
            if best is None or abs(dz) < abs(best[2][2] - p[2]):
                best = cand
        if best is None:
            unresolved.append({"nodo": nid, "viga": element_tag(owner), "xyz": [round(v, 3) for v in p]})
            continue
        kind, host_node, q, host = best
        if kind == "interior":
            host_node = next_node
            next_node += 1
            xyz[host_node] = q
            data["nodes"].append({"id": host_node, "x": round(q[0], 6), "y": round(q[1], 6), "z": round(q[2], 6),
                                  "origen": "union_edificios"})
            forced.setdefault(host["id"], set()).add(host_node)
        link = {
            "id": next_elem, "type": "enlace", "nodeI": host_node, "nodeJ": nid,
            "sectionId": "ENLACE_RIGIDO", "width_m": 0.70, "height_m": 0.70,
            "stiffnessFactor": ENLACE_RIGIDO_FACTOR,
            "sourceBuilding": owner.get("sourceBuilding"), "sourceId": f"LINK_{nid}",
            "elementTag": f"LINK_{element_tag(owner)}", "piso": owner.get("piso", ""),
            "deadLoad": 0.0, "liveLoad": 0.0, "areaTributaria": 0.0,
        }
        next_elem += 1
        data["elements"].append(link)
        links.append(link["elementTag"])
        actions.append({"nodo": nid, "accion": "enlace rigido vertical", "desnivel_m": round(p[2] - q[2], 3),
                        "barra": element_tag(host), "viga": element_tag(owner), "enlace": link["elementTag"]})
    if remap:
        for e in data["elements"]:
            for key in ("nodeI", "nodeJ"):
                if e[key] in remap:
                    e[key] = remap[e[key]]
    if forced:
        _split_elements(data, xyz, lambda e: forced.get(e["id"], ()), splits)
    return {"extremos_libres_iniciales": len(free), "acciones": actions,
            "enlaces_rigidos": links, "sin_resolver": unresolved}



# ==================================================================
# MUROS ESTRUCTURALES (columna ancha + brazos rigidos)
# ==================================================================
# Los 75 paneles del JSON son muros estructurales (StructuralWall), 6 muros en
# edificio_1 y 9 en edificio_2, todos de Z=-4 a Z=16. Los tabiques no son
# elementos: van como carga muerta de terminaciones. Cada muro se modela como
# una columna ancha (seccion t x L, eje fuerte en el plano del muro) en el
# centro del muro, con brazos rigidos horizontales en cada nivel hacia sus
# extremos y hacia los nodos del portico que caen sobre el muro. La base del
# muro queda empotrada.
MODELAR_MUROS = True
MURO_Z_BASE = -4.0
MURO_Z_TOPE = 16.0
BRAZO_RIGIDO_ALTO_M = 4.0      # brazo = franja de muro de un piso de alto
BRAZO_RIGIDO_FACTOR = 1.0   # franja t x 4 m: ya muy rigida frente a las vigas
PESO_ESPECIFICO_HA_KN_M3 = 25.0
# Peso propio de columnas y muros (las vigas ya reciben q_G por area tributaria;
# el peso propio de vigas no estaba en el modelo original y sigue sin agregarse).
PESO_PROPIO_VERTICALES = True
# Peso propio del alma de las vigas bajo la losa: b*(h - e_losa)*25 kN/m.
# q_G (6.23 kN/m2 = 635 kg/m2) ya incluye la losa de 0.15 m y terminaciones.
PESO_PROPIO_VIGAS = True
LOSA_ESPESOR_M = 0.15


def _wall_panel_range(wall, z_node):
    """Rango [zb, zt] del panel: edificio_1 guarda el nodo en el TOPE del
    panel (bottom FOUNDATION -> nodo z=0); edificio_2 lo guarda en la BASE
    (rotulos E2_Z-8.17..E2_Z-4.01 = z -4.00..0.16 con offset +4.17)."""
    return (z_node, None) if wall.get("sourceBuilding") == "edificio_2" else (None, z_node)


def agregar_muros(data, tol=TOL_CONECTIVIDAD_M):
    """Agrega los muros estructurales como barras. Devuelve (data, reporte)."""
    import copy

    data = copy.deepcopy(data)
    nodes = node_map(data)
    xyz = {nid: (float(n["x"]), float(n["y"]), float(n["z"])) for nid, n in nodes.items()}
    frame_nodes = set()
    for e in data["elements"]:
        frame_nodes.update((e["nodeI"], e["nodeJ"]))
    portico_nodes = set(frame_nodes)
    next_node = max(nodes) + 1
    next_elem = max(e["id"] for e in data["elements"]) + 1

    def node_at(p):
        nonlocal next_node
        for nid in frame_nodes:
            if math.dist(xyz[nid], p) < tol:
                return nid
        nid = next_node
        next_node += 1
        xyz[nid] = p
        node = {"id": nid, "x": round(p[0], 6), "y": round(p[1], 6), "z": round(p[2], 6), "origen": "muro"}
        data["nodes"].append(node)
        nodes[nid] = node
        frame_nodes.add(nid)
        return nid

    # agrupar paneles por muro (misma huella en planta)
    stacks = {}
    for index, wall in enumerate(data.get("walls", [])):
        a, b = xyz[wall["nodeI"]], xyz[wall["nodeJ"]]
        key = tuple(round(v, 3) for v in (a[0], a[1], b[0], b[1]))
        stack = stacks.setdefault(key, {"a": a[:2], "b": b[:2], "t": float(wall["grosor"]),
                                        "building": wall.get("sourceBuilding") or "edificio_1",
                                        "name": (wall.get("sourceId") or f"MURO_E1_{len(stacks) + 1}").rsplit("_L", 1)[0],
                                        "panels": [], "levels": {MURO_Z_BASE, MURO_Z_TOPE}})
        stack["panels"].append((index, a[2]))
        stack["levels"].add(round(a[2], 3))

    report = {"muros": [], "elementos_muro": 0, "brazos": 0}
    wall_elements = {}
    for key, st in stacks.items():
        (ax, ay), (bx, by) = st["a"], st["b"]
        length = math.hypot(bx - ax, by - ay)
        cx, cy = 0.5 * (ax + bx), 0.5 * (ay + by)
        along_x = abs(bx - ax) >= abs(by - ay)
        # niveles de nodos del portico que caen sobre el muro
        on_wall = {}
        for nid in list(frame_nodes):
            px, py, pz = xyz[nid]
            if pz < MURO_Z_BASE - tol or pz > MURO_Z_TOPE + tol:
                continue
            at_end = math.dist((px, py), (ax, ay)) < tol or math.dist((px, py), (bx, by)) < tol
            inside = _seg_param((px, py, 0.0), (ax, ay, 0.0), (bx, by, 0.0), tol) is not None
            if at_end or inside:
                on_wall.setdefault(round(pz, 3), set()).add(nid)
                st["levels"].add(round(pz, 3))
        levels = sorted(st["levels"])
        centers = []
        for z in levels:
            c = node_at((cx, cy, z))
            centers.append(c)
        # columna ancha: eje fuerte en el plano del muro. Para barras verticales
        # (geomTransf vecxz=(1,0,0)) z local = X global, y local = -Y global:
        # height_m es la dimension en X y width_m la dimension en Y.
        width, height = (st["t"], length) if along_x else (length, st["t"])
        elems = []
        for k in range(len(levels) - 1):
            e = {"id": next_elem, "type": "muro_eq", "nodeI": centers[k], "nodeJ": centers[k + 1],
                 "sectionId": f"MURO_{st['t']:.2f}x{length:.2f}", "width_m": width, "height_m": height,
                 "sourceBuilding": st["building"], "sourceId": st["name"], "wallName": st["name"],
                 "elementTag": f"{st['name']}.{k + 1}", "wallLength_m": round(length, 4), "wallThickness_m": st["t"],
                 "wallStrongAxis": "My" if along_x else "Mz", "deadLoad": 0.0, "liveLoad": 0.0, "areaTributaria": 0.0}
            next_elem += 1
            data["elements"].append(e)
            elems.append(e)
        # brazos rigidos en cada nivel (salvo la base, que va empotrada)
        n_arms = 0
        for k, z in enumerate(levels):
            if z <= MURO_Z_BASE + tol:
                continue
            targets = {node_at((ax, ay, z)), node_at((bx, by, z))} | on_wall.get(z, set())
            for target in sorted(targets):
                if target == centers[k]:
                    continue
                data["elements"].append({
                    "id": next_elem, "type": "brazo_rigido", "nodeI": centers[k], "nodeJ": target,
                    "sectionId": "BRAZO_MURO", "width_m": st["t"], "height_m": BRAZO_RIGIDO_ALTO_M,
                    "stiffnessFactor": BRAZO_RIGIDO_FACTOR, "sourceBuilding": st["building"],
                    "sourceId": st["name"], "elementTag": f"BRAZO_{st['name']}_{k}_{target}",
                    "deadLoad": 0.0, "liveLoad": 0.0, "areaTributaria": 0.0})
                next_elem += 1
                n_arms += 1
        data["supports"].append({"node": centers[0], "type": f"empotrado base muro {st['name']}",
                                 "ux": 1, "uy": 1, "uz": 1, "rx": 1, "ry": 1, "rz": 1})
        # panel del JSON -> barras de muro que cubre (para la demanda P-M)
        panel_levels = sorted({round(z, 3) for _, z in st["panels"]})
        for index, z_node in st["panels"]:
            zb, zt = _wall_panel_range(data["walls"][index], z_node)
            if zb is None:
                zb = max([z for z in panel_levels if z < z_node - tol] or [MURO_Z_BASE])
            if zt is None:
                zt = min([z for z in panel_levels if z > z_node + tol] or [MURO_Z_TOPE])
            ids = [e["id"] for e in elems
                   if xyz[e["nodeI"]][2] >= zb - tol and xyz[e["nodeJ"]][2] <= zt + tol]
            data["walls"][index]["analysisElements"] = ids
            data["walls"][index]["panelZ"] = [round(zb, 3), round(zt, 3)]
            wall_elements[index] = ids
        report["muros"].append({"muro": st["name"], "edificio": st["building"], "t_m": st["t"],
                                "L_m": round(length, 3), "niveles": levels, "brazos": n_arms,
                                "nodos_portico_conectados": sum(len(v & portico_nodes) for v in on_wall.values())})
        report["elementos_muro"] += len(elems)
        report["brazos"] += n_arms
    data["wallModel"] = {k: v for k, v in report.items()}
    return data, report


def beam_self_weight_per_m(element):
    if not PESO_PROPIO_VIGAS or element.get("type") != "viga":
        return 0.0
    b = float(element.get("width_m") or 0.6)
    h = float(element.get("height_m") or 0.8)
    return PESO_ESPECIFICO_HA_KN_M3 * b * max(h - LOSA_ESPESOR_M, 0.0)


def seismic_self_weight_nodal(data):
    """Peso propio para la masa sismica: columnas, muros y vigas (mitad a cada extremo)."""
    out = dict(self_weight_nodal(data))
    nodes = node_map(data)
    for e in data.get("elements", []):
        w = beam_self_weight_per_m(e)
        if w <= 0.0:
            continue
        half = 0.5 * w * element_length(e, nodes)
        for key in ("nodeI", "nodeJ"):
            out[e[key]] = out.get(e[key], 0.0) + half
    return out


def self_weight_nodal(data):
    """Peso propio de columnas y muros: mitad a cada extremo [kN]."""
    nodes = node_map(data)
    out = {}
    if not PESO_PROPIO_VERTICALES:
        return out
    for e in data.get("elements", []):
        if e.get("type") == "columna":
            area = float(e.get("width_m") or 0.7) * float(e.get("height_m") or 0.7)
        elif e.get("type") == "muro_eq":
            area = float(e["wallLength_m"]) * float(e["wallThickness_m"])
        else:
            continue
        w = PESO_ESPECIFICO_HA_KN_M3 * area * element_length(e, nodes)
        for key in ("nodeI", "nodeJ"):
            out[e[key]] = out.get(e[key], 0.0) + 0.5 * w
    return out

def load_model_data(path=None, fix_connectivity=True):
    """Carga el JSON del edificio completo y (por defecto) corrige la conectividad."""
    data = load_json(path or JSON_PATH)
    if not fix_connectivity:
        return data
    fixed, _ = corregir_conectividad(data)
    if MODELAR_MUROS:
        fixed, _ = agregar_muros(fixed)
    return fixed


def structural_components(data):
    """Componentes conexas de barras y si tienen apoyo declarado."""
    nodes = node_map(data)
    adjacency = {}
    for e in data.get("elements", []):
        ni, nj = e.get("nodeI"), e.get("nodeJ")
        if ni in nodes and nj in nodes:
            adjacency.setdefault(ni, set()).add(nj)
            adjacency.setdefault(nj, set()).add(ni)
    support_nodes = {s.get("node") for s in data.get("supports", [])}
    seen = set()
    comps = []
    for seed in sorted(adjacency):
        if seed in seen:
            continue
        seen.add(seed)
        stack, comp = [seed], []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for nxt in adjacency[cur]:
                if nxt not in seen:
                    seen.add(nxt)
                    stack.append(nxt)
        comps.append({"nodes": comp, "apoyado": any(n in support_nodes for n in comp)})
    return comps


# ==================================================================
# CARGAS: nodales + distribuidas en barras
# ==================================================================
# True: la carga muerta D y la sobrecarga Q de cada viga se aplican como
# carga uniforme (eleLoad -beamUniform) a lo largo de la viga, en vez de
# repartir qL/2 a cada nodo. Asi los diagramas de vano son parabolicos y
# los momentos de empotramiento aparecen en el analisis. Las cargas NO se
# cuentan dos veces: en este modo no se generan cargas nodales de gravedad.
CARGAS_GRAVEDAD_DISTRIBUIDAS = True


class LoadSet(dict):
    """dict nodo -> [Fx, Fy, Fz] con cargas distribuidas por barra.

    element_loads: {id_elemento: [wx, wy, wz]} en coordenadas GLOBALES [kN/m].
    """

    def __init__(self, *args, element_loads=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.element_loads = dict(element_loads or {})


def element_loads_of(loads):
    return getattr(loads, "element_loads", {}) or {}


def total_load_vector(loads, data):
    """Suma global [Fx, Fy, Fz] de cargas nodales + distribuidas."""
    total = [0.0, 0.0, 0.0]
    for vec in loads.values():
        for k in range(3):
            total[k] += vec[k]
    element_loads = element_loads_of(loads)
    if element_loads:
        nodes = node_map(data)
        by_id = {e["id"]: e for e in data.get("elements", [])}
        for eid, w in element_loads.items():
            length = element_length(by_id[eid], nodes)
            for k in range(3):
                total[k] += w[k] * length
    return total


def beam_gravity_element_loads(data, field=None, per_beam_total=None):
    """Carga uniforme vertical (hacia -Z) por viga: w = total / L."""
    nodes = node_map(data)
    out = {}
    for element in data.get("elements", []):
        if element.get("type") != "viga":
            continue
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue
        total = per_beam_total(element) if per_beam_total else float(element.get(field) or 0.0)
        length = element_length(element, nodes)
        if total <= 0.0 or length <= 0.0:
            continue
        out[element["id"]] = [0.0, 0.0, -total / length]
    return out


def live_load_set(live_transfer):
    """Caso Q listo para analizar (nodal o distribuido segun el modo)."""
    loads = LoadSet(vector_loads_from_dict(live_transfer["cargas_nodales_Q"]))
    for eid, w in (live_transfer.get("cargas_distribuidas_Q") or {}).items():
        loads.element_loads[int(eid)] = list(w)
    return loads


def transfer_live_load(data, q_q):
    nodes = node_map(data)
    nodal_loads = {node_id: {"Fx": 0.0, "Fy": 0.0, "Fz": 0.0} for node_id in nodes}
    distributed = {}
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

        if CARGAS_GRAVEDAD_DISTRIBUIDAS and length > 0.0:
            distributed[element["id"]] = [0.0, 0.0, -q_lineal]
        else:
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
            "aplicacion": "distribuida (eleLoad -beamUniform)" if element["id"] in distributed else "nodal qL/2",
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
        "cargas_distribuidas_Q": {str(k): v for k, v in distributed.items()},
        "modo_aplicacion": "distribuida" if CARGAS_GRAVEDAD_DISTRIBUIDAS else "nodal",
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
    connected_nodes = set()
    for element in data.get("elements", []):
        connected_nodes.add(element.get("nodeI"))
        connected_nodes.add(element.get("nodeJ"))
    dead = dead_load_by_floor(data)
    live = {floor: values["Q_kN"] for floor, values in live_transfer["por_piso"].items()}
    floor_names = floor_name_by_z(data)
    floor_groups = group_close_floors(set(dead) | set(live), FLOOR_GROUP_TOL_M)
    q_by_beam = {beam["id"]: beam["Q_total_kN"] for beam in live_transfer.get("vigas", [])}
    node_weights = {}
    node_building = {}
    for element in data.get("elements", []):
        if element.get("nodeI") not in nodes or element.get("nodeJ") not in nodes:
            continue
        for key in ("nodeI", "nodeJ"):
            node_building.setdefault(element[key], set()).add(element.get("sourceBuilding") or "?")
        if element.get("type") != "viga":
            continue
        w = float(element.get("deadLoad") or 0.0) + 0.5 * q_by_beam.get(element["id"], 0.0)
        for key in ("nodeI", "nodeJ"):
            node_weights[element[key]] = node_weights.get(element[key], 0.0) + 0.5 * w
    self_weight = seismic_self_weight_nodal(data)
    for nid, w in self_weight.items():
        node_weights[nid] = node_weights.get(nid, 0.0) + w
    ex_nodal = {}
    ey_nodal = {}
    all_z = [node["z"] for node in nodes.values() if node["id"] in connected_nodes]
    base_z = min(all_z)
    height_total = max(max(all_z) - base_z, 1e-6)
    floor_rows = []

    for floor_group in floor_groups:
        floor_nodes = floor_nodes_for_group(nodes, floor_group)
        if not floor_nodes:
            continue
        structural_floor_nodes = [node for node in floor_nodes if node["id"] in connected_nodes]
        if structural_floor_nodes:
            floor_nodes = structural_floor_nodes
        floor = weighted_floor_z(floor_group, dead, live)
        d = sum(dead.get(level, 0.0) for level in floor_group)
        q = sum(live.get(level, 0.0) for level in floor_group)
        seismic_weight = d + 0.5 * q
        lateral = seismic_coeff * seismic_weight

        # Fuerza inercial de cada nodo proporcional a su peso sismico
        # (D + 0.5Q concentrado en los extremos de cada viga). Reemplaza la
        # version anterior que aplicaba TODO el corte del piso en un unico
        # nodo cercano al CM (sin diafragma rigido eso concentraba el corte
        # en una sola union y dejaba a edificio_2 sin carga sismica).
        floor_ids = {node["id"] for node in floor_nodes}
        weights = {nid: w for nid, w in node_weights.items() if nid in floor_ids and w > 0.0}
        w_sum = sum(weights.values())
        if w_sum <= 0.0:
            weights = {nid: 1.0 for nid in floor_ids}
            w_sum = float(len(weights))
        # peso propio de columnas y muros concentrado en los nodos del piso
        d_self = sum(self_weight.get(nid, 0.0) for nid in floor_ids)
        d += d_self
        seismic_weight = d + 0.5 * q
        lateral = seismic_coeff * seismic_weight
        cm_x = sum(nodes[nid]["x"] * w for nid, w in weights.items()) / w_sum
        cm_y = sum(nodes[nid]["y"] * w for nid, w in weights.items()) / w_sum
        application_node = closest_node_to_xy([nodes[nid] for nid in weights], cm_x, cm_y)
        by_building = {}
        xs = [nodes[nid]["x"] for nid in weights]
        ys = [nodes[nid]["y"] for nid in weights]
        z_rel = max(float(floor) - base_z, 0.0)
        ecc_ex = TORSION_ACCIDENTAL_FACTOR * (max(ys) - min(ys)) * z_rel / height_total if TORSION_ACCIDENTAL else 0.0
        ecc_ey = TORSION_ACCIDENTAL_FACTOR * (max(xs) - min(xs)) * z_rel / height_total if TORSION_ACCIDENTAL else 0.0
        for nid, w in weights.items():
            f = lateral * w / w_sum
            ex_nodal[str(nid)] = {"Fx": f, "Fy": 0.0, "Fz": 0.0, "Mz": f * ecc_ex}
            ey_nodal[str(nid)] = {"Fx": 0.0, "Fy": f, "Fz": 0.0, "Mz": f * ecc_ey}
            for building in node_building.get(nid, {"?"}):
                by_building[building] = by_building.get(building, 0.0) + f / len(node_building.get(nid, {"?"}))
        floor_rows.append({
            "piso": floor_label(floor_group, floor, floor_names),
            "floor_z_m": float(floor),
            "niveles_agrupados_z_m": [float(level) for level in floor_group],
            "D_kN": d,
            "D_peso_propio_columnas_muros_vigas_kN": d_self,
            "Q_kN": q,
            "W_sismico_D_plus_0_5Q_kN": seismic_weight,
            "masa_equivalente_kN_s2_m": seismic_weight / G_ACCEL,
            "coeficiente_sismico": seismic_coeff,
            "F_EX_kN": lateral,
            "F_EY_kN": lateral,
            "centro_masa_estimado": {"x": cm_x, "y": cm_y, "z": float(floor)},
            "nodo_aplicacion": application_node["id"],
            "nodo_aplicacion_nota": "nodo de referencia mas cercano al CM; la fuerza se reparte en todos los nodos del piso",
            "n_nodos_con_fuerza": len(weights),
            "F_por_edificio_kN": {k: round(v, 6) for k, v in sorted(by_building.items())},
            "excentricidad_accidental_EX_m": ecc_ex,
            "excentricidad_accidental_EY_m": ecc_ey,
            "torsion_EX_respecto_CM_kN_m": lateral * ecc_ex,
            "torsion_EY_respecto_CM_kN_m": lateral * ecc_ey,
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
            "distribucion": "F_i = C * W_i en cada nodo del piso (W_i = D + 0.5Q tributario del nodo); resultante en el CM de masas.",
            "torsion": "Torsion accidental NCh433: M_k = F_k * 0.10 b_k Z_k / H (signo +), repartida como Mz nodal proporcional a la masa; con diafragma rigido equivale a un momento en el piso.",
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


ELEMENT_AXES = {}
AUTO_ANCHORS = []
DIAPHRAGMS = []
# Diafragma rigido por nivel (losas). Sin el, los muros del edificio 1 y del
# ascensor (sin vigas que lleguen a ellos) no reciben carga lateral.
DIAFRAGMA_RIGIDO = True
DIAPHRAGM_MASTER_BASE = 1_000_000
CONSTRAINT_HANDLER = "Plain"


def local_axes(ni, nj, vecxz):
    """Ejes locales (x, y, z) de geomTransf Linear en coordenadas globales."""
    d = [nj["x"] - ni["x"], nj["y"] - ni["y"], nj["z"] - ni["z"]]
    length = math.sqrt(sum(v * v for v in d))
    ex = [v / length for v in d]
    ey = [vecxz[1] * ex[2] - vecxz[2] * ex[1], vecxz[2] * ex[0] - vecxz[0] * ex[2], vecxz[0] * ex[1] - vecxz[1] * ex[0]]
    ny = math.sqrt(sum(v * v for v in ey))
    ey = [v / ny for v in ey]
    ez = [ex[1] * ey[2] - ex[2] * ey[1], ex[2] * ey[0] - ex[0] * ey[2], ex[0] * ey[1] - ex[1] * ey[0]]
    return ex, ey, ez


def build_model(data):
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para correr la Parte C.")

    global CONSTRAINT_HANDLER
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)
    ELEMENT_AXES.clear()
    AUTO_ANCHORS.clear()
    DIAPHRAGMS.clear()
    CONSTRAINT_HANDLER = "Plain"
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

    declared_support_nodes = {support.get("node") for support in data.get("supports", [])}
    for node in nodes.values():
        # Los nodos sin barras (geometria de muros/losas) no se crean: antes se
        # creaban y se empotraban (252 "apoyos" ficticios en getFixedNodes).
        if node["id"] in connected_nodes or node["id"] in declared_support_nodes:
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
        factor = float(element.get("stiffnessFactor") or 1.0)  # enlaces rigidos
        area, iy, iz, j = area * factor, iy * factor, iz * factor, j * factor
        ni = nodes[element["nodeI"]]
        nj = nodes[element["nodeJ"]]
        length = element_length(element, nodes)
        dz = abs(nj["z"] - ni["z"])
        transf = 2 if length > 0.0 and dz / length > 0.90 else 1
        ops.element("elasticBeamColumn", element["id"], element["nodeI"], element["nodeJ"], area, E_CONCRETE, G_CONCRETE, j, iy, iz, transf)
        ELEMENT_AXES[element["id"]] = local_axes(ni, nj, (1.0, 0.0, 0.0) if transf == 2 else (0.0, 0.0, 1.0))

    for node_id in support_nodes:
        if node_id not in connected_nodes:
            ops.fix(node_id, 1, 1, 1, 1, 1, 1)  # apoyo declarado sin barras

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
            AUTO_ANCHORS.append({"node": anchor, "componente_nodos": len(component)})

    if DIAFRAGMA_RIGIDO:
        base_z = min(nodes[n]["z"] for n in connected_nodes)
        by_level = {}
        for n in connected_nodes:
            z = round(nodes[n]["z"], 3)
            if abs(z - base_z) < 1e-6 or n in support_nodes:
                continue
            by_level.setdefault(z, []).append(n)
        for k, (z, slaves) in enumerate(sorted(by_level.items()), start=1):
            if len(slaves) < 2:
                continue
            master = DIAPHRAGM_MASTER_BASE + k
            cx = sum(nodes[n]["x"] for n in slaves) / len(slaves)
            cy = sum(nodes[n]["y"] for n in slaves) / len(slaves)
            z_exact = nodes[slaves[0]]["z"]
            if any(nodes[n]["z"] != z_exact for n in slaves):
                # OpenSees ignora sin aviso los esclavos que no estan en el plano del maestro.
                raise RuntimeError(f"Diafragma z={z}: nodos con cotas distintas {sorted({nodes[n]['z'] for n in slaves})}")
            ops.node(master, cx, cy, z_exact)
            ops.fix(master, 0, 0, 1, 1, 1, 0)
            ops.rigidDiaphragm(3, master, *sorted(slaves))
            DIAPHRAGMS.append({"z": z, "master": master, "slaves": len(slaves)})
        if DIAPHRAGMS:
            CONSTRAINT_HANDLER = "Transformation"

    return nodes


def dead_nodal_loads(data):
    """Caso G. Con CARGAS_GRAVEDAD_DISTRIBUIDAS la carga muerta de cada viga va
    como carga uniforme (LoadSet.element_loads) y no se generan fuerzas nodales."""
    if CARGAS_GRAVEDAD_DISTRIBUIDAS:
        nodal = {n: [0.0, 0.0, -w] for n, w in self_weight_nodal(data).items()}
        loads = LoadSet(nodal, element_loads=beam_gravity_element_loads(data, field="deadLoad"))
        nodes = node_map(data)
        for e in data.get("elements", []):
            w = beam_self_weight_per_m(e)
            if w > 0.0 and e.get("nodeI") in nodes and e.get("nodeJ") in nodes:
                acc = loads.element_loads.setdefault(e["id"], [0.0, 0.0, 0.0])
                acc[2] -= w
        return loads
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
    """{nodo: [Fx, Fy, Fz, Mz]} (Mz opcional, torsion accidental)."""
    return {int(node): [float(vec.get("Fx", 0.0)), float(vec.get("Fy", 0.0)), float(vec.get("Fz", 0.0)),
                        float(vec.get("Mz", 0.0))] for node, vec in loads.items()}


def combine_nodal_loads(load_sets, lambdas):
    node_ids = sorted({node for loads in load_sets.values() for node in loads})
    combined = LoadSet({node: [0.0, 0.0, 0.0, 0.0] for node in node_ids})
    for case, loads in load_sets.items():
        factor = lambdas.get(case, 0.0)
        for node, vec in loads.items():
            for k in range(len(vec)):
                combined[node][k] += factor * vec[k]
        for eid, w in element_loads_of(loads).items():
            acc = combined.element_loads.setdefault(eid, [0.0, 0.0, 0.0])
            for k in range(3):
                acc[k] += factor * w[k]
    return combined


def apply_nodal_loads(nodal_loads):
    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)
    for node, load in nodal_loads.items():
        mz = load[3] if len(load) > 3 else 0.0   # torsion accidental
        if max(abs(load[0]), abs(load[1]), abs(load[2]), abs(mz)) < 1e-12:
            continue
        ops.load(node, load[0], load[1], load[2], 0.0, 0.0, mz)
    for eid, w in element_loads_of(nodal_loads).items():
        if max(abs(w[0]), abs(w[1]), abs(w[2])) < 1e-12:
            continue
        ex, ey, ez = ELEMENT_AXES[eid]
        wx = sum(w[k] * ex[k] for k in range(3))
        wy = sum(w[k] * ey[k] for k in range(3))
        wz = sum(w[k] * ez[k] for k in range(3))
        ops.eleLoad("-ele", eid, "-type", "-beamUniform", wy, wz, wx)


def analyze_case(data, nodal_loads, control_node, element_id):
    nodes = build_model(data)
    apply_nodal_loads(nodal_loads)
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints(CONSTRAINT_HANDLER)
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
        "Q": live_load_set(live_transfer),
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


def phi_moment_at(phi_points, p_demand):
    pts = sorted(phi_points, key=lambda point: point["phiPn_kN"])
    if not pts:
        return None
    if p_demand > pts[-1]["phiPn_kN"] or p_demand < pts[0]["phiPn_kN"]:
        return None
    for i in range(len(pts) - 1):
        p0 = pts[i]["phiPn_kN"]
        p1 = pts[i + 1]["phiPn_kN"]
        if p0 <= p_demand <= p1:
            t = (p_demand - p0) / (p1 - p0) if p1 != p0 else 0.0
            return max(0.0, pts[i]["phiMn_kN_m"] + t * (pts[i + 1]["phiMn_kN_m"] - pts[i]["phiMn_kN_m"]))
    return pts[-1]["phiMn_kN_m"]


def supported_components(adjacency, support_nodes):
    visited = set()
    components = []
    for node_id in adjacency:
        if node_id in visited:
            continue
        visited.add(node_id)
        stack = [node_id]
        component = []
        while stack:
            current = stack.pop()
            component.append(current)
            for nxt in adjacency.get(current, ()):
                if nxt not in visited:
                    visited.add(nxt)
                    stack.append(nxt)
        if any(node in support_nodes for node in component):
            components.append(component)
    for component in components:
        for node_id in component:
            yield node_id


def verify_building(data, live_transfer, seismic, lambdas):
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para correr la verificacion.")
    nodes = node_map(data)
    load_sets = {
        "G": dead_nodal_loads(data),
        "Q": live_load_set(live_transfer),
        "EX": vector_loads_from_dict(seismic["cargas_nodales_EX"]),
        "EY": vector_loads_from_dict(seismic["cargas_nodales_EY"]),
    }
    combined = combine_nodal_loads(load_sets, lambdas)

    build_model(data)
    apply_nodal_loads(combined)
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints(CONSTRAINT_HANDLER)
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    ops.analyze(1)
    ops.reactions()

    reactions = [0.0, 0.0, 0.0]
    for support in data.get("supports", []):
        node = support.get("node")
        if node in nodes:
            reaction = ops.nodeReaction(node)
            reactions[0] += reaction[0]
            reactions[1] += reaction[1]
            reactions[2] += reaction[2]

    connected = {element["nodeI"] for element in data.get("elements", [])} | {element["nodeJ"] for element in data.get("elements", [])}
    adjacency = {node_id: set() for node_id in nodes}
    for element in data.get("elements", []):
        ni = element.get("nodeI")
        nj = element.get("nodeJ")
        if ni in nodes and nj in nodes:
            adjacency[ni].add(nj)
            adjacency[nj].add(ni)
    support_nodes = {support.get("node") for support in data.get("supports", []) if support.get("node") in nodes}
    supported = set()
    if support_nodes:
        for node_id in supported_components(adjacency, support_nodes):
            supported.add(node_id)
    main_building = "edificio_1"
    building_columns = {
        building: [
            element for element in data.get("elements", [])
            if element.get("type") == "columna"
            and element.get("sourceBuilding") == building
            and element.get("nodeI") in nodes
            and element.get("nodeJ") in nodes
        ]
        for building in {element.get("sourceBuilding") for element in data.get("elements", []) if element.get("type") == "columna"}
    }
    candidate_control = [
        node for node in nodes.values()
        if node["id"] in connected
        and node["id"] in supported
        and any(node["id"] in (element.get("nodeI"), element.get("nodeJ")) for element in building_columns.get(main_building, []))
    ]
    if not candidate_control:
        candidate_control = [node for node in nodes.values() if node["id"] in connected and node["id"] in supported]
    if not candidate_control:
        candidate_control = [node for node in nodes.values() if node["id"] in connected]
    control_node = max(candidate_control, key=lambda n: (n["z"], n["x"] ** 2 + n["y"] ** 2))["id"]
    displacement = list(ops.nodeDisp(control_node))[:3]

    section = make_column_fibers()
    ag = section["b"] * section["h"]
    ast = len(section["rebar_xy"]) * section["bar_area_m2"]
    po = 0.85 * section["fc"] * (ag - ast) + section["fy"] * ast
    pm_points = simplified_pm_points(section, po)
    phi_points = [{"phiPn_kN": point["phiPn_kN"], "phiMn_kN_m": point["phiMn_kN_m"]} for point in pm_points]
    phi_po = max(point["phiPn_kN"] for point in phi_points)

    supported_columns = [
        element for element in building_columns.get(main_building, [])
        if element["nodeI"] in supported and element["nodeJ"] in supported
    ]
    excluded_other_buildings = [
        element for building, columns in building_columns.items()
        if building != main_building
        for element in columns
    ]
    floating_columns = [
        element for element in building_columns.get(main_building, [])
        if element not in supported_columns
    ]

    column_rows = []
    for element in supported_columns:
        try:
            # Acciones LOCALES (x local = eje de la columna). Con eleForce
            # (global) el indice 0 es Fx global = corte, no axial.
            force = ops.eleResponse(element["id"], "localForce")
        except Exception:
            continue
        if not force or len(force) < 12:
            continue
        p_compression = max(force[0], -force[6], 0.0)
        m_demand = max(math.hypot(force[4], force[5]), math.hypot(force[10], force[11]))
        m_allow = phi_moment_at(phi_points, p_compression)
        axial_util = p_compression / phi_po if phi_po else 0.0
        if m_allow is None or m_allow <= 0.0:
            flex_util = 0.0 if m_demand == 0.0 else float("inf")
        else:
            flex_util = m_demand / m_allow
        ratio = max(axial_util, flex_util) if flex_util != float("inf") else float("inf")
        column_rows.append({
            "element_id": element["id"],
            "element_tag": element_tag(element),
            "piso_z_m": element_mid_z(element, nodes),
            "P_demanda_kN": round(p_compression, 2),
            "M_demanda_kN_m": round(m_demand, 2),
            "phiMn_disponible_kN_m": None if m_allow is None else round(m_allow, 2),
            "utilizacion_axial": round(axial_util, 3),
            "utilizacion_flexion": None if flex_util == float("inf") else round(flex_util, 3),
            "utilizacion_total": None if ratio == float("inf") else round(ratio, 3),
            "cumple": ratio <= 1.0,
        })

    failing = [row for row in column_rows if not row["cumple"]]
    worst = max(column_rows, key=lambda row: row["utilizacion_total"] if row["utilizacion_total"] is not None else float("inf"), default=None)
    return {
        "lambdas": lambdas,
        "combinacion": "R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY",
        "control_node": control_node,
        "desplazamiento_control_m": displacement,
        "reacciones_globales_Fx_Fy_Fz_kN": reactions,
        "capacidad_columna_70_70": {"Po_kN": po, "phiPo_kN": phi_po, "puntos_pm": pm_points},
        "edificio_analizado": main_building,
        "columnas_edificio_1_excluidas_no_apoyadas": len(floating_columns),
        "columnas_excluidas_otros_edificios": len(excluded_other_buildings),
        "otro_edificio_artefacto": any(building != main_building for building in building_columns),
        "columnas_evaluadas": len(column_rows),
        "columnas_no_cumplen": len(failing),
        "peor_columna": worst,
        "detalle_columnas": column_rows,
        "veredicto": "NO CUMPLE" if failing else ("CUMPLE" if column_rows else "SIN DATOS"),
    }


def print_verification(verdict, q_q, seismic_coeff):
    print("\nParte E - Verificacion: aguanta?")
    print(f"q_Q = {q_q:.4f} kN/m2 | Coef sismico = {seismic_coeff}")
    print("Combinacion: R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY")
    for case, factor in verdict["lambdas"].items():
        print(f"  lambda_{case} = {factor:.3f}")

    print("\nSanidad global del modelo")
    print(f"  Nodo de control = {verdict['control_node']}")
    u = verdict["desplazamiento_control_m"]
    print(f"  Desplazamiento ux, uy, uz [m] = {[round(value, 5) for value in u]}")
    print(f"  |u| = {math.sqrt(sum(value * value for value in u)):.4f} m")
    if math.sqrt(sum(value * value for value in u)) > 0.25:
        print("  ATENCION: |u| es grande para un modelo lineal; verificar rigideces/soportes. Demandas referenciales.")
    print(f"  Reacciones Fx, Fy, Fz [kN] = {[round(value, 1) for value in verdict['reacciones_globales_Fx_Fy_Fz_kN']]}")

    print(f"\nColumnas evaluadas (edificio 1) = {verdict['columnas_evaluadas']} | No cumplen = {verdict['columnas_no_cumplen']}")
    if verdict.get("columnas_excluidas_otros_edificios"):
        print(f"  Columnas de otro edificio excluidas del chequeo: {verdict['columnas_excluidas_otros_edificios']} (modelo lineal no fisico: se deforma decenas de metros)")
    if verdict.get("columnas_edificio_1_excluidas_no_apoyadas"):
        print(f"  Columnas de edificio 1 sin apoyo excluidas: {verdict['columnas_edificio_1_excluidas_no_apoyadas']}")
    print("  Nota: P y M salen del modelo elastico (cargas de losas a traves de vigas); resultado referencial.")
    worst = verdict["peor_columna"]
    if worst:
        print("Peor columna:")
        print(f"  {worst['element_tag']} en z={worst['piso_z_m']:.2f} m: P demanda = {worst['P_demanda_kN']:.1f} kN, M demanda = {worst['M_demanda_kN_m']:.1f} kN*m")
        if worst["phiMn_disponible_kN_m"] is not None:
            print(f"  phiMn disponible = {worst['phiMn_disponible_kN_m']:.1f} kN*m | M/phiMn = {worst['utilizacion_flexion']:.3f}")
        print(f"  Utilizacion total = {worst['utilizacion_total']}  (<= 1.0 significa que aguanta)")

    print(f"\n==> VEREDICTO: {verdict['veredicto']}")
    if verdict["veredicto"] == "CUMPLE":
        print("  Todas las columnas quedan dentro del diagrama P-M con esta combinacion.")
    elif verdict["veredicto"] == "NO CUMPLE":
        print("  Columnas que sobrepasan el diagrama P-M:")
        for row in verdict["detalle_columnas"]:
            if not row["cumple"]:
                print(f"    {row['element_tag']} (z={row['piso_z_m']:.2f} m): P={row['P_demanda_kN']:.0f} kN, M={row['M_demanda_kN_m']:.0f} kN*m, utilizacion={row['utilizacion_total']}")
        print("  Soluciones: reducir cargas (SC/coef), agrandar la seccion, o aumentar el refuerzo.")


def concrete_stress(eps, fc):
    if eps <= 0.0:
        return 0.0
    return min(25_000_000.0 * eps, 0.85 * fc)


def steel_stress(eps, fy, es):
    return max(-fy, min(fy, es * eps))


def evenly_spaced_positions(start, end, count):
    if count <= 0:
        return []
    if count == 1:
        return [0.5 * (start + end)]
    return [start + i * (end - start) / (count - 1) for i in range(count)]


def rebar_coordinates(b, h, cover):
    x_left = -b / 2.0 + cover
    x_right = b / 2.0 - cover
    rows = [
        ("inferior", -h / 2.0 + cover, REBAR_BARS_INFERIOR),
        ("centro", 0.0, REBAR_BARS_CENTRO),
        ("superior", h / 2.0 - cover, REBAR_BARS_SUPERIOR),
    ]
    coords = []
    for row_name, y, bars in rows:
        for x in evenly_spaced_positions(x_left, x_right, bars):
            coords.append({"fila": row_name, "x": x, "y": y})
    return coords


def make_column_fibers():
    b = h = 0.70
    cover = 0.05
    fc = 25_000.0  # H-25
    fy = 420_000.0
    es = 200_000_000.0
    bar_area = math.pi * (BAR_DIAMETER_MM / 1000.0) ** 2 / 4.0
    fibers = []
    nx = CONCRETE_FIBERS_X
    ny = CONCRETE_FIBERS_Y
    for ix in range(nx):
        x = -b / 2.0 + (ix + 0.5) * b / nx
        for iy in range(ny):
            y = -h / 2.0 + (iy + 0.5) * h / ny
            fibers.append({"type": "concrete", "x": x, "y": y, "area": b / nx * h / ny})

    rebar_xy = rebar_coordinates(b, h, cover)
    for bar in rebar_xy:
        x = bar["x"]
        y = bar["y"]
        fibers.append({"type": "steel", "x": x, "y": y, "area": bar_area})
    return {"b": b, "h": h, "cover": cover, "fc": fc, "fy": fy, "Es": es, "bar_area_m2": bar_area, "fibers": fibers, "rebar_xy": rebar_xy, "concrete_fibers_x": nx, "concrete_fibers_y": ny}


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
    fc = -25_000.0  # H-25
    epsc0 = -0.002
    fcu = -21_250.0
    epscu = -0.003
    fy = 420_000.0
    es = 200_000_000.0
    b = h = 0.70
    cover = 0.05
    bar_area = math.pi * (BAR_DIAMETER_MM / 1000.0) ** 2 / 4.0

    ops.uniaxialMaterial("Concrete01", concrete_tag, fc, epsc0, fcu, epscu)
    ops.uniaxialMaterial("Steel01", steel_tag, fy, es, 0.01)
    ops.section("Fiber", section_tag)
    ops.patch("rect", concrete_tag, CONCRETE_FIBERS_Y, CONCRETE_FIBERS_X, -h / 2, -b / 2, h / 2, b / 2)
    y_bot = -h / 2 + cover
    y_mid = 0.0
    y_top = h / 2 - cover
    z_left = -b / 2 + cover
    z_right = b / 2 - cover
    rebar_layers = [
        ("inferior", REBAR_BARS_INFERIOR, y_bot),
        ("centro", REBAR_BARS_CENTRO, y_mid),
        ("superior", REBAR_BARS_SUPERIOR, y_top),
    ]
    for _, bars, y in rebar_layers:
        if bars <= 0:
            continue
        if bars == 1:
            ops.layer("straight", steel_tag, bars, bar_area, y, 0.0, y, 0.0)
        else:
            ops.layer("straight", steel_tag, bars, bar_area, y, z_left, y, z_right)
    return {
        "section_tag": section_tag,
        "concrete_material": {"tag": concrete_tag, "type": "Concrete01", "fc_kN_m2": fc, "epsc0": epsc0, "fcu_kN_m2": fcu, "epscu": epscu},
        "steel_material": {"tag": steel_tag, "type": "Steel01", "fy_kN_m2": fy, "Es_kN_m2": es, "b": 0.01},
        "patch": f"rect concrete {CONCRETE_FIBERS_X} x {CONCRETE_FIBERS_Y}",
        "reinforcement": f"{REBAR_BARS_INFERIOR} barras abajo, {REBAR_BARS_CENTRO} al centro, {REBAR_BARS_SUPERIOR} arriba; diametro {BAR_DIAMETER_MM:g} mm por barra",
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


def steel_force_from_strain(eps, area_mm2, fy_mpa, es_mpa):
    stress_mpa = max(-fy_mpa, min(fy_mpa, es_mpa * eps))
    return stress_mpa * area_mm2 / 1000.0, stress_mpa


def steel_rows_mm(section):
    h_mm = section["h"] * 1000.0
    cover_mm = section["cover"] * 1000.0
    bar_area_mm2 = section["bar_area_m2"] * 1_000_000.0
    return [
        {"fila": "superior", "bars": REBAR_BARS_SUPERIOR, "d_mm": cover_mm, "area_mm2": REBAR_BARS_SUPERIOR * bar_area_mm2},
        {"fila": "media", "bars": REBAR_BARS_CENTRO, "d_mm": h_mm / 2.0, "area_mm2": REBAR_BARS_CENTRO * bar_area_mm2},
        {"fila": "inferior_traccion", "bars": REBAR_BARS_INFERIOR, "d_mm": h_mm - cover_mm, "area_mm2": REBAR_BARS_INFERIOR * bar_area_mm2},
    ]


def nominal_pm_from_c(section, c_mm, label, phi_factor, eps_tension):
    b_mm = section["b"] * 1000.0
    h_mm = section["h"] * 1000.0
    fc_mpa = section["fc"] / 1000.0
    fy_mpa = section["fy"] / 1000.0
    es_mpa = section["Es"] / 1000.0
    eps_cu = 0.003
    beta1 = 0.85
    a_mm = min(beta1 * c_mm, h_mm)
    concrete_force_kN = 0.85 * fc_mpa * a_mm * b_mm / 1000.0
    concrete_moment_kN_m = concrete_force_kN * (h_mm / 2.0 - a_mm / 2.0) / 1000.0
    steel_detail = []
    pn_kN = concrete_force_kN
    mn_kN_m = concrete_moment_kN_m

    for row in steel_rows_mm(section):
        eps_si = eps_cu * (c_mm - row["d_mm"]) / c_mm
        force_kN, stress_mpa = steel_force_from_strain(eps_si, row["area_mm2"], fy_mpa, es_mpa)
        moment_kN_m = force_kN * (h_mm / 2.0 - row["d_mm"]) / 1000.0
        pn_kN += force_kN
        mn_kN_m += moment_kN_m
        steel_detail.append({
            "fila": row["fila"],
            "barras": row["bars"],
            "d_mm": row["d_mm"],
            "eps_si": eps_si,
            "fs_MPa": stress_mpa,
            "Fs_kN": force_kN,
            "fluye": abs(stress_mpa) >= fy_mpa - 1e-9,
        })

    return {
        "estado": label,
        "phi_reduccion": phi_factor,
        "eps_cu": eps_cu,
        "eps_st_ultima_fila": eps_tension,
        "c_mm": c_mm,
        "a_mm": a_mm,
        "Cc_kN": concrete_force_kN,
        "Pn_kN": pn_kN,
        "Mn_kN_m": abs(mn_kN_m),
        "phiPn_kN": phi_factor * pn_kN,
        "phiMn_kN_m": phi_factor * abs(mn_kN_m),
        "steel_rows": steel_detail,
    }


def find_flexural_pure_point(section):
    lo = 1.0
    hi = section["h"] * 1000.0 * 2.0
    for _ in range(100):
        mid = 0.5 * (lo + hi)
        point = nominal_pm_from_c(section, mid, "D) Flexion pura", 0.90, None)
        if point["Pn_kN"] > 0.0:
            hi = mid
        else:
            lo = mid
    point = nominal_pm_from_c(section, 0.5 * (lo + hi), "D) Flexion pura", 0.90, None)
    point["nota"] = "c se obtiene iterando hasta Pn ~= 0."
    return point


def simplified_pm_points(section, po_kN):
    h_mm = section["h"] * 1000.0
    cover_mm = section["cover"] * 1000.0
    dt_mm = h_mm - cover_mm
    eps_cu = 0.003
    ast_mm2 = len(section["rebar_xy"]) * section["bar_area_m2"] * 1_000_000.0
    fy_mpa = section["fy"] / 1000.0

    pure_compression = {
        "estado": "A) Compresion pura",
        "phi_reduccion": 0.65,
        "eps_cu": None,
        "eps_st_ultima_fila": None,
        "c_mm": None,
        "a_mm": h_mm,
        "Cc_kN": None,
        "Pn_kN": po_kN,
        "Mn_kN_m": 0.0,
        "phiPn_kN": 0.65 * po_kN,
        "phiMn_kN_m": 0.0,
        "nota": "Toda la seccion trabaja en compresion; momento nulo o despreciable.",
    }
    balance_eps_st = 0.002
    ductile_eps_st = 0.005
    balance_c = eps_cu * dt_mm / (eps_cu + balance_eps_st)
    ductile_c = eps_cu * dt_mm / (eps_cu + ductile_eps_st)
    pure_tension_pn = -ast_mm2 * fy_mpa / 1000.0
    pure_tension = {
        "estado": "E) Traccion pura",
        "phi_reduccion": 0.90,
        "eps_cu": None,
        "eps_st_ultima_fila": None,
        "c_mm": None,
        "a_mm": 0.0,
        "Cc_kN": 0.0,
        "Pn_kN": pure_tension_pn,
        "Mn_kN_m": 0.0,
        "phiPn_kN": 0.90 * pure_tension_pn,
        "phiMn_kN_m": 0.0,
        "nota": "Hormigon traccionado agrietado; solo aporta el acero en fluencia.",
    }
    return [
        pure_compression,
        nominal_pm_from_c(section, balance_c, "B) Balance", 0.65, balance_eps_st),
        nominal_pm_from_c(section, ductile_c, "C) Ultima falla ductil", 0.90, ductile_eps_st),
        find_flexural_pure_point(section),
        pure_tension,
    ]


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
    pm = simplified_pm_points(section, po)

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
            "concrete_fibers": section["concrete_fibers_x"] * section["concrete_fibers_y"],
            "concrete_fibers_x": section["concrete_fibers_x"],
            "concrete_fibers_y": section["concrete_fibers_y"],
            "steel_bars": len(section["rebar_xy"]),
            "steel_bars_inferior": REBAR_BARS_INFERIOR,
            "steel_bars_centro": REBAR_BARS_CENTRO,
            "steel_bars_superior": REBAR_BARS_SUPERIOR,
            "bar_area_m2": section["bar_area_m2"],
            "bar_area_mm2": bar_area_mm2,
            "bar_diameter_mm": bar_diameter_mm(section["bar_area_m2"]),
            "steel_yield_strain": steel_yield_strain,
            "Po_kN": po,
        },
        "opensees_definition": opensees_section,
        "reinforcement_coordinates_xy_m": [{"fila": bar["fila"], "x": bar["x"], "y": bar["y"]} for bar in section["rebar_xy"]],
        "reinforcement_coordinates_xy_mm": [{"fila": bar["fila"], "x": bar["x"] * 1000.0, "y": bar["y"] * 1000.0} for bar in section["rebar_xy"]],
        "m_phi": mphi,
        "m_phi_first_steel_yield": first_yield,
        "p_m_points": pm,
        "interpretacion": "El diagrama P-M se construye con cinco estados manuales: compresion pura, balance, ultima falla ductil, flexion pura y traccion pura. La curva M-phi se mantiene como verificacion fibra a fibra de momento-curvatura.",
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

    plt.figure(figsize=(6, 5))
    plt.plot([p["Mn_kN_m"] for p in capacity["p_m_points"]], [p["Pn_kN"] for p in capacity["p_m_points"]], "ro-")
    for point in capacity["p_m_points"]:
        plt.annotate(point["estado"].split(")")[0], (point["Mn_kN_m"], point["Pn_kN"]), textcoords="offset points", xytext=(5, 5), fontsize=8)
    plt.xlabel("M [kN m]")
    plt.ylabel("P [kN]")
    plt.title("P-M simplificado COL70/70: estados A-E")
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
                "label": point["estado"],
                "P_kN": point["Pn_kN"],
                "M_kN_m": point["Mn_kN_m"],
                "phiP_kN": point["phiPn_kN"],
                "phiM_kN_m": point["phiMn_kN_m"],
                "phi_1_m": 0.0,
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
    print(f"  Fibras de hormigon        = {section['concrete_fibers']} ({section['concrete_fibers_x']} x {section['concrete_fibers_y']})")
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
    print(f"  Distribucion barras       = {section['steel_bars_inferior']} inferior, {section['steel_bars_centro']} centro, {section['steel_bars_superior']} superior")
    print(f"  Ast total                 = {section['Ast_m2']:.6f} m2")
    print(f"  Ast total                 = {section['Ast_mm2']:.1f} mm2")
    print(f"  Ag bruta                  = {section['Ag_mm2']:.1f} mm2")
    print(f"  Cuantia                   = {100.0 * section['cuantia_refuerzo']:.3f} %")
    print(f"  Po aproximado             = {section['Po_kN']:.3f} kN")
    print("\nPuntos curva P-M simplificada")
    for point in capacity["p_m_points"]:
        c_text = "-" if point.get("c_mm") is None else f"{point['c_mm']:.1f} mm"
        eps_text = "-" if point.get("eps_st_ultima_fila") is None else f"{point['eps_st_ultima_fila']:.4f}"
        print(f"  {point['estado']}: Pn = {point['Pn_kN']:.3f} kN, Mn = {point['Mn_kN_m']:.3f} kN*m, phi = {point['phi_reduccion']:.2f}, c = {c_text}, eps_t = {eps_text}")
        print(f"      phi*Pn = {point['phiPn_kN']:.3f} kN, phi*Mn = {point['phiMn_kN_m']:.3f} kN*m")
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


# ==================================================================
# PARTE ADICIONAL 1 - CORRIDA EXPLICITA Y EXTRACCION COMPLETA
# ==================================================================
def run_and_extract(data, nodal_loads):
    """Arma el modelo, aplica cargas nodales, analiza y extrae
    desplazamientos de todos los nodos, reacciones y fuerzas internas."""
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para este analisis.")
    nodes = build_model(data)
    apply_nodal_loads(nodal_loads)
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints(CONSTRAINT_HANDLER)
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    ok = ops.analyze(1)
    ops.reactions()

    displacements = {}
    for node_id in nodes:
        try:
            vals = list(ops.nodeDisp(node_id))
        except Exception:
            continue
        if not vals:
            continue
        displacements[node_id] = {
            "ux": vals[0] if len(vals) > 0 else 0.0,
            "uy": vals[1] if len(vals) > 1 else 0.0,
            "uz": vals[2] if len(vals) > 2 else 0.0,
            "rx": vals[3] if len(vals) > 3 else 0.0,
            "ry": vals[4] if len(vals) > 4 else 0.0,
            "rz": vals[5] if len(vals) > 5 else 0.0,
        }

    reactions = [0.0, 0.0, 0.0]
    per_node = {}
    for support in data.get("supports", []):
        node = support.get("node")
        if node not in nodes:
            continue
        try:
            r = list(ops.nodeReaction(node))
        except Exception:
            continue
        per_node[node] = r
        reactions[0] += r[0] if len(r) > 0 else 0.0
        reactions[1] += r[1] if len(r) > 1 else 0.0
        reactions[2] += r[2] if len(r) > 2 else 0.0

    element_forces = {}
    element_forces_local = {}
    for element in data.get("elements", []):
        try:
            force = list(ops.eleForce(element["id"]))
        except Exception:
            continue
        element_forces[element["id"]] = force
        try:
            element_forces_local[element["id"]] = list(ops.eleResponse(element["id"], "localForce"))
        except Exception:
            pass

    all_reactions = [0.0, 0.0, 0.0]
    fixed_nodes = list(ops.getFixedNodes()) if hasattr(ops, "getFixedNodes") else []
    for node in fixed_nodes:
        r = list(ops.nodeReaction(node))
        for k in range(3):
            all_reactions[k] += r[k]

    return {
        "ok": ok == 0,
        "ok_code": ok,
        "displacements": displacements,
        "reactions": {"sum_Fx": reactions[0], "sum_Fy": reactions[1], "sum_Fz": reactions[2]},
        "per_node_reactions": {str(k): v for k, v in per_node.items()},
        "reactions_all_fixed": {"sum_Fx": all_reactions[0], "sum_Fy": all_reactions[1], "sum_Fz": all_reactions[2]},
        "auto_anchors": list(AUTO_ANCHORS),
        "element_forces": element_forces,
        "element_forces_local": element_forces_local,
    }


def save_result_section(section_key, section_value):
    with open(OUTPUT_PATH, encoding="utf-8") as file:
        result = json.load(file)
    result[section_key] = section_value
    write_json(OUTPUT_PATH, result)
    print(f"Seccion '{section_key}' agregada a {OUTPUT_PATH}")


# ==================================================================
# PARTE G - CASO DE CARGA PERMANENTE G (numerico)
# ==================================================================
def gravity_case_report(data):
    """Corre el caso G (peso propio + terminaciones) y reporta cargas
    aplicadas por piso, reacciones, conservacion (G = sum Rz) y extremos.
    Solo se consideran las vigas/columnas de componentes apoyados: los
    elementos flotantes (sin apoyo) del JSON completo se excluyen porque
    producen artefactos no fisicos (desplazamientos de decenas de metros)."""
    if ops is None:
        raise RuntimeError("Falta instalar openseespy para el caso G.")
    nodes = node_map(data)
    adjacency = {node_id: set() for node_id in nodes}
    for element in data.get("elements", []):
        ni = element.get("nodeI")
        nj = element.get("nodeJ")
        if ni in nodes and nj in nodes:
            adjacency[ni].add(nj)
            adjacency[nj].add(ni)
    support_nodes = {support.get("node") for support in data.get("supports", []) if support.get("node") in nodes}
    visited = set()
    components = []
    for seed in adjacency:
        if seed in visited:
            continue
        visited.add(seed)
        stack = [seed]
        comp = []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for nxt in adjacency.get(cur, ()):
                if nxt not in visited:
                    visited.add(nxt)
                    stack.append(nxt)
        if any(n in support_nodes for n in comp):
            components.append(set(comp))
    supported = set().union(*components) if components else set()
    main = max(components, key=len) if components else set()
    n_supports_main = len([n for n in support_nodes if n in main])
    src_of_main = None
    for element in data.get("elements", []):
        if element.get("nodeI") in main and element.get("nodeJ") in main and element.get("sourceBuilding"):
            src_of_main = element.get("sourceBuilding")
            break

    aplicado_total = 0.0
    by_floor = {}
    aplicado_main = 0.0
    n_floating = 0
    for element in data.get("elements", []):
        if element.get("type") != "viga":
            continue
        ni = element.get("nodeI")
        nj = element.get("nodeJ")
        if ni not in nodes or nj not in nodes:
            continue
        total = float(element.get("deadLoad") or 0.0)
        if ni not in supported or nj not in supported:
            if total > 1e-9:
                n_floating += 1
            continue
        if total <= 1e-9:
            continue
        aplicado_total += total
        if ni in main and nj in main:
            aplicado_main += total
        nivel = str(element_mid_z(element, nodes))
        by_floor[nivel] = by_floor.get(nivel, 0.0) + total

    g_loads = dead_nodal_loads(data)
    res = run_and_extract(data, g_loads)

    sum_rx = res["reactions"]["sum_Fx"]
    sum_ry = res["reactions"]["sum_Fy"]
    sum_rz = res["reactions"]["sum_Fz"]
    n_supports = len(data.get("supports", []))
    err = abs(aplicado_total - sum_rz)


    def _gather(nid_set):
        """Extremos de desplazamiento restringidos a un conjunto de nodos."""
        uzm = 0.0
        uzmn = None
        hm = 0.0
        hmn = None
        for nid, d in res["displacements"].items():
            if nid not in nid_set:
                continue
            az = abs(d["uz"])
            if az > uzm:
                uzm, uzmn = az, nid
            h = (d["ux"] ** 2 + d["uy"] ** 2) ** 0.5
            if h > hm:
                hm, hmn = h, nid
        return uzm, uzmn, hm, hmn


    def _gather_cols(nid_set):
        """Extremos de fuerzas de columnas restringidos a un conjunto de nodos."""
        nmax = 0.0
        nmaxe = None
        mmax = 0.0
        mmaxe = None
        ncols = 0
        for element in data.get("elements", []):
            if element.get("type") != "columna":
                continue
            if element.get("nodeI") not in nid_set or element.get("nodeJ") not in nid_set:
                continue
            force = res.get("element_forces_local", {}).get(element["id"])
            if not force or len(force) < 12:
                continue
            ncols += 1
            for extremo in (force[:6], force[6:]):
                N = abs(extremo[0])
                M = (extremo[4] ** 2 + extremo[5] ** 2) ** 0.5
                if N > nmax:
                    nmax, nmaxe = N, element["id"]
                if M > mmax:
                    mmax, mmaxe = M, element["id"]
        return nmax, nmaxe, mmax, mmaxe, ncols


    max_uz, max_uz_node, max_h, max_h_node = _gather(supported)
    max_N, max_N_elem, max_M, max_M_elem, n_columns = _gather_cols(supported)
    max_uz_m1, max_uz_node_m1, max_h_m1, max_h_node_m1 = _gather(main)
    max_N_m1, max_N_elem_m1, max_M_m1, max_M_elem_m1, n_cols_m1 = _gather_cols(main)

    report = {
        "unidades": "kN, m",
        "caso": "G",
        "aplicado_por_piso": {lv: round(v, 3) for lv, v in sorted(by_floor.items())},
        "G_total_aplicado_kN": round(aplicado_total, 3),
        "G_aplicado_componente_principal_kN": round(aplicado_main, 3),
        "componente_principal": src_of_main,
        "n_apoyos_componente_principal": n_supports_main,
        "reacciones": {"sum_Rx_kN": sum_rx, "sum_Ry_kN": sum_ry, "sum_Rz_kN": sum_rz},
        "conservacion_error_kN": err,
        "n_apoyos": n_supports,
        "columnas_evaluadas": n_columns,
        "columnas_evaluadas_componente_principal": n_cols_m1,
        "vigas_flotantes_con_carga_excluidas": n_floating,
        "nota": ("Se excluyen las vigas/columnas flotantes (componentes sin apoyo) y sus cargas. "
                 "Los extremos del componente principal (edificio_1) van aparte; el resto de componentes "
                 "apoyados de edificio_2 quedan en los extremos globales."),
        "max_uz_m": max_uz,
        "max_uz_node": max_uz_node,
        "max_h_m": max_h,
        "max_h_node": max_h_node,
        "max_axial_columna_kN": max_N,
        "max_axial_elem": max_N_elem,
        "max_momento_columna_kNm": max_M,
        "max_momento_elem": max_M_elem,
        "componente_principal_extremos": {
            "max_uz_m": max_uz_m1,
            "max_uz_node": max_uz_node_m1,
            "max_h_m": max_h_m1,
            "max_h_node": max_h_node_m1,
            "max_axial_columna_kN": max_N_m1,
            "max_axial_elem": max_N_elem_m1,
            "max_momento_columna_kNm": max_M_m1,
            "max_momento_elem": max_M_elem_m1,
        },
        "convergio": res["ok"],
    }
    write_json(OUT_DIR / "part_g_gravity.json", report)
    return report


def print_gravity(report):
    print("\n" + "=" * 70)
    print("PARTE G - CASO DE CARGA PERMANENTE (carga muerta)")
    print("=" * 70)
    print("Carga muerta aplicada por piso (D*A sobre vigas):")
    for nivel, valor in report["aplicado_por_piso"].items():
        print(f"  {nivel}: G = {valor:.3f} kN")
    print(f"  TOTAL G aplicado = {report['G_total_aplicado_kN']:.3f} kN")
    print("-" * 70)
    print(f"  Reaccion Rx (suma) = {report['reacciones']['sum_Rx_kN']:.3f} kN")
    print(f"  Reaccion Ry (suma) = {report['reacciones']['sum_Ry_kN']:.3f} kN")
    print(f"  Reaccion Rz (suma) = {report['reacciones']['sum_Rz_kN']:.3f} kN")
    print(f"  Conservacion |G - Rz| = {report['conservacion_error_kN']:.3e} kN")
    print(f"  Apoyos = {report['n_apoyos']}")
    print("-" * 70)
    print(f"  Desplazamiento vertical max = {report['max_uz_m']:.6f} m (nodo {report['max_uz_node']})")
    print(f"  Desplazamiento horiz max    = {report['max_h_m']:.6f} m (nodo {report['max_h_node']})")
    print(f"  Axial compresion max columna = {report['max_axial_columna_kN']:.3f} kN (elem {report['max_axial_elem']})")
    print(f"  Momento max columna          = {report['max_momento_columna_kNm']:.3f} kN*m (elem {report['max_momento_elem']})")
    ce = report.get("componente_principal_extremos", {})
    nm = report.get("componente_principal")
    print("-" * 70)
    print(f"  Componente principal ({nm or 'estructura principal'}):")
    print(f"    G aplicado = {report.get('G_aplicado_componente_principal_kN', 0.0):.3f} kN, "
          f"apoyos = {report.get('n_apoyos_componente_principal', 0)}")
    for k, v in (("uz", ce.get("max_uz_m")), ("h", ce.get("max_h_m"))):
        pass
    print(f"    Desplazamiento vertical max = {ce.get('max_uz_m', 0.0):.6f} m (nodo {ce.get('max_uz_node')})")
    print(f"    Desplazamiento horiz max    = {ce.get('max_h_m', 0.0):.6f} m (nodo {ce.get('max_h_node')})")
    print(f"    Axial compresion max columna = {ce.get('max_axial_columna_kN', 0.0):.3f} kN (elem {ce.get('max_axial_elem')})")
    print(f"    Momento max columna          = {ce.get('max_momento_columna_kNm', 0.0):.3f} kN*m (elem {ce.get('max_momento_elem')})")


# ==================================================================
# PARTE ADICIONAL 2 - TABLAS SISMICAS POR PISO (EX/EY explicito)
# ==================================================================
def seismic_floor_tables(data, live_transfer, seismic):
    """Corre EX y EY explicitos y arma la tabla por piso con desplazamiento
    promedio, maximo/minimo nodal en la direccion del sismo y torsion rz.
    Solo se usan nodos de componentes apoyados (ver Parte G)."""
    nodes = node_map(data)
    adjacency = {node_id: set() for node_id in nodes}
    for element in data.get("elements", []):
        ni = element.get("nodeI")
        nj = element.get("nodeJ")
        if ni in nodes and nj in nodes:
            adjacency[ni].add(nj)
            adjacency[nj].add(ni)
    support_nodes = {support.get("node") for support in data.get("supports", []) if support.get("node") in nodes}
    supported = set(supported_components(adjacency, support_nodes))

    cargas_ex = {int(nid): v for nid, v in seismic["cargas_nodales_EX"].items() if int(nid) in supported}
    cargas_ey = {int(nid): v for nid, v in seismic["cargas_nodales_EY"].items() if int(nid) in supported}
    fx = sum(v.get("Fx", 0.0) for v in cargas_ex.values())
    fy = sum(v.get("Fy", 0.0) for v in cargas_ey.values())

    res = {
        "EX": run_and_extract(data, vector_loads_from_dict(cargas_ex)),
        "EY": run_and_extract(data, vector_loads_from_dict(cargas_ey)),
    }
    config = {"EX": ("X", "ux", "sum_Fx", "carga_lateral_total_EX_kN"),
              "EY": ("Y", "uy", "sum_Fy", "carga_lateral_total_EY_kN")}
    tables = {}
    for case, (label, dof, react_comp, total_key) in config.items():
        rows = {}
        for row in seismic["pisos"]:
            levels = [float(level) for level in row["niveles_agrupados_z_m"]]
            app = row["nodo_aplicacion"]
            chosen = [
                (nid, d) for nid, d in res[case]["displacements"].items()
                if nid in supported
                and any(abs(round(nodes[nid]["z"], 3) - level) < 1e-6 for level in levels)
            ]
            vals = [d[dof] for nid, d in chosen]
            rzs = [d["rz"] for nid, d in chosen]
            rows[row["piso"]] = {
                "floor_z_m": row["floor_z_m"],
                "niveles_agrupados_z_m": row["niveles_agrupados_z_m"],
                "u_prom_en_dir_m": (sum(vals) / len(vals)) if vals else 0.0,
                "u_max_nodal_m": max(vals) if vals else 0.0,
                "u_min_nodal_m": min(vals) if vals else 0.0,
                "n_nodos": len(vals),
                "rz_nodo_aplicacion_rad": res[case]["displacements"].get(app, {}).get("rz", 0.0),
                "rz_promedio_rad": (sum(rzs) / len(rzs)) if rzs else 0.0,
            }
        total = fx if case == "EX" else fy
        corte = abs(res[case]["reactions"][react_comp])
        tables[case] = {
            "direccion": label,
            "c_total_kN": total,
            "corte_basal_kN": corte,
            "error_corte_kN": abs(corte - total),
            "c_total_incluyendo_flotantes_kN": seismic[total_key],
            "max_desplazamiento_m": max((r["u_prom_en_dir_m"] for r in rows.values()), default=0.0),
            "max_u_nodal_m": max((r["u_max_nodal_m"] for r in rows.values()), default=0.0),
            "convergio": res[case]["ok"],
            "pisos": rows,
        }
    report = {"hipotesis_masa": "W_sismico = D + 0.5Q", "EX": tables["EX"], "EY": tables["EY"]}
    write_json(OUT_DIR / "part_b_sismo_tablas.json", report)
    return report


def print_seismic_tables(report):
    print("\n" + "=" * 70)
    print("PARTE B - TABLAS SISMICAS POR PISO (corrida explicita EX/EY)")
    print("=" * 70)
    for case in ("EX", "EY"):
        c = report[case]
        print(f"\n[{case}] direccion {c['direccion']}")
        print(f"  Carga lateral total F      = {c['c_total_kN']:.3f} kN")
        print(f"  Corte basal (reacciones)   = {c['corte_basal_kN']:.3f} kN")
        print(f"  Error |F - corte basal|    = {c['error_corte_kN']:.3e} kN")
        print(f"  Max desplazamiento promedio= {c['max_desplazamiento_m']:.6f} m")
        print(f"  Tabla por piso (u_prom, u_max, u_min, rz):")
        for nivel, r in c["pisos"].items():
            print(f"    {nivel}: u_prom={r['u_prom_en_dir_m']:.6f} m | "
                  f"u_max={r['u_max_nodal_m']:.6f} m | "
                  f"u_min={r['u_min_nodal_m']:.6f} m | "
                  f"nodos={r['n_nodos']} | "
                  f"rz={r['rz_nodo_aplicacion_rad']:.6e} rad")


# ==================================================================
# PARTE ADICIONAL 3 - SUPERPOSICION CON 3 COMBINACIONES C1/C2/C3
# ==================================================================
def superposition_check_multi(data, live_transfer, seismic, combinations):
    """Corre los casos base G/Q/EX/EY una sola vez y verifica superposicion
    contra la corrida explicita para cada combinacion de la lista."""
    nodes = node_map(data)
    connected = {element["nodeI"] for element in data.get("elements", [])} | {element["nodeJ"] for element in data.get("elements", [])}
    control_node = max((node for node in nodes.values() if node["id"] in connected), key=lambda n: (n["z"], n["x"] ** 2 + n["y"] ** 2))["id"]
    element_id = next(element["id"] for element in data["elements"] if element.get("type") == "viga")
    load_sets = {
        "G": dead_nodal_loads(data),
        "Q": live_load_set(live_transfer),
        "EX": vector_loads_from_dict(seismic["cargas_nodales_EX"]),
        "EY": vector_loads_from_dict(seismic["cargas_nodales_EY"]),
    }
    cases = {name: analyze_case(data, loads, control_node, element_id) for name, loads in load_sets.items()}

    report = {
        "control_node": control_node,
        "element_id": element_id,
        "combinaciones": {},
        "max_err_global": 0.0,
        "superposicion_valida": True,
    }

    for nombre, lambdas in combinations.items():
        explicit = analyze_case(data, combine_nodal_loads(load_sets, lambdas), control_node, element_id)

        predicted_disp = [0.0, 0.0, 0.0]
        predicted_react = [0.0, 0.0, 0.0]
        predicted_force = [0.0] * len(cases["G"].get("element_force_sample", []))
        for case, result in cases.items():
            factor = lambdas.get(case, 0.0)
            for i in range(3):
                predicted_disp[i] += factor * result["control_displacement_ux_uy_uz_m"][i]
                predicted_react[i] += factor * result["sum_reactions_Fx_Fy_Fz_kN"][i]
            for i, value in enumerate(result.get("element_force_sample", [])):
                predicted_force[i] += factor * value

        disp_error = [explicit["control_displacement_ux_uy_uz_m"][i] - predicted_disp[i] for i in range(3)]
        reaction_error = [explicit["sum_reactions_Fx_Fy_Fz_kN"][i] - predicted_react[i] for i in range(3)]
        force_error = [
            explicit.get("element_force_sample", [])[i] - predicted_force[i]
            for i in range(min(len(explicit.get("element_force_sample", [])), len(predicted_force)))
        ]
        scale = max(max_abs(disp_error), max_abs(reaction_error), max_abs(force_error))
        super_ok = scale < 1e-6
        report["max_err_global"] = max(report["max_err_global"], scale)
        report["superposicion_valida"] = report["superposicion_valida"] and super_ok

        report["combinaciones"][nombre] = {
            "lambdas": lambdas,
            "combinacion": "R = lambda_G G + lambda_Q Q + lambda_EX EX + lambda_EY EY",
            "errors": {
                "disp_abs_m": disp_error,
                "reaction_abs_kN": reaction_error,
                "element_force_abs": force_error,
                "max_disp_abs_m": max_abs(disp_error),
                "max_reaction_abs_kN": max_abs(reaction_error),
                "max_element_force_abs": max_abs(force_error),
            },
            "max_err_global": scale,
            "superposicion_valida": super_ok,
            "convergio_explicito": explicit["ok"],
        }
    write_json(OUT_DIR / "part_c_superposicion_3.json", report)
    return report


def print_superposition_multi(report):
    print("\n" + "=" * 70)
    print("PARTE C - SUPERPOSICION 3 COMBINACIONES (C1/C2/C3 NCh433)")
    print("=" * 70)
    print(f"Nodo de control = {report['control_node']} | Elemento de control = {report['element_id']}")
    for nombre, c in report["combinaciones"].items():
        e = c["errors"]
        print("-" * 70)
        print(f"  Combinacion {nombre}: {c['lambdas']}")
        print(f"  Max error desplazamiento = {e['max_disp_abs_m']:.3e} m")
        print(f"  Max error reaccion       = {e['max_reaction_abs_kN']:.3e} kN")
        print(f"  Max error fuerzas        = {e['max_element_force_abs']:.3e}")
        print(f"  Valida (err < 1e-6)      = {c['superposicion_valida']}")
    print("-" * 70)
    print(f"  Max error global (todas las combinaciones) = {report['max_err_global']:.3e}")
    print(f"  SUPERPOSICION LINEAL VALIDA = {report['superposicion_valida']}")


# ==================================================================
# PARTE ADICIONAL 4 - SENSIBILIDAD M-PHI (mallas 10x10/20x20/40x40)
# ==================================================================
def _fiber_stress_concrete(eps):
    if eps < 0.0:
        return 0.0
    if eps <= _FIB_EPS_C0:
        return _FIB_FC * (2.0 * eps / _FIB_EPS_C0 - (eps / _FIB_EPS_C0) ** 2)
    if eps <= _FIB_EPS_CU:
        t = (eps - _FIB_EPS_C0) / (_FIB_EPS_CU - _FIB_EPS_C0)
        return _FIB_FC * (1.0 - 0.15 * t)
    return 0.85 * _FIB_FC


def _fiber_stress_steel(eps):
    ey = _FIB_FY / _FIB_ES
    if eps >= ey:
        return _FIB_FY + _FIB_ES * _FIB_EH * (eps - ey)
    if eps <= -ey:
        return -_FIB_FY + _FIB_ES * _FIB_EH * (eps + ey)
    return _FIB_ES * eps


def _fiber_axial(curv, eps0, fibers):
    P = 0.0
    for (x, d, area, mat) in fibers:
        eps = eps0 + curv * d
        sig = _fiber_stress_concrete(eps) if mat == "concrete" else _fiber_stress_steel(eps)
        P += sig * area
    return P


def _fiber_solve_PM(curv, P_target, fibers):
    lo, hi = -0.20, 0.05
    f_lo = _fiber_axial(curv, lo, fibers) - P_target
    f_hi = _fiber_axial(curv, hi, fibers) - P_target
    if f_lo * f_hi > 0.0:
        return None, None
    for _ in range(400):
        mid = 0.5 * (lo + hi)
        f_mid = _fiber_axial(curv, mid, fibers) - P_target
        if abs(f_mid) < 1e-7:
            lo = hi = mid
            break
        if f_lo * f_mid < 0.0:
            hi = mid
        else:
            lo = mid
        if hi - lo < 1e-10:
            break
    eps0 = 0.5 * (lo + hi)
    M = 0.0
    for (x, d, area, mat) in fibers:
        eps = eps0 + curv * d
        sig = _fiber_stress_concrete(eps) if mat == "concrete" else _fiber_stress_steel(eps)
        M += sig * area * d
    return eps0, M


def _col_build_fibers(n):
    fibers = []
    dy = _B / n
    dz = _B / n
    for i in range(n):
        x = (i + 0.5) * dy
        d = _B / 2.0 - x
        for _ in range(n):
            fibers.append((x, d, dy * dz, "concrete"))
    half_bar = _B / 2.0 - _FIB_COVER
    abar = math.pi * (0.025 ** 2) / 4.0
    coords = [
        (half_bar, half_bar), (-half_bar, half_bar),
        (half_bar, -half_bar), (-half_bar, -half_bar),
        (0.0, half_bar), (0.0, -half_bar),
        (half_bar, 0.0), (-half_bar, 0.0),
    ]
    for (y, z) in coords:
        x = _B / 2.0 - y
        fibers.append((x, y, abar, "steel"))
    return fibers


def _fiber_moment_curvature(P_target, n, max_curv, num):
    fibers = _col_build_fibers(n)
    curv = []
    M = []
    for i in range(num + 1):
        k = max_curv * i / num
        _, m = _fiber_solve_PM(k, P_target, fibers)
        if m is None:
            break
        curv.append(k)
        M.append(m)
    return curv, M


def _fiber_rigidez_inicial(curv, M):
    if len(curv) < 2 or curv[1] == curv[0]:
        return 0.0
    return (M[1] - M[0]) / (curv[1] - curv[0])


def sensitivity_mphi():
    """Sensibilidad de la discretizacion M-phi de la columna 70x70 (H-30):
    mallas 10x10, 20x20 y 40x40. Mismo integrador de parte D."""
    Pn0 = 0.85 * _FIB_FC * _B * _B
    P_serv = 0.20 * Pn0

    print("\n" + "=" * 70)
    print("PARTE D2 - SENSIBILIDAD DE DISCRETIZACION M-PHI (columna 70x70 H-30)")
    print("=" * 70)
    print(f"P = 0 (flexion pura) y P = {P_serv:.0f} kN (0.2*Pn0)")

    report = {
        "unidades": "kN, m",
        "seccion_m": {"b": _B, "h": _B},
        "concreto_MPa": _FIB_FC / 1000.0,
        "Pn0_kN": Pn0,
        "mallas": {},
        "referencia": "malla 40x40",
    }

    series = {}
    for n in MALLAS_SENSIBILIDAD:
        curv0, M0 = _fiber_moment_curvature(0.0, n, 0.12, 500)
        curvS, MS = _fiber_moment_curvature(P_serv, n, 0.12, 500)
        peak0 = max(M0) if M0 else 0.0
        peakS = max(MS) if MS else 0.0
        k0 = curv0[M0.index(max(M0))] if M0 else 0.0
        kS = curvS[MS.index(max(MS))] if MS else 0.0
        r0 = _fiber_rigidez_inicial(curv0, M0)
        rS = _fiber_rigidez_inicial(curvS, MS)
        n_fib = n * n + 8

        series[n] = {"curv_P0": list(curv0), "M_P0": list(M0)}
        report["mallas"][str(n)] = {
            "n_fibras_total": n_fib,
            "Mmax_P0_kNm": peak0,
            "phi_Mmax_P0_1m": k0,
            "Mmax_Pserv_kNm": peakS,
            "phi_Mmax_Pserv_1m": kS,
            "rigidez_inicial_P0_kNm2": r0,
            "rigidez_inicial_Pserv_kNm2": rS,
        }
        print("-" * 70)
        print(f"Malla {n}x{n}  ({n_fib} fibras):")
        print(f"  P=0        : Mmax={peak0:.2f} kN-m @ phi={k0:.6f} 1/m | EI0={r0:.3e} kN-m2")
        print(f"  P={P_serv:.0f}: Mmax={peakS:.2f} kN-m @ phi={kS:.6f} 1/m | EI0={rS:.3e} kN-m2")

    ref = report["mallas"]["40"]
    diff = {}
    for n in MALLAS_SENSIBILIDAD:
        if n == 40:
            continue
        m = report["mallas"][str(n)]
        diff[str(n)] = {
            "Mmax_P0_dif_pct": (m["Mmax_P0_kNm"] - ref["Mmax_P0_kNm"]) / ref["Mmax_P0_kNm"] * 100.0,
            "Mmax_Pserv_dif_pct": (m["Mmax_Pserv_kNm"] - ref["Mmax_Pserv_kNm"]) / ref["Mmax_Pserv_kNm"] * 100.0,
            "EI0_P0_dif_pct": (m["rigidez_inicial_P0_kNm2"] - ref["rigidez_inicial_P0_kNm2"]) / ref["rigidez_inicial_P0_kNm2"] * 100.0,
        }
        print("-" * 70)
        print(f"Diferencia malla {n}x{n} vs 40x40:")
        print(f"  Mmax_P0={diff[str(n)]['Mmax_P0_dif_pct']:+.3f}% | "
              f"Mmax_Pserv={diff[str(n)]['Mmax_Pserv_dif_pct']:+.3f}% | "
              f"EI0_P0={diff[str(n)]['EI0_P0_dif_pct']:+.3f}%")
    report["diferencia_vs_40x40_pct"] = diff
    report["series"] = {str(n): series[n] for n in series}

    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(figsize=(8, 5))
        for n in MALLAS_SENSIBILIDAD:
            s = series[n]
            ax.plot(s["curv_P0"], s["M_P0"], label=f"{n}x{n}")
        ax.set_xlabel("Curvatura phi [1/m]")
        ax.set_ylabel("Momento M [kN.m]")
        ax.set_title("Sensibilidad de discretizacion M-phi (P = 0)")
        ax.legend(title="Malla de fibras")
        ax.grid(True, alpha=0.3)
        fig.tight_layout()
        png = OUT_DIR / "M_phi_sensibilidad.png"
        fig.savefig(png, dpi=150)
        plt.close(fig)
        print(f"\nGrafico guardado en {png}")
        report["grafico"] = str(png)
    except Exception as exc:
        print(f"\n[aviso] no se pudo generar grafico: {exc}")

    write_json(OUT_DIR / "part_d_sensibilidad.json", report)
    print(f"Resultados guardados en {OUT_DIR / 'part_d_sensibilidad.json'}")
    return report


# ==================================================================
# PARTE ADICIONAL 5 - CURVA P-M DEL MURO (W_DPRIME_OPENING_TO_3)
# ==================================================================
def _wall_n_barras_por_capa():
    n = 0
    pos = _WALL_COVER
    while pos <= _WALL_L - _WALL_COVER + 1e-9:
        n += 1
        pos += _WALL_S
    return n


def _wall_build_fibers():
    fibers = []
    dx = _WALL_L / 40
    dy = _WALL_T / 10
    for i in range(40):
        x = (i + 0.5) * dx
        d = _WALL_L / 2.0 - x
        for _ in range(10):
            fibers.append((x, d, dx * dy, "concrete"))
    n_layer = _wall_n_barras_por_capa()
    abar = math.pi * (_WALL_DBAR ** 2) / 4.0
    for capa_y in (-(_WALL_T / 2.0 - _WALL_COVER), (_WALL_T / 2.0 - _WALL_COVER)):
        for j in range(n_layer):
            x = _WALL_COVER + j * _WALL_S
            d = _WALL_L / 2.0 - x
            fibers.append((x, d, abar, "steel"))
    return fibers, n_layer * 2


def _wall_solve_PM(curv, P_target, fibers):
    lo, hi = -0.20, 0.05
    f_lo = _fiber_axial(curv, lo, fibers) - P_target
    f_hi = _fiber_axial(curv, hi, fibers) - P_target
    if f_lo * f_hi > 0.0:
        return None, None, None
    for _ in range(400):
        mid = 0.5 * (lo + hi)
        f_mid = _fiber_axial(curv, mid, fibers) - P_target
        if abs(f_mid) < 1e-7:
            lo = hi = mid
            break
        if f_lo * f_mid < 0.0:
            hi = mid
        else:
            lo = mid
        if hi - lo < 1e-10:
            break
    eps0 = 0.5 * (lo + hi)
    M = 0.0
    eps_max_c = 0.0
    for (x, d, area, mat) in fibers:
        eps = eps0 + curv * d
        sig = _fiber_stress_concrete(eps) if mat == "concrete" else _fiber_stress_steel(eps)
        M += sig * area * d
        if mat == "concrete" and eps > eps_max_c:
            eps_max_c = eps
    return eps0, M, eps_max_c


def _wall_moment_curvature(P_target):
    fibers, n_steel = _wall_build_fibers()
    curv = []
    M = []
    for i in range(800 + 1):
        k = 0.05 * i / 800
        _, m, ec = _wall_solve_PM(k, P_target, fibers)
        if m is None:
            break
        curv.append(k)
        M.append(m)
        if ec >= _FIB_EPS_CU - 1e-9:
            break
    return curv, M, n_steel


def _wall_interaccion(Pn0, fracs):
    """Envolvente P-M: para cada nivel de axial P se corre la M-phi y se toma
    el momento en la falla por aplastamiento del concreto (malla de curvaturas).
    Los puntos cuya M-phi no alcanza la falla se omiten."""
    points = []
    for frac in fracs:
        Px = frac * Pn0
        curv, M, _ = _wall_moment_curvature(Px)
        if not M:
            continue
        peak = max(M)
        k_peak = curv[M.index(peak)]
        points.append({"P_frac": frac, "P_kN": Px, "Mmax_kNm": peak, "phi_Mmax_1m": k_peak})
    return points


def wall_pm_curve_generic(t, L, dbar=_WALL_DBAR, spacing=_WALL_S, cover=_WALL_COVER, n_c=90):
    """Diagrama de interaccion P-M nominal de un muro rectangular t x L con dos
    capas de barras phi dbar @ spacing (misma armadura que W_DPRIME), flexion en
    el plano. Compatibilidad de deformaciones con eps_cu en la fibra extrema y
    las mismas leyes de material que la seccion de fibra (H-30, A630-420).
    Devuelve puntos {label, P_kN, M_kN_m} ordenados por P (compresion +)."""
    import numpy as np

    n_strips = 200
    dx = L / n_strips
    d_c = L / 2.0 - (np.arange(n_strips) + 0.5) * dx          # + hacia el borde comprimido
    a_c = np.full(n_strips, dx * t)
    n_layer = 0
    pos = cover
    bars = []
    while pos <= L - cover + 1e-9:
        bars.append(L / 2.0 - pos)
        pos += spacing
    abar = math.pi * dbar ** 2 / 4.0
    d_s = np.array(bars * 2)
    a_s = np.full(d_s.size, abar)
    As = a_s.sum()
    Ag = t * L
    ey = _FIB_FY / _FIB_ES

    def conc(eps):
        e = np.clip(eps, 0.0, None)
        r = e / _FIB_EPS_C0
        sig = np.where(e <= _FIB_EPS_C0, _FIB_FC * (2 * r - r ** 2),
                       np.where(e <= _FIB_EPS_CU, _FIB_FC * (1 - 0.15 * (e - _FIB_EPS_C0) / (_FIB_EPS_CU - _FIB_EPS_C0)),
                                0.85 * _FIB_FC))
        return np.where(eps > 0.0, sig, 0.0)

    def steel(eps):
        return np.where(eps >= ey, _FIB_FY + _FIB_ES * _FIB_EH * (eps - ey),
                        np.where(eps <= -ey, -_FIB_FY + _FIB_ES * _FIB_EH * (eps + ey), _FIB_ES * eps))

    points = []
    for c in np.geomspace(0.02 * L, 3.0 * L, n_c):
        eps_c = _FIB_EPS_CU * (d_c - (L / 2.0 - c)) / c
        eps_s = _FIB_EPS_CU * (d_s - (L / 2.0 - c)) / c
        sc = conc(eps_c)
        ss = steel(eps_s) - conc(eps_s)                   # descuenta el hormigon desplazado
        P = float((sc * a_c).sum() + (ss * a_s).sum())
        M = float((sc * a_c * d_c).sum() + (ss * a_s * d_s).sum())
        points.append({"label": f"c/L={c / L:.2f}", "P_kN": P, "M_kN_m": abs(M)})
    # compresion pura con la misma ley: deformacion uniforme eps_c0 (pico f'c)
    p0 = _FIB_FC * (Ag - As) + float(steel(np.array([_FIB_EPS_C0]))[0]) * As
    points = [p for p in points if p["P_kN"] < p0]
    points.append({"label": "Compresion pura", "P_kN": float(p0), "M_kN_m": 0.0})
    # traccion pura: acero fluyendo (incluye el endurecimiento que ya aparece en el barrido)
    pt = min([-_FIB_FY * As] + [p["P_kN"] for p in points])
    points.append({"label": "Traccion pura", "P_kN": pt, "M_kN_m": 0.0})
    points.sort(key=lambda p: (p["P_kN"], p["M_kN_m"]))
    return {"points": points, "As_m2": As, "Ag_m2": Ag, "n_barras": int(d_s.size)}


def wall_pm_curve():
    """Curva P-M y M-phi del muro W_DPRIME_OPENING_TO_3 (t=0.25, L=7.60, H-30)."""
    Ag = _WALL_T * _WALL_L
    Pn0 = 0.85 * _FIB_FC * Ag
    n_steel_tot = _wall_n_barras_por_capa() * 2
    abar = math.pi * (_WALL_DBAR ** 2) / 4.0
    As_tot = n_steel_tot * abar
    cuantia = As_tot / Ag

    print("\n" + "=" * 70)
    print("PARTE E2 - CURVA P-M DEL MURO (seccion de fibra)")
    print("=" * 70)
    print(f"Muro: t = {_WALL_T} m, L = {_WALL_L} m | Ag = {Ag:.3f} m2")
    print(f"Concreto H-30: f'c = {_FIB_FC / 1000.0:.0f} MPa | acero fy = {_FIB_FY / 1000.0:.0f} MPa")
    print(f"Armadura: {n_steel_tot} barras phi 12 mm (2 capas @200mm) | As = {As_tot * 1e4:.2f} cm2 | cuantia = {cuantia * 100:.2f}%")
    print(f"Pn0 = 0.85 f'c Ag = {Pn0:.0f} kN")

    curv0, M0, _ = _wall_moment_curvature(0.0)
    peak0 = max(M0) if M0 else 0.0
    k_peak0 = curv0[M0.index(peak0)] if M0 else 0.0
    print("-" * 70)
    print("CURVA M-PHI del muro (P = 0, flexion pura):")
    print(f"  n_puntos = {len(curv0)} | M_peak = {peak0:.1f} kN-m @ phi = {k_peak0:.5f} 1/m")

    # Punto de compresion pura (M -> 0): hormigon en 0.85 f'c a eps_cu + acero en fy.
    po_sq = 0.85 * _FIB_FC * Ag + _FIB_FY * As_tot
    # Punto de traccion pura (P negativo, M = 0): el hormigon no resiste traccion
    # (fisura), la capacidad axial es solo el acero fluyendo en tension.
    pt_sq = -_FIB_FY * As_tot
    fracs = [0.00, 0.05, 0.10, 0.15, 0.20, 0.30, 0.40, 0.50, 0.55, 0.60,
             0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 0.98, 1.00, 1.02, 1.04, 1.06]
    points = _wall_interaccion(Pn0, fracs)
    points.append({"P_frac": po_sq / Pn0, "P_kN": po_sq, "Mmax_kNm": 0.0, "phi_Mmax_1m": 0.0})
    points.append({"P_frac": pt_sq / Pn0, "P_kN": pt_sq, "Mmax_kNm": 0.0, "phi_Mmax_1m": 0.0})
    points = sorted(points, key=lambda x: x["P_kN"])
    print("-" * 70)
    print("ENVOLVENTE P-M del muro (P vs Mmax, domo completo):")
    print(f"  {'P/Pn0':>6s} {'P [kN]':>9s} {'Mmax [kN-m]':>11s} {'phi@Mmax':>10s}")
    for p in points:
        if p["Mmax_kNm"] <= 0.0:
            print(f"  {p['P_frac']:6.3f} {p['P_kN']:9.0f} {p['Mmax_kNm']:11.1f} {'-':>10s}")
        else:
            print(f"  {p['P_frac']:6.3f} {p['P_kN']:9.0f} {p['Mmax_kNm']:11.1f} {p['phi_Mmax_1m']:10.5f}")

    p_peak = max(points, key=lambda x: x["Mmax_kNm"])
    print("-" * 70)
    print(f"INTERPRETACION: M_max(P=0) = {peak0:.0f} kN-m (flexion pura).")
    print(f"  El maximo de la envolvente ocurre en P = {p_peak['P_kN']:.0f} kN "
          f"(P/Pn0 = {p_peak['P_frac']:.2f}), M = {p_peak['Mmax_kNm']:.0f} kN-m.")
    print(f"  En compresion pura el momento se anula en P = {po_sq:.0f} kN "
          f"(P/Pn0 = {po_sq / Pn0:.3f}), cerrando el domo.")
    print(f"  En traccion pura (M = 0) la capacidad es el acero en tension: P = {pt_sq:.0f} kN "
          f"(P/Pn0 = {pt_sq / Pn0:.3f}), cerrando el domo por el lado negativo.")

    report = {
        "unidades": "kN, m",
        "muro": "W_DPRIME_OPENING_TO_3",
        "seccion_m": {"t": _WALL_T, "L": _WALL_L},
        "concreto_MPa": _FIB_FC / 1000.0,
        "acero_MPa": _FIB_FY / 1000.0,
        "armadura": {"diametro_mm": int(_WALL_DBAR * 1000), "espaciado_mm": int(_WALL_S * 1000),
                     "n_barras_total": n_steel_tot, "As_total_m2": As_tot, "cuantia_vertical": cuantia},
        "Pn0_kN": Pn0,
        "P_compresion_pura_kN": po_sq,
        "P_traccion_pura_kN": pt_sq,
        "Mphi_P0": {"curv": [round(c, 6) for c in curv0], "M": [round(m, 1) for m in M0]},
        "interaccion_PM": points,
    }
    write_json(OUT_DIR / "part_e_wall.json", report)
    print(f"Resultados guardados en {OUT_DIR / 'part_e_wall.json'}")

    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(1, 2, figsize=(12, 5))
        ax[0].plot(curv0, M0)
        ax[0].set_xlabel("Curvatura phi [1/m]")
        ax[0].set_ylabel("Momento M [kN.m]")
        ax[0].set_title("Muro 0.25 x 7.60 m - M-phi (P = 0)")
        ax[0].grid(True, alpha=0.3)
        sorted_points = sorted(points, key=lambda p: p["P_kN"])
        Ms = [p["Mmax_kNm"] for p in sorted_points]
        Ps = [p["P_kN"] for p in sorted_points]
        ax[1].plot(Ms, Ps, "o-")
        ax[1].axhline(0, color="k", lw=0.5)
        ax[1].axvline(0, color="k", lw=0.5)
        ax[1].plot(p_peak["Mmax_kNm"], p_peak["P_kN"], "rs", label="Punto de balance (max M)")
        ax[1].set_xlabel("Mmax [kN.m]")
        ax[1].set_ylabel("Axial P [kN]")
        ax[1].set_title("Muro 0.25 x 7.60 m - Diagrama de interaccion P-M")
        ax[1].legend()
        ax[1].grid(True, alpha=0.3)
        fig.tight_layout()
        png = OUT_DIR / "P_M_wall.png"
        fig.savefig(png, dpi=150)
        plt.close(fig)
        print(f"Graficos guardados en {png}")
    except Exception as exc:
        print(f"[aviso] no se pudo generar grafico: {exc}")
    return report


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


def find_element(data, wanted_id):
    for element in data.get("elements", []):
        if id_matches(element.get("id"), wanted_id) or id_matches(element_tag(element), wanted_id) or id_matches(element.get("sourceId"), wanted_id):
            return element
    return None


def find_wall(data, wanted_id):
    for wall in data.get("walls", []):
        if id_matches(wall.get("id"), wanted_id) or id_matches(wall.get("sourceId"), wanted_id):
            return wall
    return None


def ask_load_combination():
    print("\nCaso/combinacion para fuerzas internas")
    print("  G  = carga permanente")
    print("  Q  = carga viva")
    print("  EX = sismo en X")
    print("  EY = sismo en Y")
    print("  C1 = G+0.5Q+0.3EX+0.2EY")
    print("  C2 = G+0.5Q+0.3EX-0.2EY")
    print("  C3 = G+0.5Q-0.3EX+0.2EY")
    print("  P  = personalizada")
    choice = input("Elige caso/combinacion [C1]: ").strip().upper() or "C1"
    if choice in ("G", "Q", "EX", "EY"):
        return choice, {"G": 1.0 if choice == "G" else 0.0, "Q": 1.0 if choice == "Q" else 0.0, "EX": 1.0 if choice == "EX" else 0.0, "EY": 1.0 if choice == "EY" else 0.0}
    if choice in COMBINACIONES_NCH433:
        return choice, COMBINACIONES_NCH433[choice]
    if choice == "P":
        return "PERSONALIZADA", ask_lambdas()
    print("  Opcion no reconocida. Se usa C1.")
    return "C1", COMBINACIONES_NCH433["C1"]


def component_resultant(a, b):
    sign = 1.0 if abs(a) >= abs(b) and a >= 0.0 else -1.0 if abs(a) >= abs(b) else 1.0 if b >= 0.0 else -1.0
    return sign * math.sqrt(a * a + b * b)


def section_forces_local(f, length, t):
    """Esfuerzos internos en la seccion x = t*L a partir de las 12 acciones
    LOCALES de extremo (ops.eleResponse(id, 'localForce')). Misma convencion
    que FrameForces.Evaluate en Unity: N positivo en traccion, cara I = +f[i],
    cara J = -f[j]; la carga uniforme se obtiene del equilibrio (f_i + f_j)/L,
    por lo que el momento es parabolico si la barra tiene eleLoad."""
    s = max(0.0, min(1.0, t))
    x = length * s
    qy = (f[1] + f[7]) / length
    qz = (f[2] + f[8]) / length
    return {
        "N": -f[0] * (1.0 - s) + f[6] * s,
        "Vy": f[1] - qy * x,
        "Vz": f[2] - qz * x,
        "T": f[3] * (1.0 - s) - f[9] * s,
        "My": f[4] + f[2] * x - 0.5 * qz * x * x,
        "Mz": f[5] - f[1] * x + 0.5 * qy * x * x,
    }


def force_values_at(force, element, t, length):
    """force: acciones LOCALES de extremo. (Antes se interpolaban acciones
    globales entre extremos y se sumaba una parabola inventada con
    uniformLoad; ambas cosas fueron corregidas.)"""
    v = section_forces_local(force, length, t)
    n, vy, vz, torsion, my, mz = v["N"], v["Vy"], v["Vz"], v["T"], v["My"], v["Mz"]
    return {
        "N": n,
        "Vy": vy,
        "Vz": vz,
        "V": component_resultant(vy, vz),
        "T": torsion,
        "My": my,
        "Mz": mz,
        "M": component_resultant(my, mz),
    }


def max_internal_value(force, element, length, key):
    best = None
    for i in range(101):
        t = i / 100.0
        vals = force_values_at(force, element, t, length)
        value = vals[key]
        if best is None or abs(value) > abs(best["valor"]):
            best = {"valor": value, "t": t, "x_m": t * length, "x_pct": 100.0 * t}
    return best


def format_row(name, unit, vi, vc, vj, vmax):
    return f"{name:<10} {vi:>14.3f} {vc:>14.3f} {vj:>14.3f} {vmax['valor']:>14.3f} {vmax['x_m']:>10.3f} {vmax['x_pct']:>8.1f} {unit}"


def internal_forces_report(data, live_transfer, seismic, wanted_id, combo_name, lambdas):
    element = find_element(data, wanted_id)
    if element is None:
        wall = find_wall(data, wanted_id)
        if wall is not None:
            return {
                "tipo": "muro",
                "error": "El muro existe, pero en P1L3 no esta modelado como elemento OpenSees con eleForce. Su demanda P-M se revisa en P1L4/Unity.",
                "muro": wall,
            }
        return {"error": "No se encontro el elemento solicitado.", "id_buscado": wanted_id}

    load_sets = {
        "G": dead_nodal_loads(data),
        "Q": live_load_set(live_transfer),
        "EX": vector_loads_from_dict(seismic["cargas_nodales_EX"]),
        "EY": vector_loads_from_dict(seismic["cargas_nodales_EY"]),
    }
    combined_loads = combine_nodal_loads(load_sets, lambdas)
    result = run_and_extract(data, combined_loads)
    force = result.get("element_forces_local", {}).get(element["id"])
    if not force or len(force) < 12:
        return {"error": "No se pudieron extraer fuerzas internas para este elemento.", "elemento": element}

    nodes = node_map(data)
    length = element_length(element, nodes)
    vals_i = force_values_at(force, element, 0.0, length)
    vals_c = force_values_at(force, element, 0.5, length)
    vals_j = force_values_at(force, element, 1.0, length)
    maxima = {key: max_internal_value(force, element, length, key) for key in ("N", "V", "M", "T", "Vy", "Vz", "My", "Mz")}
    return {
        "combo": combo_name,
        "lambdas": lambdas,
        "elemento": element,
        "largo_m": length,
        "ok_analisis": result["ok"],
        "valores_I": vals_i,
        "valores_centro": vals_c,
        "valores_J": vals_j,
        "maximos": maxima,
        "force_12_componentes": force,
    }


def print_internal_forces_report(report):
    if "error" in report:
        print("\nNo se pudo generar la tabla de fuerzas internas.")
        print(f"  {report['error']}")
        if report.get("tipo") == "muro":
            wall = report["muro"]
            print(f"  Muro: id={wall.get('id')} sourceId={wall.get('sourceId', '-')}, nodos {wall.get('nodeI')} - {wall.get('nodeJ')}")
        return

    element = report["elemento"]
    print("\nFuerzas internas del elemento")
    print(f"  Combo/caso       = {report['combo']}")
    print(f"  Lambdas          = G {report['lambdas'].get('G', 0):.3f}, Q {report['lambdas'].get('Q', 0):.3f}, EX {report['lambdas'].get('EX', 0):.3f}, EY {report['lambdas'].get('EY', 0):.3f}")
    print(f"  ID               = {element.get('id')} | tag = {element_tag(element)}")
    print(f"  Tipo             = {element.get('type')} | seccion = {element.get('sectionId') or element.get('seccion')}")
    print(f"  Nodos            = I {element.get('nodeI')} | J {element.get('nodeJ')}")
    print(f"  Largo            = {report['largo_m']:.3f} m")
    print("\nTabla resumida: valores en I, centro, J y maximo absoluto")
    print("Magnitud             Nodo I         Centro         Nodo J        Max abs        x [m]    x/L [%] unidad")
    print("-" * 108)
    vi = report["valores_I"]
    vc = report["valores_centro"]
    vj = report["valores_J"]
    mx = report["maximos"]
    print(format_row("Axial N", "kN", vi["N"], vc["N"], vj["N"], mx["N"]))
    print(format_row("Corte V", "kN", vi["V"], vc["V"], vj["V"], mx["V"]))
    print(format_row("Momento M", "kN*m", vi["M"], vc["M"], vj["M"], mx["M"]))
    print(format_row("Torsion T", "kN*m", vi["T"], vc["T"], vj["T"], mx["T"]))
    print("\nComponentes locales para revisar ejes")
    print("Magnitud             Nodo I         Centro         Nodo J        Max abs        x [m]    x/L [%] unidad")
    print("-" * 108)
    print(format_row("Vy", "kN", vi["Vy"], vc["Vy"], vj["Vy"], mx["Vy"]))
    print(format_row("Vz", "kN", vi["Vz"], vc["Vz"], vj["Vz"], mx["Vz"]))
    print(format_row("My", "kN*m", vi["My"], vc["My"], vj["My"], mx["My"]))
    print(format_row("Mz", "kN*m", vi["Mz"], vc["Mz"], vj["Mz"], mx["Mz"]))


def print_internal_forces_at_position(data, live_transfer, seismic, wanted_id, combo_name, lambdas):
    report = internal_forces_report(data, live_transfer, seismic, wanted_id, combo_name, lambdas)
    if "error" in report:
        print_internal_forces_report(report)
        return

    element = report["elemento"]
    length = report["largo_m"]
    print("\nConsulta puntual de fuerzas internas")
    print(f"  ID               = {element.get('id')} | tag = {element_tag(element)}")
    print(f"  Tipo             = {element.get('type')} | seccion = {element.get('sectionId') or element.get('seccion')}")
    print(f"  Nodos            = I {element.get('nodeI')} | J {element.get('nodeJ')}")
    print(f"  Largo total      = {length:.3f} m")
    x = ask_float(f"En que parte del elemento quieres evaluar x [m], entre 0 y {length:.3f}", 0.5 * length)
    if x < 0.0:
        print("  x menor que 0. Se usa x = 0.")
        x = 0.0
    if x > length:
        print(f"  x mayor que el largo. Se usa x = {length:.3f}.")
        x = length
    t = x / length if length > 0.0 else 0.0
    values = force_values_at(report["force_12_componentes"], element, t, length)

    print(f"\nValores en x = {x:.3f} m desde nodo I ({100.0 * t:.1f}% del largo)")
    print(f"  Combo/caso       = {combo_name}")
    print(f"  Lambdas          = G {lambdas.get('G', 0):.3f}, Q {lambdas.get('Q', 0):.3f}, EX {lambdas.get('EX', 0):.3f}, EY {lambdas.get('EY', 0):.3f}")
    print("\nTabla puntual")
    print("Magnitud                  Valor      Unidad")
    print("-" * 42)
    print(f"Axial N             {values['N']:>12.3f}      kN")
    print(f"Corte Vy            {values['Vy']:>12.3f}      kN")
    print(f"Corte Vz            {values['Vz']:>12.3f}      kN")
    print(f"Corte resultante V  {values['V']:>12.3f}      kN")
    print(f"Torsion T           {values['T']:>12.3f}      kN*m")
    print(f"Momento My          {values['My']:>12.3f}      kN*m")
    print(f"Momento Mz          {values['Mz']:>12.3f}      kN*m")
    print(f"Momento resultante M{values['M']:>12.3f}      kN*m")


def interactive_menu():
    data = load_model_data()
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
        print("9. Verificacion: aguanta? (demanda P-M)  (sin ID, pide lambdas)")
        print("10. Caso G numerico (carga permanente)     (sin ID)")
        print("11. Tablas sismo por piso EX/EY explicito  (sin ID)")
        print("12. Superposicion 3 combinaciones C1/C2/C3 (sin ID)")
        print("13. Sensibilidad M-phi 10x10/20x20/40x40   (sin ID)")
        print("14. Capacidad P-M del muro                 (sin ID)")
        print("15. Fuerzas internas N/V/M/T de elemento   (con ID/tag)")
        print("16. Fuerzas internas en una posicion x     (con ID/tag)")
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
        elif option == "9":
            lambdas = ask_lambdas()
            verdict = verify_building(data, live_transfer, seismic, lambdas)
            print_verification(verdict, q_q, seismic_coeff)
        elif option == "10":
            report = gravity_case_report(data)
            save_result_section("parte_G_caso_gravedad", report)
            print_gravity(report)
        elif option == "11":
            report = seismic_floor_tables(data, live_transfer, seismic)
            save_result_section("parte_B_tablas_sismo_por_piso", report)
            print_seismic_tables(report)
        elif option == "12":
            report = superposition_check_multi(data, live_transfer, seismic, COMBINACIONES_NCH433)
            save_result_section("parte_C_superposicion_3_combinaciones", report)
            print_superposition_multi(report)
        elif option == "13":
            report = sensitivity_mphi()
            save_result_section("parte_D2_sensibilidad_M_phi", report)
        elif option == "14":
            report = wall_pm_curve()
            save_result_section("parte_E2_muro_PM", report)
        elif option == "15":
            wanted_id = input("ID/tag del elemento, ej. B3002_V60/80, 359 o COL...: ").strip()
            combo_name, lambdas = ask_load_combination()
            report = internal_forces_report(data, live_transfer, seismic, wanted_id, combo_name, lambdas)
            print_internal_forces_report(report)
        elif option == "16":
            wanted_id = input("ID/tag del elemento, ej. B3002_V60/80, 359 o COL...: ").strip()
            combo_name, lambdas = ask_load_combination()
            print_internal_forces_at_position(data, live_transfer, seismic, wanted_id, combo_name, lambdas)
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
    parser.add_argument("--verifica", action="store_true", help="Corre Parte E: demanda vs capacidad P-M (aguanta?)")
    parser.add_argument("--caso-g", action="store_true", help="Corre Parte G: caso de carga permanente G numerico")
    parser.add_argument("--sismo-tablas", action="store_true", help="Genera tablas sismicas por piso EX/EY (corrida explicita)")
    parser.add_argument("--superposicion-3", action="store_true", help="Corre Parte C con las 3 combinaciones C1/C2/C3")
    parser.add_argument("--sensibilidad", action="store_true", help="Corre Parte D2: sensibilidad M-phi 10x10/20x20/40x40")
    parser.add_argument("--muro-pm", action="store_true", help="Corre Parte E2: curva P-M del muro W_DPRIME_OPENING_TO_3")
    args = parser.parse_args()

    if args.menu or len(sys.argv) == 1:
        interactive_menu()
        return

    q_q = kg_m2_to_kn_m2(args.sc_kg_m2) if args.sc_kg_m2 is not None else args.qQ
    data = load_model_data()
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

    if args.verifica:
        lambdas = {"G": args.lambdaG, "Q": args.lambdaQ, "EX": args.lambdaEX, "EY": args.lambdaEY}
        verdict = verify_building(data, live_transfer, seismic, lambdas)
        print_verification(verdict, q_q, args.coef_sismo)
        return

    if args.caso_g:
        report = gravity_case_report(data)
        save_result_section("parte_G_caso_gravedad", report)
        print_gravity(report)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.sismo_tablas:
        report = seismic_floor_tables(data, live_transfer, seismic)
        save_result_section("parte_B_tablas_sismo_por_piso", report)
        print_seismic_tables(report)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.superposicion_3:
        report = superposition_check_multi(data, live_transfer, seismic, COMBINACIONES_NCH433)
        save_result_section("parte_C_superposicion_3_combinaciones", report)
        print_superposition_multi(report)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.sensibilidad:
        report = sensitivity_mphi()
        save_result_section("parte_D2_sensibilidad_M_phi", report)
        print(f"salida = {OUTPUT_PATH}")
        return

    if args.muro_pm:
        report = wall_pm_curve()
        save_result_section("parte_E2_muro_PM", report)
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
