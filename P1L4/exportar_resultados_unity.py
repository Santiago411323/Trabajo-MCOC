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
        "materialName": "H-25 / Acero A630-420",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.70,
        "h_m": 0.70,
        "note": "Seccion rectangular 70x70 cm, H-25 (f'c=25 MPa), fy=420 MPa"
    },
    {
        "sectionId": "COL70/70_FIBER",
        "elementType": "columna",
        "materialName": "H-25 / Acero A630-420 (curva P-M de 5 puntos)",
        "fc_MPa": 25.0,
        "fy_MPa": 420.0,
        "E_MPa": 25000.0,
        "b_m": 0.70,
        "h_m": 0.70,
        "steelBars": 8,
        "barDiameter_mm": 25.0,
        "Ast_mm2": 3927.0,
        "rho_percent": 0.80,
        "Po_kN": 11978.4,
        "note": "P-M COL70/70: f'c=25 MPa, fy=420, 8 phi25, rec=52.5mm; Po=0.85f'c(Ag-Ast)+fyAst=11978 kN"
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
    raw_data = load_json(JSON_BASE)
    # Vigas partidas en uniones T/X y union edificio_1/edificio_2 (ver P1L3).
    data, connectivity_report = cvm.corregir_conectividad(raw_data)
    wall_report = None
    if cvm.MODELAR_MUROS:
        data, wall_report = cvm.agregar_muros(data)
        print(f"  Muros: {len(wall_report['muros'])} muros -> {wall_report['elementos_muro']} barras + {wall_report['brazos']} brazos rigidos")
    data_nodes = {n["id"]: n for n in data["nodes"]}
    print(f"  Conectividad: {connectivity_report['barras_partidas']} barras partidas, "
          f"{connectivity_report['nodos_nuevos_en_cruces']} nodos en cruces, "
          f"{len(connectivity_report['union_entre_edificios']['enlaces_rigidos'])} enlaces rigidos")
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
    Q = cvm.live_load_set(live_transfer)
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

    # ── Correr analisis base y por combinacion ───────────────────────
    base_results = {}
    for case_name, nodal_loads in load_sets.items():
        print(f"\nAnalizando caso base {case_name}...")
        try:
            result = cvm.run_and_extract(data, nodal_loads)
            base_results[case_name] = result
            print(f"  OK={result.get('ok', False)} | fuerzas_elem={len(result.get('element_forces', {}))}")
        except Exception as e:
            print(f"  ERROR: {e}")
            base_results[case_name] = None

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
    for combo_name, result in {**base_results, **all_results}.items():
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
    for combo_name, result in {**base_results, **all_results}.items():
        if result is None:
            continue
        # Acciones LOCALES de extremo (ops.eleResponse(id, 'localForce')).
        forces_dict = result.get("element_forces_local", {})
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
            "interpretation": f"Envolvente P-M W_DPRIME_OPENING_TO_3 (t=0.25m, L=7.60m, 2 capas phi12@200). {len(wall_points)} puntos de la envolvente.",
            "points": wall_points
        })

    # ── Demandas por muro desde el analisis ─────────────────────────
    # Cada panel del JSON apunta a las barras "muro_eq" que lo representan
    # (walls[i].analysisElements). P = compresion en la base del panel,
    # M = momento maximo en el plano del muro (eje fuerte) en el panel.
    elements_by_id = {e["id"]: e for e in data.get("elements", [])}

    def demands_for_wall(wall):
        ids = wall.get("analysisElements") or []
        if not ids:
            return []
        out = []
        for combo_name in ["C1", "C2", "C3"]:
            result = all_results.get(combo_name)
            if result is None:
                continue
            forces = result.get("element_forces_local", {})
            bottom = min(ids, key=lambda i: data_nodes[elements_by_id[i]["nodeI"]]["z"])
            p_comp = forces[bottom][0]              # compresion positiva = f_i[0]
            m_max = 0.0
            for i in ids:
                f = forces[i]
                comps = (4, 10) if elements_by_id[i].get("wallStrongAxis") == "My" else (5, 11)
                m_max = max(m_max, abs(f[comps[0]]), abs(f[comps[1]]))
            out.append({
                "combo": combo_name,
                "P_kN": round(p_comp, 2),
                "M_kN_m": round(m_max, 2),
                "note": f"{elements_by_id[bottom].get('wallName')} z={wall.get('panelZ')}: OpenSees, columna ancha {elements_by_id[bottom].get('sectionId')}",
            })
        return out

    # ── Curvas P-M de cada seccion de muro ──────────────────────────
    wall_curve_for = {}
    wall_curves = {}
    for i, wall in enumerate(data.get("walls", [])):
        ids = wall.get("analysisElements") or []
        if not ids or (wall_pm_data and str(wall.get("sourceId", "")).startswith("W_DPRIME_OPENING_TO_3")):
            continue
        e = elements_by_id[ids[0]]
        t, L = float(e["wallThickness_m"]), float(e["wallLength_m"])
        cid = f"W_{t:.2f}x{L:.2f}"
        wall_curve_for[i] = cid
        if cid not in wall_curves:
            c = cvm.wall_pm_curve_generic(t, L)
            wall_curves[cid] = {
                "sectionId": cid, "elementType": "muro", "b_m": t, "h_m": L,
                "fc_MPa": 30.0, "fy_MPa": 420.0, "steelBars": c["n_barras"], "barDiameter_mm": 12.0,
                "Ast_mm2": round(c["As_m2"] * 1e6, 1), "rho_percent": round(100 * c["As_m2"] / c["Ag_m2"], 3),
                "Po_kN": max(p["P_kN"] for p in c["points"]),
                "interpretation": f"Interaccion P-M nominal muro t={t:.2f} m, L={L:.2f} m, H-30, 2 capas phi12@200 (armadura supuesta igual a W_DPRIME), flexion en el plano; compatibilidad de deformaciones.",
                "points": [{"label": p["label"], "P_kN": round(p["P_kN"], 2), "M_kN_m": round(p["M_kN_m"], 2)} for p in c["points"]],
            }

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
        # W_DPRIME_OPENING_TO_3 usa su curva de fibra (P1L3 part_e_wall); los
        # demas muros usan la curva P-M de su propia seccion t x L.
        if bool(wall_pm_data) and str(wall.get("sourceId", "")).startswith("W_DPRIME_OPENING_TO_3"):
            curve_id = "W_DPRIME_OPENING_TO_3"
        else:
            curve_id = wall_curve_for.get(i, "")
        has_curve = bool(curve_id)
        entry = dict(wall)
        entry["id"] = i + 1
        entry["demands"] = demands_for_wall(entry)
        walls_enriched.append(entry)
        wall_registry.append({
            "index": i + 1,
            "nodeI": wall.get("nodeI"),
            "nodeJ": wall.get("nodeJ"),
            "grosor": grosor,
            "longitud": longitud,
            "bottom": wall.get("bottom", ""),
            "top": wall.get("top", ""),
            "pmSectionId": curve_id,
            "hasCurve": has_curve,
            "demands": entry["demands"]
        })

    # ── JSON de salida ───────────────────────────────────────────────
    curva_muro_n = len(wall_pm_data) if wall_pm_data else 0
    # Apoyos: declarados + anclajes automaticos que OpenSees haya necesitado.
    supports_out = [dict(sup) for sup in data.get("supports", [])]
    anchors = {}
    for result in list(base_results.values()) + list(all_results.values()):
        for anchor in (result or {}).get("auto_anchors", []):
            anchors[anchor["node"]] = anchor
    for node_id, anchor in sorted(anchors.items()):
        supports_out.append({"node": node_id, "type": "anclaje automatico P1L3 (componente sin apoyo)",
                             "ux": 1, "uy": 1, "uz": 1, "rx": 1, "ry": 1, "rz": 1})
    if anchors:
        print(f"  AVISO: {len(anchors)} anclajes automaticos (componentes sin apoyo)")

    # Cargas realmente aplicadas en el analisis (trazabilidad para Unity).
    element_loads_out = []
    for case_name in ("G", "Q"):
        for eid, w in cvm.element_loads_of(load_sets[case_name]).items():
            element_loads_out.append({"case": case_name, "id": int(eid), "wx": w[0], "wy": w[1], "wz": w[2]})

    equilibrium = {}
    for case_name, result in {**base_results, **all_results}.items():
        if result is None:
            continue
        loads = load_sets[case_name] if case_name in load_sets else cvm.combine_nodal_loads(load_sets, combos[case_name])
        applied = cvm.total_load_vector(loads, data)
        react = result["reactions_all_fixed"]
        equilibrium[case_name] = {
            "carga_total_kN": applied,
            "reaccion_total_kN": [react["sum_Fx"], react["sum_Fy"], react["sum_Fz"]],
            "desbalance_kN": [applied[0] + react["sum_Fx"], applied[1] + react["sum_Fy"], applied[2] + react["sum_Fz"]],
        }

    output = {
        "p1l4": {
            "version": "2.0",
            "elementForceCoordinates": "local",
            "analysisModel": {
                "gravedad": "D y Q como carga uniforme por viga (eleLoad -beamUniform)" if cvm.CARGAS_GRAVEDAD_DISTRIBUIDAS else "D y Q nodales qL/2",
                "sismo": "F_i = C*W_i en cada nodo del piso (W = D + 0.5Q)",
                "conectividad": {k: v for k, v in connectivity_report.items() if k != "detalle"},
                "muros": wall_report,
                "diafragmas": list(cvm.DIAPHRAGMS),
                "pesoPropio": "columnas y muros (nodal) + alma de vigas b*(h-0.15) (distribuida), 25 kN/m3",
                "torsionAccidental": "NCh433 e_k = 0.10 b_k Z_k/H dentro de EX/EY" if cvm.TORSION_ACCIDENTAL else "no",
                "anclajesAutomaticos": len(anchors),
                "equilibrio": equilibrium,
            },
            "elementLoads": element_loads_out,
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
            "Fuerzas de elemento: acciones LOCALES de extremo de OpenSees (eleResponse localForce), 12 componentes [Fx,Fy,Fz,Mx,My,Mz] en I y J; p1l4.elementForceCoordinates = local",
            "Cargas de gravedad D y Q aplicadas como carga uniforme en cada viga: momentos parabolicos en los vanos",
            "Vigas partidas en uniones T/X y union edificio_2 -> edificio_1 en x=-10 (ver p1l4.analysisModel.conectividad)",
            "Desplazamientos exportados para G, Q, EX, EY, C1, C2, C3",
            "Curvas P-M: COL70/70_FIBER (5 puntos, semana3) y W_DPRIME_OPENING_TO_3 ({} puntos, P1L3)".format(curva_muro_n),
            "Muros estructurales en el analisis: columna ancha (t x L) + brazos rigidos por nivel, base empotrada; diafragma rigido por nivel",
            "Demandas muro: P y M (eje fuerte) de las barras muro_eq del analisis OpenSees por panel y combinacion"
        ],
        "nodes": data.get("nodes", []),
        "elements": data.get("elements", []),
        "walls": walls_enriched,
        "supports": supports_out,
        "diaphragmList": data.get("diaphragmList", []),
        "slabs": data.get("slabs", []),
        "pointLoads": data.get("pointLoads", []),
        "tributaryList": data.get("tributaryList", [])
    }

    output["p1l4"]["pmCurves"].extend(wall_curves.values())
    for cid, curve in wall_curves.items():
        output["p1l4"]["sectionMaterials"].append({
            "sectionId": cid, "elementType": "muro", "materialName": "H-30 / Acero A630-420 (muro)",
            "fc_MPa": 30.0, "fy_MPa": 420.0, "E_MPa": 25000.0, "b_m": curve["b_m"], "h_m": curve["h_m"],
            "steelBars": curve["steelBars"], "barDiameter_mm": 12.0, "Ast_mm2": curve["Ast_mm2"],
            "rho_percent": curve["rho_percent"], "note": "Armadura supuesta: 2 capas phi12@200"})

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
    print(f"  Registros fuerzas_elem: {n_forces} (casos G/Q/EX/EY + C1/C2/C3)")
    for case_name, eq in equilibrium.items():
        print(f"  Equilibrio {case_name}: desbalance max = {max(abs(v) for v in eq['desbalance_kN']):.3e} kN")
    print(f"  Curvas P-M: {n_pm}")
    print("Listo.")


if __name__ == "__main__":
    main()
