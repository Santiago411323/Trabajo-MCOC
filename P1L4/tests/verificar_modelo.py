# -*- coding: utf-8 -*-
"""Verificacion del modelo corregido (P1L3 -> P1L4 -> JSON Unity).

Uso (desde la raiz del repo):
    python P1L4/tests/verificar_modelo.py              # usa openseespy
    python P1L4/tests/verificar_modelo.py --numpy      # usa mini_opensees (sin openseespy)

Comprueba, sin escribir resultados de produccion:
  1. Conectividad: una sola estructura apoyada, sin extremos libres de vigas,
     sin anclajes automaticos, nodos restringidos == apoyos declarados.
  2. Conservacion de carga: D total del JSON base == D aplicado == reaccion Rz.
  3. Equilibrio global por caso (cargas nodales + distribuidas vs reacciones).
  4. Superposicion: C1..C3 == suma ponderada de G, Q, EX, EY (fuerzas locales).
  5. El JSON exportado coincide con localForce de una corrida nueva.
  6. Evaluacion de secciones (misma formula que FrameForces.Evaluate en Unity):
     el diagrama cierra en el extremo J para todas las barras y casos.
  7. Caso patron: viga empotrada L=6 m, q=10 kN/m -> M extremos -30, centro +15.
"""
import argparse
import json
import math
import sys
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VIEWER = ROOT / "P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json"


def use_numpy_solver():
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import mini_opensees
    pkg = types.ModuleType("openseespy")
    pkg.opensees = mini_opensees
    sys.modules["openseespy"] = pkg
    sys.modules["openseespy.opensees"] = mini_opensees


