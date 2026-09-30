#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Reportes de consola que leen exactamente el archivo consumido por Unity.

Este modulo es deliberadamente de solo lectura. No vuelve a ejecutar OpenSees ni
reescribe resultados: el menu de P1L3 funciona como auditor del contrato P1L4.
"""

import json
import math
from pathlib import Path


BASE_CASES = ("G", "Q", "EX", "EY")
NAMED_CASES = BASE_CASES + ("C1", "C2", "C3")


def _mag(values):
    return math.sqrt(sum(float(value) ** 2 for value in values))


def _fmt(value, decimals=3):
    return f"{float(value):.{decimals}f}"


class UnityResultsValidator:
    """Indice de resultados equivalente a ``UnityData`` del visualizador."""

    def __init__(self, unity_json_path, slab_json_path=None):
        self.path = Path(unity_json_path)
        if not self.path.exists():
            raise FileNotFoundError(f"No existe el JSON que consume Unity: {self.path}")
        self.data = json.loads(self.path.read_text(encoding="utf-8"))
        self.p1l4 = self.data.get("p1l4") or {}
        if self.p1l4.get("elementForceCoordinates") != "local":
            raise RuntimeError("Unity requiere fuerzas de extremo en coordenadas locales.")

        self.nodes = {int(row["id"]): row for row in self.data.get("nodes", [])}
        self.elements = {int(row["id"]): row for row in self.data.get("elements", [])}
        self.forces = {}
        for row in self.p1l4.get("elementForces", []):
            self.forces.setdefault(str(row["combo"]), {})[int(row["id"])] = [float(v) for v in row["f"]]
        self.displacements = {}
        for row in self.p1l4.get("displacements", []):
            self.displacements.setdefault(str(row["combo"]), {})[int(row["node"])] = row
        self.element_loads = {}
        for row in self.p1l4.get("elementLoads", []):
            self.element_loads.setdefault(str(row["case"]), {})[int(row["id"])] = row
        self.combinations = {
            str(row["name"]): row for row in self.p1l4.get("combinations", [])
        }
        self.equilibrium = (self.p1l4.get("analysisModel") or {}).get("equilibrio") or {}
        self.pm_curves = {
            str(row["sectionId"]): row for row in self.p1l4.get("pmCurves", [])
        }
        self.materials = {
            str(row["sectionId"]): row for row in self.p1l4.get("sectionMaterials", [])
        }
        self.wall_registry = self.p1l4.get("wallRegistry", [])

        self.slab_path = Path(slab_json_path) if slab_json_path else self.path.with_name("slab_load_surfaces.json")
        self.slab_surfaces = []
        if self.slab_path.exists():
            slab_root = json.loads(self.slab_path.read_text(encoding="utf-8"))
            self.slab_surfaces = slab_root.get("slabs", []) if isinstance(slab_root, dict) else slab_root

        missing = [case for case in NAMED_CASES if case not in self.forces or case not in self.displacements]
        if missing:
            raise RuntimeError("El export de Unity no contiene resultados completos para: " + ", ".join(missing))

    def print_source(self):
        print("\nFUENTE UNICA DE VALIDACION")
        print(f"  {self.path}")
        print(f"  P1L4 version: {self.p1l4.get('version', '?')}")
        print("  Fuerzas: locales OpenSees, 12 acciones de extremo")
        print("  El menu no reanaliza ni reconstruye resultados.")

    def factors(self, case_name, custom=None):
        if custom is not None:
            return {name: float(custom.get(name, 0.0)) for name in BASE_CASES}
        if case_name in BASE_CASES:
            return {name: 1.0 if name == case_name else 0.0 for name in BASE_CASES}
        row = self.combinations.get(case_name)
        if not row:
            raise KeyError(f"Caso/combinacion no disponible en Unity: {case_name}")
        return {name: float(row.get(name, 0.0)) for name in BASE_CASES}

    def force_map(self, case_name, custom=None):
        if custom is None and case_name in self.forces:
            return self.forces[case_name]
        factors = self.factors(case_name, custom)
        result = {}
        ids = set().union(*(self.forces[name].keys() for name in BASE_CASES))
        for element_id in ids:
            combined = [0.0] * 12
            valid = True
            for name, factor in factors.items():
                source = self.forces[name].get(element_id)
                if source is None:
                    valid = False
                    break
                for index in range(12):
                    combined[index] += factor * source[index]
            if valid:
                result[element_id] = combined
        return result

    def displacement_map(self, case_name, custom=None):
        if custom is None and case_name in self.displacements:
            return self.displacements[case_name]
        factors = self.factors(case_name, custom)
        result = {}
        ids = set().union(*(self.displacements[name].keys() for name in BASE_CASES))
        for node_id in ids:
            if any(node_id not in self.displacements[name] for name in BASE_CASES):
                continue
            row = {"combo": case_name, "node": node_id}
            for component in ("ux", "uy", "uz", "rx", "ry", "rz"):
                row[component] = sum(
                    factors[name] * float(self.displacements[name][node_id].get(component, 0.0))
                    for name in BASE_CASES
                )
            result[node_id] = row
        return result

    def equilibrium_for(self, case_name, custom=None):
        if custom is None and case_name in self.equilibrium:
            return self.equilibrium[case_name]
        factors = self.factors(case_name, custom)
        result = {}
        for key in ("carga_total_kN", "reaccion_total_kN", "desbalance_kN"):
            result[key] = [
                sum(factors[name] * float(self.equilibrium[name][key][index]) for name in BASE_CASES)
                for index in range(3)
            ]
        return result

    def resolve_element(self, wanted):
        query = str(wanted).strip().lower()
        for element in self.elements.values():
            candidates = (
                str(element.get("id", "")), str(element.get("elementTag", "")),
                str(element.get("sourceId", "")),
            )
            if any(query == candidate.lower() for candidate in candidates if candidate):
                return element
        return None

    def resolve_slab(self, wanted):
        query = str(wanted).strip().lower()
        for slab in self.slab_surfaces:
            if str(slab.get("id", "")).lower() == query:
                return slab
        for slab in self.data.get("slabs", []):
            if str(slab.get("id", "")).lower() == query:
                return slab
        return None

    def element_length(self, element):
        ni = self.nodes.get(int(element["nodeI"]))
        nj = self.nodes.get(int(element["nodeJ"]))
        if not ni or not nj:
            return 0.0
        return _mag((nj["x"] - ni["x"], nj["y"] - ni["y"], nj["z"] - ni["z"]))

    @staticmethod
    def section_forces(force, length, t):
        """Replica exactamente ``FrameForces.Evaluate`` de Unity."""
        s = max(0.0, min(1.0, float(t)))
        x = length * s
        qy = (force[1] + force[7]) / length
        qz = (force[2] + force[8]) / length
        return {
            "N": -force[0] * (1.0 - s) + force[6] * s,
            "Vy": force[1] - qy * x,
            "Vz": force[2] - qz * x,
            "T": force[3] * (1.0 - s) - force[9] * s,
            "My": force[4] + force[2] * x - 0.5 * qz * x * x,
            "Mz": force[5] - force[1] * x + 0.5 * qy * x * x,
        }

    def summary(self):
        eq_g = self.equilibrium["G"]
        eq_q = self.equilibrium["Q"]
        eq_ex = self.equilibrium["EX"]
        eq_ey = self.equilibrium["EY"]
        q = float(self.data.get("Q_kN_m2", 0.0))
        area = abs(float(eq_q["carga_total_kN"][2])) / q if q else 0.0
        print("\nRESUMEN GLOBAL — mismos resultados cargados por Unity")
        print(f"  q_Q                         = {q:.6f} kN/m2")
        print(f"  Area cargada equivalente    = {area:.3f} m2")
        print(f"  Carga G total               = {abs(eq_g['carga_total_kN'][2]):.3f} kN")
        print(f"  Carga Q total               = {abs(eq_q['carga_total_kN'][2]):.3f} kN")
        print(f"  Carga lateral EX            = {abs(eq_ex['carga_total_kN'][0]):.3f} kN")
        print(f"  Carga lateral EY            = {abs(eq_ey['carga_total_kN'][1]):.3f} kN")
        for case in NAMED_CASES:
            imbalance = self.equilibrium[case]["desbalance_kN"]
            print(f"  Equilibrio {case:<2} |error|       = {_mag(imbalance):.3e} kN")

    def beam_report(self, wanted):
        element = self.resolve_element(wanted)
        if not element or element.get("type") != "viga":
            print(f"\nNo existe una viga Unity con ID/tag '{wanted}'. Usa opcion 7 para ver ejemplos.")
            return
        element_id = int(element["id"])
        print(f"\nVIGA UNITY {element.get('elementTag')} (ID {element_id})")
        print(f"  Piso / seccion              = {element.get('piso')} / {element.get('sectionId') or element.get('seccion')}")
        print(f"  Nodos I-J                   = {element.get('nodeI')} - {element.get('nodeJ')}")
        print(f"  Largo                       = {self.element_length(element):.3f} m")
        print(f"  Area tributaria             = {float(element.get('areaTributaria', 0)):.3f} m2")
        print(f"  G tributaria                = {float(element.get('deadLoad', 0)):.3f} kN")
        print(f"  Q tributaria                = {float(element.get('liveLoad', 0)):.3f} kN")
        for case in ("G", "Q"):
            load = self.element_loads.get(case, {}).get(element_id)
            if load:
                print(f"  Carga uniforme {case} (wz)       = {float(load.get('wz', 0)):.6f} kN/m")

    def slab_report(self, wanted):
        slab = self.resolve_slab(wanted)
        if not slab:
            print(f"\nNo existe una losa Unity con ID '{wanted}'. Usa opcion 7 para ver ejemplos.")
            return
        print(f"\nLOSA UNITY {slab.get('id')}")
        if "area" not in slab:
            area = abs((slab.get("x1", 0) - slab.get("x0", 0)) * (slab.get("y1", 0) - slab.get("y0", 0)))
            print(f"  Nivel                       = {slab.get('nivel')}")
            print(f"  Area geometrica             = {area:.3f} m2")
            print("  No hay desglose tributario en slab_load_surfaces.json.")
            return
        print(f"  Tipo                        = {slab.get('profile')}")
        print(f"  Area geometrica             = {float(slab['area']):.3f} m2")
        print(f"  Espesor                     = {float(slab['thickness']):.3f} m")
        print(f"  Densidad                    = {float(slab['density']):.1f} kg/m3")
        print(f"  Masa propia                 = {float(slab['mass']):.3f} kg")
        print(f"  Peso propio                 = {float(slab['selfWeight']):.3f} kN")
        print(f"  Permanente adicional       = {float(slab['permanentAdditional']):.3f} kN")
        print(f"  G de la losa               = {float(slab['totalG']):.3f} kN")
        print(f"  Sobrecarga Q               = {float(slab['totalQ']):.3f} kN")
        print("\n  APORTE A CADA RECEPTOR")
        for receiver in slab.get("receivers", []):
            beam = self.elements.get(int(receiver["beam"]), {})
            label = beam.get("elementTag") or f"ID {receiver['beam']}"
            print(f"    {label:<22} A={float(receiver['area']):7.3f} m2 | "
                  f"PP={float(receiver['selfWeight']):8.3f} | G={float(receiver['G']):8.3f} | Q={float(receiver['Q']):8.3f} kN")
        area_sum = sum(float(row.get("area", 0.0)) for row in slab.get("receivers", []))
        error = abs(area_sum - float(slab["area"]))
        print(f"  Suma areas receptoras       = {area_sum:.3f} m2")
        print(f"  Error de conservacion       = {error:.6e} m2  {'PASS' if error <= 1e-5 else 'FAIL'}")

    def floor_displacement_rows(self, case):
        component = "ux" if case == "EX" else "uy"
        grouped = {}
        for node_id, displacement in self.displacements[case].items():
            node = self.nodes.get(node_id)
            if not node:
                continue
            key = round(float(node["z"]), 3)
            grouped.setdefault(key, []).append(displacement)
        names = {}
        for slab in self.data.get("slabs", []):
            names.setdefault(round(float(slab.get("z", 0.0)), 3), set()).add(str(slab.get("nivel", "")))
        rows = []
        for z in sorted(grouped):
            values = [float(row[component]) for row in grouped[z]]
            rotations = [float(row.get("rz", 0.0)) for row in grouped[z]]
            rows.append({
                "z": z, "floor": "/".join(sorted(value for value in names.get(z, set()) if value)) or "nivel",
                "avg": sum(values) / len(values), "max": max(values), "min": min(values),
                "rz": sum(rotations) / len(rotations), "count": len(values),
            })
        return rows

    def seismic_report(self, detailed=False):
        print("\nSISMO EX/EY — resultados exportados a Unity")
        print(f"  Coeficiente sismico         = {float(self.data.get('seismic_coefficient', 0)):.3f}")
        for case, component, load_index in (("EX", "Ux", 0), ("EY", "Uy", 1)):
            eq = self.equilibrium[case]
            total = abs(float(eq["carga_total_kN"][load_index]))
            basal = abs(float(eq["reaccion_total_kN"][load_index]))
            print(f"\n[{case}] Carga lateral={total:.3f} kN | Corte basal={basal:.3f} kN | error={abs(total-basal):.3e} kN")
            for row in self.floor_displacement_rows(case):
                if detailed or row["count"]:
                    print(f"  {row['floor']:<16} z={row['z']:7.3f} m | {component} prom={row['avg']:+.6e} m | "
                          f"max={row['max']:+.6e} | min={row['min']:+.6e} | rz={row['rz']:+.3e} rad | n={row['count']}")

    def combination_report(self, case_name, custom=None):
        force_map = self.force_map(case_name, custom)
        disp_map = self.displacement_map(case_name, custom)
        eq = self.equilibrium_for(case_name, custom)
        factors = self.factors(case_name, custom)
        critical_node, critical_disp = max(
            disp_map.items(), key=lambda item: _mag((item[1]["ux"], item[1]["uy"], item[1]["uz"])),
        )
        critical_element = None
        critical_value = -1.0
        for element_id, force in force_map.items():
            value = max(abs(force[index]) for index in (0, 1, 2, 4, 5, 6, 7, 8, 10, 11))
            if value > critical_value:
                critical_element, critical_value = element_id, value
        print(f"\nRESPUESTA UNITY — {case_name}")
        print("  Factores: " + " + ".join(f"{factors[name]:g}{name}" for name in BASE_CASES))
        print(f"  Nodos con desplazamiento    = {len(disp_map)}")
        print(f"  Elementos con fuerza local  = {len(force_map)}")
        print(f"  Desplazamiento maximo       = {_mag((critical_disp['ux'], critical_disp['uy'], critical_disp['uz']))*1000:.3f} mm (nodo {critical_node})")
        element = self.elements.get(critical_element, {})
        print(f"  Mayor accion de extremo     = {critical_value:.3f} (elemento {element.get('elementTag', critical_element)})")
        print(f"  Desbalance global           = {_mag(eq['desbalance_kN']):.3e} kN")

    def gravity_report(self):
        eq = self.equilibrium["G"]
        displacements = self.displacements["G"]
        node_id, vertical = max(displacements.items(), key=lambda item: abs(float(item[1]["uz"])))
        node_h, horizontal = max(displacements.items(), key=lambda item: math.hypot(float(item[1]["ux"]), float(item[1]["uy"])))
        columns = {eid for eid, row in self.elements.items() if row.get("type") == "columna"}
        axial = max(((eid, max(abs(f[0]), abs(f[6]))) for eid, f in self.forces["G"].items() if eid in columns), key=lambda item: item[1])
        moment = max(((eid, max(math.hypot(f[4], f[5]), math.hypot(f[10], f[11]))) for eid, f in self.forces["G"].items() if eid in columns), key=lambda item: item[1])
        print("\nCASO G — mismos resultados exportados a Unity")
        print(f"  Carga aplicada              = {abs(eq['carga_total_kN'][2]):.3f} kN")
        print(f"  Reaccion vertical           = {abs(eq['reaccion_total_kN'][2]):.3f} kN")
        print(f"  Error de equilibrio         = {_mag(eq['desbalance_kN']):.3e} kN")
        print(f"  |Uz| maximo                 = {abs(float(vertical['uz']))*1000:.3f} mm (nodo {node_id})")
        print(f"  Uh maximo                   = {math.hypot(float(horizontal['ux']), float(horizontal['uy']))*1000:.3f} mm (nodo {node_h})")
        print(f"  Axial maximo columna        = {axial[1]:.3f} kN ({self.elements[axial[0]].get('elementTag')})")
        print(f"  Momento maximo columna      = {moment[1]:.3f} kN*m ({self.elements[moment[0]].get('elementTag')})")

    def superposition_report(self):
        print("\nAUDITORIA C1/C2/C3 — export directo vs superposicion G/Q/EX/EY")
        overall = 0.0
        for case in ("C1", "C2", "C3"):
            factors = self.factors(case)
            predicted_f = self.force_map("PERSONALIZADA", factors)
            predicted_d = self.displacement_map("PERSONALIZADA", factors)
            force_error = max(
                abs(self.forces[case][eid][index] - values[index])
                for eid, values in predicted_f.items() for index in range(12)
            )
            disp_error = max(
                abs(float(self.displacements[case][nid][component]) - float(values[component]))
                for nid, values in predicted_d.items() for component in ("ux", "uy", "uz", "rx", "ry", "rz")
            )
            overall = max(overall, force_error, disp_error)
            print(f"  {case}: error fuerzas={force_error:.3e} | error desplazamientos={disp_error:.3e}  "
                  f"{'PASS' if max(force_error, disp_error) <= 1e-6 else 'FAIL'}")
        print(f"  Error maximo global         = {overall:.3e}")

    @staticmethod
    def capacity_ratio(curve, p, m):
        points = curve.get("points", [])
        if len(points) < 2:
            return 0.0
        p_min = min(float(row["P_kN"]) for row in points)
        p_max = max(float(row["P_kN"]) for row in points)
        m = abs(float(m))
        if p < p_min - 0.01 or p > p_max + 0.01:
            return 99.99
        capacity = 0.0
        found = False
        for a, b in zip(points, points[1:]):
            pa, pb = float(a["P_kN"]), float(b["P_kN"])
            if abs(pb - pa) <= 0.001 or p < min(pa, pb) or p > max(pa, pb):
                continue
            t = (p - pa) / (pb - pa)
            capacity = max(capacity, float(a["M_kN_m"]) + t * (float(b["M_kN_m"]) - float(a["M_kN_m"])))
            found = True
        if not found:
            capacity = max((float(row["M_kN_m"]) for row in points if abs(float(row["P_kN"]) - p) <= 0.01), default=0.0)
        if capacity <= 0.001:
            return 0.0 if m <= 0.001 else 99.99
        return m / capacity

    def resolve_curve_for_element(self, element):
        section = str(element.get("sectionId") or element.get("seccion") or "")
        return self.pm_curves.get(section) or self.pm_curves.get(section + "_FIBER")

    def pm_verification(self, case_name, custom=None):
        force_map = self.force_map(case_name, custom)
        rows = []
        skipped = 0
        for element_id, element in self.elements.items():
            if element.get("type") != "columna":
                continue
            curve = self.resolve_curve_for_element(element)
            force = force_map.get(element_id)
            if not curve or not force or len(force) < 12:
                skipped += 1
                continue
            # Exactamente ElementSelectable.GetPMDemandForCase: extremo I local.
            p = float(force[0])
            m = math.hypot(float(force[4]), float(force[5]))
            ratio = self.capacity_ratio(curve, p, m)
            rows.append((ratio, element, p, m, curve["sectionId"]))
        rows.sort(key=lambda row: row[0], reverse=True)
        failing = [row for row in rows if row[0] > 1.0]
        print(f"\nVERIFICACION P-M IDENTICA A UNITY — {case_name}")
        print("  Demanda: P=f[0], M=sqrt(f[4]^2+f[5]^2), fuerzas locales extremo I")
        print(f"  Columnas evaluadas={len(rows)} | sin curva/fuerza={skipped} | fuera/no cumple={len(failing)}")
        print("  Peores resultados:")
        for ratio, element, p, m, section in rows[:10]:
            status = "FUERA DE CURVA" if ratio >= 99.99 else ("NO CUMPLE" if ratio > 1.0 else "OK")
            ratio_text = ">=99.99" if ratio >= 99.99 else f"{ratio:.3f}"
            print(f"    {element.get('elementTag'):<16} P={p:9.2f} kN | M={m:9.2f} kN*m | C={ratio_text:>7} | {section} | {status}")
        print(f"  VEREDICTO: {'NO CUMPLE' if failing else 'CUMPLE'}")

    def capacity_report(self, section_id="COL70/70_FIBER"):
        curve = self.pm_curves.get(section_id)
        if not curve:
            print(f"\nNo existe la curva '{section_id}' en el JSON de Unity.")
            return
        material = self.materials.get(section_id) or self.materials.get(section_id.replace("_FIBER", "")) or {}
        print(f"\nCAPACIDAD EXPORTADA A UNITY — {section_id}")
        print(f"  Tipo                        = {curve.get('elementType')}")
        print(f"  Seccion                     = {curve.get('b_m', 0):.3f} x {curve.get('h_m', 0):.3f} m")
        print(f"  f'c / fy                    = {curve.get('fc_MPa', material.get('fc_MPa', 0)):.1f} / {curve.get('fy_MPa', material.get('fy_MPa', 0)):.1f} MPa")
        print(f"  Armadura                    = {curve.get('steelBars', 0)} barras phi {curve.get('barDiameter_mm', 0):.1f} mm")
        print(f"  Ast / rho                   = {curve.get('Ast_mm2', 0):.1f} mm2 / {curve.get('rho_percent', 0):.3f}%")
        print(f"  Po                          = {curve.get('Po_kN', 0):.2f} kN")
        print("  Puntos P-M mostrados por Unity:")
        for index, point in enumerate(curve.get("points", [])):
            name = point.get("label") or chr(ord("A") + index)
            print(f"    {name:<12} P={float(point['P_kN']):10.2f} kN | M={float(point['M_kN_m']):10.2f} kN*m")
        mc = curve.get("momentCurvature") or []
        if mc:
            maximum = max(mc, key=lambda row: abs(float(row.get("M_kN_m", 0))))
            first_yield = curve.get("momentCurvatureFirstYield") or {}
            print(f"  M-phi: {len(mc)} puntos | fluencia phi={float(first_yield.get('phi_1_m', 0)):.6e} 1/m, "
                  f"M={float(first_yield.get('M_kN_m', 0)):.2f} kN*m | Mmax={float(maximum['M_kN_m']):.2f} kN*m")

    def wall_capacity_report(self, wanted=""):
        query = str(wanted).strip().lower()
        selected = None
        if query:
            for row in self.wall_registry:
                candidates = (str(row.get("index", "")), str(row.get("pmSectionId", "")))
                if any(query == value.lower() for value in candidates if value):
                    selected = row
                    break
            curve = self.pm_curves.get(wanted)
            if curve and curve.get("elementType") == "muro":
                self.capacity_report(wanted)
                return
        if selected is None and self.wall_registry:
            selected = self.wall_registry[0]
        if not selected:
            print("\nUnity no contiene muros registrados.")
            return
        section = selected.get("pmSectionId")
        self.capacity_report(section)
        print(f"  Muro registro               = {selected.get('index')} ({selected.get('bottom')} -> {selected.get('top')})")
        for demand in selected.get("demands", []):
            ratio = self.capacity_ratio(self.pm_curves[section], float(demand["P_kN"]), float(demand["M_kN_m"]))
            print(f"    {demand['combo']}: P={float(demand['P_kN']):.2f} kN | M={float(demand['M_kN_m']):.2f} kN*m | C={ratio:.3f}")

    def internal_force_report(self, wanted, case_name, custom=None, position=None):
        element = self.resolve_element(wanted)
        if not element:
            print(f"\nNo existe un elemento Unity con ID/tag '{wanted}'. Usa opcion 7.")
            return
        force = self.force_map(case_name, custom).get(int(element["id"]))
        if not force:
            print(f"\nEl elemento {wanted} no posee fuerzas exportadas para {case_name}.")
            return
        length = self.element_length(element)
        print(f"\nFUERZAS UNITY — {element.get('elementTag')} | ID {element['id']} | {case_name}")
        print(f"  Tipo / piso / seccion       = {element.get('type')} / {element.get('piso')} / {element.get('sectionId') or element.get('seccion')}")
        print(f"  Nodos I-J / largo           = {element.get('nodeI')}-{element.get('nodeJ')} / {length:.3f} m")
        if position is not None:
            x = max(0.0, min(length, float(position)))
            samples = [(x / length if length else 0.0, f"x={x:.3f} m ({100*x/length if length else 0:.1f}%)")]
        else:
            samples = ((0.0, "I (0%)"), (0.5, "centro (50%)"), (1.0, "J (100%)"))
        print("  Convencion identica a FrameForces.Evaluate de Unity")
        for t, label in samples:
            values = self.section_forces(force, length, t)
            print(f"\n  [{label}]")
            print("    N={N:+.3f} kN | Vy={Vy:+.3f} kN | Vz={Vz:+.3f} kN | T={T:+.3f} kN*m".format(**values))
            print("    My={My:+.3f} kN*m | Mz={Mz:+.3f} kN*m".format(**values))
        print("  RAW OpenSees local          = [" + ", ".join(f"{value:.6g}" for value in force) + "]")

    def print_ids(self):
        print("\nIDs DISPONIBLES EN UNITY")
        for type_name in ("viga", "columna"):
            rows = [row for row in self.elements.values() if row.get("type") == type_name][:10]
            print(f"  {type_name.capitalize()}s:")
            for row in rows:
                print(f"    ID {row['id']:<5} | {row.get('elementTag', '-'):<18} | {row.get('sectionId') or row.get('seccion')}")
        print("  Losas:")
        for row in self.slab_surfaces[:10]:
            print(f"    {row.get('id')} | A={float(row.get('area', 0)):.3f} m2 | {row.get('profile')}")
        print("  Muros / curvas:")
        for row in self.wall_registry[:10]:
            print(f"    registro {row.get('index')} | {row.get('pmSectionId')} | {row.get('bottom')}->{row.get('top')}")

