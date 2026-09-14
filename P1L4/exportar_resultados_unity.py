#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Exporta resultados enriquecidos para el visualizador Unity P1L4.

Genera un JSON estructurado a partir de los resultados de OpenSeesPy que incluye:
- Geometria original del edificio completo
- Desplazamientos por combinacion (para deformada)
- Fuerzas internas por combinacion (N, Vy, Vz, T, My, Mz) por elemento
- Curvas P-M para columna (COL70/70_FIBER) y muro (W_DPRIME_OPENING_TO_3)
- Puntos de demanda por combinacion
- Materiales por seccion
- Combinaciones NCh433 (C1, C2, C3)

Requiere: openseespy, matplotlib (opcional).

Uso:
  python P1L4/exportar_resultados_unity.py
  python P1L4/exportar_resultados_unity.py --q-kg-m2 500 --sc 0.20

El JSON se escribe en:
  P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json
"""

import sys
import os
import json
from pathlib import Path

# ── Configuracion de rutas ──────────────────────────────────────────
BASE_DIR = Path(__file__).resolve().parent
ROOT_DIR = BASE_DIR.parent
P1L3_DIR = ROOT_DIR / "P1L3"
P1L2_RESOURCES = ROOT_DIR / "P1L2" / "unity_visualizador" / "Assets" / "Resources"
JSON_BASE = P1L2_RESOURCES / "estructura_completo_unity.json"
JSON_OUT = BASE_DIR / "unity_visualizador" / "Assets" / "Resources" / "estructura_p1l4_unity.json"

# Metadata de materiales para las secciones
SECTION_MATERIALS = [
    {
        "sectionId": "COL70/70",
        "elementType": "columna",
        "materialName": "H-30 / Acero A630-420",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.70,
        "h_m": 0.70,
        "note": "Seccion rectangular 70x70 cm, H-30, fy=420 MPa"
    },
    {
        "sectionId": "COL70/70_FIBER",
        "elementType": "columna",
        "materialName": "H-30 / Acero A630-420 (analisis fibra)",
        "fc_MPa": 30.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.70,
        "h_m": 0.70,
        "steelBars": 8,
        "barDiameter_mm": 25.0,
        "Ast_mm2": 3927.0,
        "rho_percent": 0.80,
        "Po_kN": 11978.4,
        "note": "Analisis fibra P-M: H-30, fy=420, 8 phi25, rec=52.5mm"
    },
    {
        "sectionId": "W_DPRIME_OPENING_TO_3",
        "elementType": "muro",
        "materialName": "H-30 / Acero A630-420 (muro con abertura)",
        "fc_MPa": 30.0,
        "fy_MPa": 420.0,
        "E_MPa": 30000.0,
        "b_m": 0.25,
        "h_m": 7.60,
        "steelBars": 76,
        "barDiameter_mm": 12.0,
        "As_total_mm2": 8595.4,
        "rho_percent": 0.45,
        "Pn0_kN": 48450.0,
        "note": "Muro D-PRIME-OPENING-TO-3, t=0.25, L=7.60, 2 capas phi12@200"
    },
    {
        "sectionId": "V60/80",
        "elementType": "viga",
        "materialName": "H-25 / Acero A630-420 (viga)",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.60,
        "h_m": 0.80,
        "note": "Viga 60x80 cm"
    },
    {
        "sectionId": "V40/80",
        "elementType": "viga",
        "materialName": "H-25 / Acero A630-420 (viga)",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.40,
        "h_m": 0.80,
        "note": "Viga 40x80 cm"
    },
    {
        "sectionId": "V30/80",
        "elementType": "viga",
        "materialName": "H-25 / Acero A630-420 (viga)",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.30,
        "h_m": 0.80,
        "note": "Viga 30x80 cm"
    },
    {
        "sectionId": "V30/45",
        "elementType": "viga",
        "materialName": "H-25 / Acero A630-420 (viga)",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.30,
        "h_m": 0.45,
        "note": "Viga 30x45 cm"
    }
]


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)


def main():
    # ── Agregar P1L3 al path para reutilizar funciones ──────────────
    p1l3_str = str(P1L3_DIR)
    if p1l3_str not in sys.path:
        sys.path.insert(0, p1l3_str)

    try:
        import carga_viva_sismo as cvm
    except ImportError as e:
        print(f"ERROR: No se pudo importar carga_viva_sismo de P1L3: {e}")
        print(f"  Asegurate de que {p1l3_str}/carga_viva_sismo.py existe.")
        sys.exit(1)

    if cvm.ops is None:
        print("ERROR: openseespy no disponible. Instala con: pip install openseespy")
        sys.exit(1)

    # ── Parametros ──────────────────────────────────────────────────
    import argparse
    parser = argparse.ArgumentParser(description="Exportar resultados enriquecidos para Unity P1L4")
    parser.add_argument("--q-kg-m2", type=float, default=500.0,
                        help="Carga viva Q en kg/m2 (default: 500)")
    parser.add_argument("--sc", type=float, default=cvm.DEFAULT_SEISMIC_COEFF,
                        help="Coeficiente sismico (default: 0.20)")
    args = parser.parse_args()

    q_Q = cvm.kg_m2_to_kn_m2(args.q_kg_m2)
    sc = args.sc
    print(f"Parametros: Q={q_Q:.3f} kN/m2 ({args.q_kg_m2:.0f} kg/m2), Coef. sismico={sc}")

    # ── Cargar datos base ───────────────────────────────────────────
    print("Cargando estructura base...")
    data = load_json(JSON_BASE)
    q_g = float(data.get("q_G", 6.227))
    print(f"  Nodos: {len(data.get('nodes', []))}")
    print(f"  Elementos: {len(data.get('elements', []))}")
    print(f"  Muros: {len(data.get('walls', []))}")
    print(f"  Apoyos: {len(data.get('supports', []))}")
    print(f"  q_G = {q_g:.4f} kN/m2")

    # ── Cargar curva P-M del muro (part_e_wall.json) ───────────────
    wall_pm_data = None
    wall_pm_path = ROOT_DIR / "P1L3" / "resultados" / "part_e_wall.json"
    if wall_pm_path.exists():
        print("Cargando curva P-M del muro...")
        wall_pm_full = load_json(wall_pm_path)
        wall_pm_data = wall_pm_full.get("interaccion_PM", [])
        print(f"  Puntos P-M muro: {len(wall_pm_data)}")
    else:
        print(f"  AVISO: No se encontro {wall_pm_path}, se omite curva P-M muro.")

    # ── Cargar curva P-M columna (semana3_resultados_unity.json) ────
    col_pm_path = P1L2_RESOURCES / "semana3_resultados_unity.json"
    col_pm_data = None
    if col_pm_path.exists():
        print("Cargando curva P-M columna COL70/70...")
        col_pm_full = load_json(col_pm_path)
        col_pm_data = col_pm_full.get("pmPoints", [])
        print(f"  Puntos P-M columna: {len(col_pm_data)}")
    else:
        print(f"  AVISO: No se encontro {col_pm_path}, se omite curva P-M columna.")

    # ── Construir casos de carga ─────────────────────────────────────
    print("Construyendo casos de carga G, Q, EX, EY...")
    live_transfer = cvm.transfer_live_load(data, q_Q)
    seismic = cvm.build_seismic_cases(data, live_transfer, sc)

    G = cvm.dead_nodal_loads(data)
    Q = cvm.vector_loads_from_dict(live_transfer["cargas_nodales_Q"])
    EX = cvm.vector_loads_from_dict(seismic["cargas_nodales_EX"])
    EY = cvm.vector_loads_from_dict(seismic["cargas_nodales_EY"])

    load_sets = {"G": G, "Q": Q, "EX": EX, "EY": EY}

    # ── Combinaciones NCh433 ─────────────────────────────────────────
    combos = {
        "C1": {"G": 1.00, "Q": 0.50, "EX": 0.30, "EY": 0.20},
        "C2": {"G": 1.00, "Q": 0.50, "EX": 0.30, "EY": -0.20},
        "C3": {"G": 1.00, "Q": 0.50, "EX": -0.30, "EY": 0.20},
    }

    combo_labels = {
        "C1": "C1: G+0.5Q+0.3EX+0.2EY",
        "C2": "C2: G+0.5Q+0.3EX-0.2EY",
        "C3": "C3: G+0.5Q-0.3EX+0.2EY",
    }

    # ── Correr analisis por combinacion ──────────────────────────────
    all_results = {}
    for combo_name, lambdas in combos.items():
        print(f"\nAnalizando combinacion {combo_name}...")
        combined_loads = cvm.combine_nodal_loads(load_sets, lambdas)
        try:
            result = cvm.run_and_extract(data, combined_loads)
            ok = result.get("ok", False)
            all_results[combo_name] = result
            disp = result.get("displacements", {})
            forces = result.get("element_forces", {})
            n_disp = len(disp)
            n_forces = len(forces)
            print(f"  OK={ok} | desplazamientos={n_disp} | fuerzas_elem={n_forces}")
        except Exception as e:
            print(f"  ERROR: {e}")
            all_results[combo_name] = None

    # ── Empaquetar desplazamientos ───────────────────────────────────
    print("\nEmpaquetando resultados...")
    displacements_flat = []
    for combo_name, result in all_results.items():
        if result is None:
            continue
        disp_dict = result.get("displacements", {})
        for node_id, d in disp_dict.items():
            displacements_flat.append({
                "combo": combo_name,
                "node": int(node_id),
                "ux": d.get("ux", 0.0),
                "uy": d.get("uy", 0.0),
                "uz": d.get("uz", 0.0),
                "rx": d.get("rx", 0.0),
                "ry": d.get("ry", 0.0),
                "rz": d.get("rz", 0.0),
            })

    # ── Empaquetar fuerzas por elemento ──────────────────────────────
    element_forces_flat = []
    for combo_name, result in all_results.items():
        if result is None:
            continue
        forces_dict = result.get("element_forces", {})
        for elem_id, f in forces_dict.items():
            element_forces_flat.append({
                "combo": combo_name,
                "id": int(elem_id),
                "f": [float(v) for v in f[:12]] if len(f) >= 12 else [float(v) for v in f] + [0.0] * (12 - len(f))
            })

    # ── Construir curvas P-M ─────────────────────────────────────────
    pm_curves = []

    if col_pm_data:
        pm_curves.append({
            "sectionId": "COL70/70_FIBER",
            "elementType": "columna",
            "b_m": 0.70,
            "h_m": 0.70,
            "fc_MPa": 25.0,
            "fy_MPa": 420.0,
            "steelBars": 8,
            "barDiameter_mm": 25.0,
            "Ast_mm2": 3927.0,
            "rho_percent": 0.80,
            "Po_kN": 11978.4,
            "interpretation": "Diagrama P-M COL70/70 (5 puntos manuales: compresion pura, balance, falla ductil, flexion pura, traccion pura).",
            "points": [{"label": p["label"], "P_kN": p["P_kN"], "M_kN_m": p["M_kN_m"]} for p in col_pm_data]
        })

    if wall_pm_data:
        wall_points = [{"label": f'P/Pn0={p["P_frac"]:.2f}', "P_kN": p["P_kN"], "M_kN_m": p["Mmax_kNm"]} for p in wall_pm_data]
        pm_curves.append({
            "sectionId": "W_DPRIME_OPENING_TO_3",
            "elementType": "muro",
            "b_m": 0.25,
            "h_m": 7.60,
            "fc_MPa": 30.0,
            "fy_MPa": 420.0,
            "steelBars": 76,
            "barDiameter_mm": 12.0,
            "Ast_mm2": 8595.4,
            "rho_percent": 0.45,
            "Po_kN": 48450.0,
            "interpretation": "Envolvente P-M W_DPRIME_OPENING_TO_3 (t=0.25m, L=7.60m, 2 capas phi12@200). 23 puntos de la envolvente.",
            "points": wall_points
        })

    # ── Calcular demandas muro ───────────────────────────────────────
    wall_demands = []
    if wall_pm_data and data.get("elements"):
        nodes_map = {}
        for n in data.get("nodes", []):
            nodes_map[n["id"]] = (n["x"], n["y"], n["z"])

        # Estimacion de tributaria del muro
        wall_trib_area = 7.60 * 3.0
        n_levels = 5
        q_q_val = q_Q

        for combo_name, lambdas in combos.items():
            p_wall = wall_trib_area * (lambdas.get("G", 0) * q_g + lambdas.get("Q", 0) * q_q_val) * n_levels

            # Momento estimado por sismo (heuristico: del corte basal proporcional)
            v_base_x = seismic.get("corte_basal_EX_kN", 0.0) * abs(lambdas.get("EX", 0))
            v_base_y = seismic.get("corte_basal_EY_kN", 0.0) * abs(lambdas.get("EY", 0))
            h_eff = 15.0
            m_wall = (v_base_x + v_base_y) * h_eff * 0.05

            wall_demands.append({
                "combo": combo_name,
                "P_kN": round(p_wall, 2),
                "M_kN_m": round(m_wall, 2),
                "note": f"Tributaria estimada: {wall_trib_area:.1f} m2 x {n_levels} niveles. V_sismo x Heff x 5%."
            })

    if pm_curves and len(pm_curves) > 1 and wall_demands:
        pm_curves[1]["demands"] = wall_demands

    # ── Empaquetar combinaciones ─────────────────────────────────────
    combos_list = []
    for name in ["C1", "C2", "C3"]:
        combos_list.append({
            "name": name,
            "label": combo_labels[name],
            "G": combos[name].get("G", 0),
            "Q": combos[name].get("Q", 0),
            "EX": combos[name].get("EX", 0),
            "EY": combos[name].get("EY", 0)
        })

    # ── Empaquetar metadatos del muro analizado ──────────────────────
    walls_enriched = []
    wall_registry = []
    for i, wall in enumerate(data.get("walls", [])):
        grosor = float(wall.get("grosor", 0.0))
        longitud = float(wall.get("longitud", 0.0))
        has_curve = (grosor >= 0.25)
        entry = dict(wall)
        entry["id"] = i + 1
        walls_enriched.append(entry)
        wall_registry.append({
            "index": i,
            "nodeI": wall.get("nodeI"),
            "nodeJ": wall.get("nodeJ"),
            "grosor": grosor,
            "longitud": longitud,
            "bottom": wall.get("bottom", ""),
            "top": wall.get("top", ""),
            "pmSectionId": "W_DPRIME_OPENING_TO_3" if has_curve else "",
            "hasCurve": has_curve
        })

    # ── JSON de salida ───────────────────────────────────────────────
    output = {
        "p1l4": {
            "version": "1.0",
            "combinations": combos_list,
            "displacements": displacements_flat,
            "elementForces": element_forces_flat,
            "pmCurves": pm_curves,
            "sectionMaterials": SECTION_MATERIALS,
            "wallRegistry": wall_registry
        },
        "units": data.get("units", "m, kN, kN*m"),
        "q_G": data.get("q_G", q_g),
        "seismic_coefficient": sc,
        "Q_kN_m2": q_Q,
        "notes": [
            "JSON enriquecido para Unity P1L4",
            "Desplazamientos y fuerzas internas de analisis estatico lineal OpenSees",
            "Fuerzas internas en coordenadas locales del elemento (12 componentes: N, Vy, Vz, T, My, Mz x2 extremos)",
            "Curvas P-M: COL70/70_FIBER (5 puntos, semana3) y W_DPRIME_OPENING_TO_3 (23 puntos, P1L3)",
            "Demandas muro: estimadas por tributaria + sismo (hipotesis documentadas)"
        ],
        "nodes": data.get("nodes", []),
        "elements": data.get("elements", []),
        "walls": walls_enriched,
        "supports": data.get("supports", []),
        "diaphragmList": data.get("diaphragmList", []),
        "slabs": data.get("slabs", []),
        "pointLoads": data.get("pointLoads", []),
        "tributaryList": data.get("tributaryList", [])
    }

    # ── Guardar ──────────────────────────────────────────────────────
    write_json(JSON_OUT, output)
    n_nodes = len(data.get("nodes", []))
    n_elements = len(data.get("elements", []))
    n_combos = len(combos_list)
    n_disp = len(displacements_flat)
    n_forces = len(element_forces_flat)
    n_pm = len(pm_curves)
    print(f"\nJSON enriquecido guardado en: {JSON_OUT}")
    print(f"  Nodos: {n_nodes}")
    print(f"  Elementos: {n_elements}")
    print(f"  Combinaciones: {n_combos}")
    print(f"  Registros desplazamientos: {n_disp} ({n_disp // max(n_nodes, 1)} por nodo)")
    print(f"  Registros fuerzas_elem: {n_forces} ({n_forces // max(n_combos, 1)} por combo)")
    print(f"  Curvas P-M: {n_pm}")
    print("Listo.")


if __name__ == "__main__":
    main()
