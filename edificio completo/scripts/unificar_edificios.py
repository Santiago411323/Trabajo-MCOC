#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Unifica el edificio 1 y el edificio 2 en un unico JSON (formato Unity del
viewer del edificio 1) que los muestra conectados como una sola pieza continua.

Conexion (criterio del usuario, Semana 2 MCOC):
  - El edificio 2 se coloca a la IZQUIERDA del edificio 1.
  - Su cara X+ (donde estan los ascensores del edificio 2) se pega a la cara
    X- del edificio 1 (opuesta al voladizo X+, donde estan los ascensores del
    edificio 1). Ambos nucleos de ascensores quedan contiguos.
  - Los pisos se alinean por el piso 1 (Z=0) de ambos edificios.

Salidas:
  - resultados/estructura_completo_unity.json
  - unity_visualizador/Assets/Resources/estructura_completo_unity.json  (si el viewer existe)
"""

import json
import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
COMPLETO_DIR = os.path.dirname(BASE_DIR)                      # "edificio completo"
RESULT_DIR = os.path.join(COMPLETO_DIR, "resultados")

# Rutas a los JSON de origen (relativos a la raiz del repo)
REPO_DIR = os.path.normpath(os.path.join(COMPLETO_DIR, ".."))
EDIFICIO1_JSON = os.path.join(
    REPO_DIR, "edificio_ingenieria_uandes", "project", "edificio 1",
    "unity_visualizador", "Assets", "Resources", "estructura_edificio1_unity.json")
EDIFICIO2_JSON = os.path.join(
    REPO_DIR, "edificio_2", "unity_visualizador", "Assets", "Resources",
    "estructura_edificio_ingenieria_unity.json")

OUT_JSON = os.path.join(RESULT_DIR, "estructura_completo_unity.json")
UNITY_JSON_NAME = "estructura_completo_unity.json"

# ----------------------------------------------------------------------
# Parametros de la conexion (ajustables)
# ----------------------------------------------------------------------
# Contacto: columna derecha del edificio 2 (X=31.475) se alinea exacto en
# X=-10, justo al lado del edificio 1.
ED1_CARA_X = -10.0
ED2_X_COL_MAX = 31.475
OFFSET_X = ED1_CARA_X - ED2_X_COL_MAX            # -41.475

# Mover el edificio 2 en Y: -7.25 para que su borde de losa bajo (Y=0 en la cara
# de contacto) coincida con el borde bajo del edificio 1 (Y=-7.25), haciendo
# continuo el "lado izquierdo" (borde de Y baja) de la union.
OFFSET_Y = -7.25

# El edificio 1 se deja en su posicion original en Y (sin centrado).
OFFSET_Y_E1 = 0.0

# Alineacion vertical: el cielo mas alto del edificio 2 (CIELO_4=11.83) se sube
# para que coincida con el techo del edificio 1 (Z=16.0). Shift = 16.0 - 11.83 = 4.17.
OFFSET_Z = 4.17

# Offset para remapear los IDs de nodos del edificio 2 (evitar colisiones con
# el edificio 1). El edificio 1 usa ids pequenos; usamos base alta.
ID_OFFSET = 100000


def cargar_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def transformar_nodo(n):
    return {
        "id": n["id"],
        "x": round(n["x"] + OFFSET_X, 6),
        "y": round(n["y"] + OFFSET_Y, 6),
        "z": round(n["z"] + OFFSET_Z, 6),
    }


def convertir_slabs(e2):
    """Convierte los rigidDiaphragms del edificio 2 a slabs (formato e1)."""
    slabs = []
    for d in e2.get("rigidDiaphragms", []):
        slabs.append({
            "id": d["id"],
            "nivel": d.get("level", ""),
            "x0": round(d["x1"] + OFFSET_X, 6),
            "y0": round(d["y1"] + OFFSET_Y, 6),
            "x1": round(d["x2"] + OFFSET_X, 6),
            "y1": round(d["y2"] + OFFSET_Y, 6),
            "z": round(d.get("z", 0.0) + OFFSET_Z, 6),
        })
    return slabs


def transformar_elementos(e2_elements, id_map):
    out = []
    for i, el in enumerate(e2_elements):
        out.append({
            "id": el["id"],
            "type": el.get("type", "viga"),
            "nodeI": id_map[el["nodeI"]],
            "nodeJ": id_map[el["nodeJ"]],
            "uniformLoad": el.get("uniformLoad", 0.0),
            "axialI": el.get("axialI", 0.0),
            "axialJ": el.get("axialJ", 0.0),
            "shearI": el.get("shearI", 0.0),
            "shearJ": el.get("shearJ", 0.0),
            "momentI": el.get("momentI", 0.0),
            "momentJ": el.get("momentJ", 0.0),
            "piso": el.get("piso", ""),
            "areaTributaria": el.get("tributaryArea", 0.0),
            "cargaTributaria": el.get("factoredLoad12D16L", 0.0),
        })
    return out


def build_estructura_completa():
    e1 = cargar_json(EDIFICIO1_JSON)
    e2 = cargar_json(EDIFICIO2_JSON)

    # --- Nodos: edificio 1 (desplazado en Y para centrado) + edificio 2 (transformado) ---
    nodos = []
    for n in e1.get("nodes", []):
        nn = dict(n)
        nn["y"] = round(n["y"] + OFFSET_Y_E1, 6)
        nodos.append(nn)
    prox_id = max((n["id"] for n in nodos), default=0) + 1
    id_map = {}
    e2_nodes_sorted = sorted(e2.get("nodes", []), key=lambda n: n["id"])
    for n in e2_nodes_sorted:
        new_id = prox_id
        id_map[n["id"]] = new_id
        nodos.append({
            "id": new_id,
            "x": round(n["x"] + OFFSET_X, 6),
            "y": round(n["y"] + OFFSET_Y, 6),
            "z": round(n["z"] + OFFSET_Z, 6),
        })
        prox_id += 1

    # --- Elementos: edificio 1 + edificio 2 (remap ids) ---
    elementos = list(e1.get("elements", []))
    id_e = max((el["id"] for el in elementos), default=0)
    e2_elems = []
    for el in e2.get("elements", []):
        id_e += 1
        e2_elems.append({
            "id": id_e,
            "type": el.get("type", "viga"),
            "nodeI": id_map[el["nodeI"]],
            "nodeJ": id_map[el["nodeJ"]],
            "uniformLoad": el.get("uniformLoad", 0.0),
            "axialI": el.get("axialI", 0.0),
            "axialJ": el.get("axialJ", 0.0),
            "shearI": el.get("shearI", 0.0),
            "shearJ": el.get("shearJ", 0.0),
            "momentI": el.get("momentI", 0.0),
            "momentJ": el.get("momentJ", 0.0),
            "piso": "",
            "areaTributaria": el.get("tributaryArea", 0.0),
            "cargaTributaria": el.get("factoredLoad12D16L", 0.0),
        })
    elementos.extend(e2_elems)

    # --- Slabs: losas del edificio 1 (desplazadas en Y) + diafragmas del edificio 2 ---
    slabs = []
    for s in e1.get("slabs", []):
        ss = dict(s)
        ss["y0"] = round(s["y0"] + OFFSET_Y_E1, 6)
        ss["y1"] = round(s["y1"] + OFFSET_Y_E1, 6)
        slabs.append(ss)
    slabs.extend(convertir_slabs(e2))

    # --- Walls: solo del edificio 1 (el edificio 2 no tiene field walls) ---
    walls = list(e1.get("walls", []))
    for w in walls:
        if w.get("nodeI") in id_map:
            w["nodeI"] = id_map[w["nodeI"]]
        if w.get("nodeJ") in id_map:
            w["nodeJ"] = id_map[w["nodeJ"]]

    # --- Supports ---
    supports = list(e1.get("supports", []))
    for s in e2.get("supports", []):
        sp = dict(s)
        sp["node"] = id_map[s["node"]]
        supports.append(sp)

    # --- Point loads (cargas puntuales) ---
    point_loads = list(e1.get("pointLoads", []))
    for pl in e2.get("pointLoads", []):
        p = dict(pl)
        p["node"] = id_map[pl["node"]]
        point_loads.append(p)

    # --- Tributary floors (info de areas por piso, edificio 1) ---
    tributary = list(e1.get("tributaryList", []))

    estructura = {
        "units": e1.get("units", "kN-m"),
        "q_G": e1.get("q_G", 5.1),
        "nodes": nodos,
        "elements": elementos,
        "walls": walls,
        "supports": supports,
        "diaphragms": e1.get("diaphragms", []),
        "diaphragmList": e1.get("diaphragmList", []),
        "slabs": slabs,
        "tributaryAreasByFloor": e1.get("tributaryAreasByFloor", {}),
        "tributaryList": tributary,
        "localAxes": e1.get("localAxes", []),
        "pointLoads": point_loads,
        "statistics": (
            e1.get("statistics", {})
            if isinstance(e1.get("statistics"), dict)
            else {"buildings": {"edificio1": len(e1["nodes"]), "edificio2": len(e2["nodes"])}}
        ),
    }
    return estructura


def main():
    estructura = build_estructura_completa()
    os.makedirs(RESULT_DIR, exist_ok=True)

    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(estructura, f, ensure_ascii=False, indent=2)
    print(f"JSON unificado: {OUT_JSON}")

    # Copiar al viewer Unity del edificio completo (si existe)
    unity_resources = os.path.join(COMPLETO_DIR, "unity_visualizador", "Assets", "Resources")
    if os.path.isdir(unity_resources):
        unity_path = os.path.join(unity_resources, UNITY_JSON_NAME)
        with open(unity_path, "w", encoding="utf-8") as f:
            json.dump(estructura, f, ensure_ascii=False, indent=2)
        print(f"Exportado Unity: {unity_path}")
    else:
        print(f"(no hay viewer Unity en {unity_resources}; JSON en resultados/)")

    print(f"\nResumen:")
    print(f"  Nodos      : {len(estructura['nodes'])}  (e1: {len([n for n in estructura['nodes'] if n['id']<=300])}, e2: {len([n for n in estructura['nodes'] if n['id']>300])})")
    print(f"  Elementos  : {len(estructura['elements'])}")
    print(f"  Losas/slabs: {len(estructura['slabs'])}")
    print(f"  Muros      : {len(estructura['walls'])}")
    print(f"  Apoyos     : {len(estructura['supports'])}")
    xs = [n["x"] for n in estructura["nodes"]]
    ys = [n["y"] for n in estructura["nodes"]]
    print(f"  Rango X    : [{min(xs):.3f}, {max(xs):.3f}]")
    print(f"  Rango Y    : [{min(ys):.3f}, {max(ys):.3f}]")


if __name__ == "__main__":
    main()