class Checker:
    def __init__(self):
        self.passed = 0
        self.failed = []

    def check(self, ok, name):
        if ok:
            self.passed += 1
        else:
            self.failed.append(name)

    def near(self, a, b, name, tol):
        self.check(abs(a - b) <= tol, f"{name}: {a} != {b}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--numpy", action="store_true", help="usar mini_opensees en vez de openseespy")
    args = parser.parse_args()
    if args.numpy:
        use_numpy_solver()
    sys.path.insert(0, str(ROOT / "P1L3"))
    import carga_viva_sismo as cvm
    if cvm.ops is None:
        print("openseespy no disponible: use --numpy")
        return 2

    ck = Checker()
    exported = json.loads(VIEWER.read_text(encoding="utf-8"))
    raw = cvm.load_json(cvm.JSON_PATH)
    data, report = cvm.corregir_conectividad(raw)
    if cvm.MODELAR_MUROS:
        data, wall_report = cvm.agregar_muros(data)

    # 1) conectividad
    comps = cvm.structural_components(data)
    ck.check(all(c["apoyado"] for c in comps), "todas las componentes tienen apoyo declarado")
    degree = {}
    for e in data["elements"]:
        if e.get("type") == "brazo_rigido":
            continue  # extremos de muro unidos solo por brazos rigidos
        for k in ("nodeI", "nodeJ"):
            degree[e[k]] = degree.get(e[k], 0) + 1
    supports = {s["node"] for s in data["supports"]}
    arm_nodes = {e[k] for e in data["elements"] if e.get("type") == "brazo_rigido" for k in ("nodeI", "nodeJ")}
    free = [n for n, d in degree.items() if d == 1 and n not in supports and n not in arm_nodes]
    ck.check(not free, f"extremos libres de barras: {free}")
    ck.check(not report["union_entre_edificios"]["sin_resolver"], "union entre edificios resuelta")
    ck.check(len(exported["elements"]) == len(data["elements"]), "JSON exportado con la geometria corregida")

    # 2) casos de carga
    q = exported["Q_kN_m2"]
    live = cvm.transfer_live_load(data, q)
    seismic = cvm.build_seismic_cases(data, live, exported["seismic_coefficient"])
    load_sets = {"G": cvm.dead_nodal_loads(data), "Q": cvm.live_load_set(live),
                 "EX": cvm.vector_loads_from_dict(seismic["cargas_nodales_EX"]),
                 "EY": cvm.vector_loads_from_dict(seismic["cargas_nodales_EY"])}
    cases = dict(load_sets)
    for combo in exported["p1l4"]["combinations"]:
        cases[combo["name"]] = cvm.combine_nodal_loads(load_sets, combo)
    d_base = sum(float(e.get("deadLoad") or 0.0) for e in raw["elements"] if e.get("type") == "viga")
    g_applied = cvm.total_load_vector(load_sets["G"], data)[2]
    self_weight = sum(cvm.self_weight_nodal(data).values())
    beam_sw = sum(cvm.beam_self_weight_per_m(e) * cvm.element_length(e, cvm.node_map(data)) for e in data["elements"])
    ck.near(-g_applied, d_base + self_weight + beam_sw,
            "D aplicado == D vigas del JSON base + peso propio columnas/muros/vigas", 1e-6 * d_base)
    dup = report.get("columnas_duplicadas_eliminadas", {})
    ck.check(len(dup.get("columnas", [])) == 5, "columna duplicada E2 en (-10,-7.25) eliminada (5 tramos)")
    ck.check(all(abs(v.get("Mz", 0.0)) > 0 for v in seismic["cargas_nodales_EX"].values()) or not cvm.TORSION_ACCIDENTAL,
             "torsion accidental aplicada en EX")
    ck.near(seismic["carga_lateral_total_EX_kN"], sum(v["Fx"] for v in seismic["cargas_nodales_EX"].values()),
            "EX total == suma de fuerzas nodales", 1e-6)

    saved = {(r["combo"], r["id"]): r["f"] for r in exported["p1l4"]["elementForces"]}
    nodes = cvm.node_map(data)
    results = {}
    for name, loads in cases.items():
        res = cvm.run_and_extract(data, loads)
        results[name] = res
        ck.check(res["ok"], f"analisis {name} converge")
        ck.check(not res["auto_anchors"], f"{name}: sin anclajes automaticos")
        fixed = set(cvm.ops.getFixedNodes()) - {dia["master"] for dia in cvm.DIAPHRAGMS}
        ck.check(fixed == supports, f"{name}: nodos restringidos == apoyos declarados (sin maestros de diafragma)")
        # 3) equilibrio global
        applied = cvm.total_load_vector(loads, data)
        react = res["reactions_all_fixed"]
        for k, comp in enumerate(("sum_Fx", "sum_Fy", "sum_Fz")):
            ck.near(applied[k] + react[comp], 0.0, f"{name}: equilibrio {comp}", 1e-3)
        # 5) JSON == corrida nueva ; 6) cierre del diagrama
        for e in data["elements"]:
            f = res["element_forces_local"][e["id"]]
            ref = saved.get((name, e["id"]))
            scale = max(1.0, max(abs(v) for v in f))
            ck.check(ref is not None and max(abs(a - b) for a, b in zip(f, ref)) < 1e-6 * scale,
                     f"{name}/{e['id']}: JSON == localForce")
            length = cvm.element_length(e, nodes)
            end = cvm.section_forces_local(f, length, 1.0)
            tol = 1e-6 * max(1.0, max(abs(v) for v in f))
            ck.near(end["N"], f[6], f"{name}/{e['id']} N(L)", tol)
            ck.near(end["Vy"], -f[7], f"{name}/{e['id']} Vy(L)", tol)
            ck.near(end["Vz"], -f[8], f"{name}/{e['id']} Vz(L)", tol)
            ck.near(end["My"], -f[10], f"{name}/{e['id']} My(L)", tol)
            ck.near(end["Mz"], -f[11], f"{name}/{e['id']} Mz(L)", tol)

    # 4) superposicion
    for combo in exported["p1l4"]["combinations"]:
        for e in data["elements"]:
            pred = [sum(combo[c] * results[c]["element_forces_local"][e["id"]][k] for c in ("G", "Q", "EX", "EY"))
                    for k in range(12)]
            got = results[combo["name"]]["element_forces_local"][e["id"]]
            scale = max(1.0, max(abs(v) for v in got))
            ck.check(max(abs(a - b) for a, b in zip(pred, got)) < 1e-5 * scale, f"superposicion {combo['name']}/{e['id']}")

    # 4b) muros: toman carga y tienen demanda exportada
    if cvm.MODELAR_MUROS:
        wall_bases = {s["node"] for s in data["supports"] if "muro" in str(s.get("type"))}
        for name, k in (("G", 2), ("EX", 0), ("EY", 1)):
            total = sum(results[name]["per_node_reactions"][str(n)][k] for n in supports if str(n) in results[name]["per_node_reactions"])
            walls = sum(results[name]["per_node_reactions"][str(n)][k] for n in wall_bases)
            ck.check(abs(walls) > 0.05 * abs(total), f"{name}: los muros toman carga ({walls:.1f} de {total:.1f} kN)")
            print(f"{name}: bases de muros {walls:.1f} kN de {total:.1f} kN ({100 * walls / total:.1f} %)")
        for i, w in enumerate(exported["walls"]):
            ck.check(bool(w.get("analysisElements")), f"muro {i}: barras de analisis")
            ck.check(len(w.get("demands") or []) == 3, f"muro {i}: demandas C1-C3")

    # 7) caso patron
    ops = cvm.ops
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)
    ops.node(1, 0, 0, 0)
    ops.node(2, 6, 0, 0)
    ops.fix(1, 1, 1, 1, 1, 1, 1)
    ops.fix(2, 0, 1, 1, 1, 1, 1)
    ops.geomTransf("Linear", 1, 0, 0, 1)
    ops.element("elasticBeamColumn", 1, 1, 2, .48, 25e6, 25e6 / 2.4, .04, .0256, .0144, 1)
    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)
    ops.eleLoad("-ele", 1, "-type", "-beamUniform", 0, -10, 0)
    for cmd, arg in (("system", "BandGeneral"), ("numberer", "RCM"), ("constraints", "Plain"),
                     ("algorithm", "Linear"), ("analysis", "Static")):
        getattr(ops, cmd)(arg)
    ops.integrator("LoadControl", 1.0)
    ops.analyze(1)
    f = ops.eleResponse(1, "localForce")
    for t, expected in ((0.0, -30.0), (0.5, 15.0), (1.0, -30.0)):
        ck.near(cvm.section_forces_local(f, 6.0, t)["My"], expected, f"caso patron My({t})", 1e-6)
    ops.wipe()

    print(f"Conectividad: {report['barras_partidas']} barras partidas, {report['nodos_nuevos_en_cruces']} nodos en cruces, "
          f"{len(report['union_entre_edificios']['acciones'])} uniones E2->E1")
    print(f"D vigas base = {d_base:.3f} kN + peso propio = {self_weight + beam_sw:.3f} kN | reaccion G = {results['G']['reactions_all_fixed']['sum_Fz']:.3f} kN")
    if ck.failed:
        print(f"FALLA: {len(ck.failed)} comprobaciones (de {ck.passed + len(ck.failed)})")
        for name in ck.failed[:20]:
            print("  -", name)
        return 1
    print(f"PASS: {ck.passed} comprobaciones")
    return 0


if __name__ == "__main__":
    sys.exit(main())
