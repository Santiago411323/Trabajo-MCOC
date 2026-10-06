"""Análisis dinámico elástico independiente. No escribe resultados estáticos.

Datos de entrada: la geometría corregida/exportada del viewer P1L4.
Salida: JSON de contrato + binario float32 little endian, un estado por paso.
Se usa la matriz de masa nodal (sin masa de elemento); las fuerzas localForce
son acciones resistentes, no fuerzas que incluyen inercia/amortiguamiento.
"""
from __future__ import annotations

import argparse
from array import array
import hashlib
import json
import math
import os
from pathlib import Path
import re
import struct
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
BASE = Path(__file__).resolve().parent
DEFAULT_MODEL = ROOT / "P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json"
INTENSITIES = (0.25, 0.50, 1.00, 1.50)
MAGIC = b"MCOCSIS1"


def load_engine():
    # Reutiliza los paquetes locales incluso si se movió el ejecutable del venv.
    try:
        import openseespy.opensees
    except ImportError:
        local_packages = ROOT / ".venv/Lib/site-packages"
        if local_packages.exists():
            sys.path.insert(0, str(local_packages))
    cache = BASE / "cache"
    cache.mkdir(exist_ok=True)
    os.environ.setdefault("MPLCONFIGDIR", str(cache))
    sys.path.insert(0, str(ROOT / "P1L3"))
    import carga_viva_sismo as model
    if model.ops is None:
        raise RuntimeError("Este Python no puede cargar OpenSeesPy. Configure seismic/runtime.local.json o instale requirements.txt.")
    return model


def read_record(spec, directory):
    path = (Path(directory) / spec["file"]).resolve()
    content = path.read_text(encoding="utf-8-sig")
    if spec.get("format") != "AT2":
        raise ValueError("Formato no soportado: declare AT2 con NPTS y DT; no se adivinan unidades.")
    header = re.search(r"NPTS\s*=\s*(\d+)\s*,?\s*DT\s*=\s*([.\dEe+-]+)", content, re.I)
    if not header:
        raise ValueError("AT2 sin encabezado NPTS/DT.")
    body = content[content.find("\n", header.end()) + 1:].split("***")[0]
    acceleration = [float(value.replace("D", "E")) for value in body.split()]
    count, dt = int(header[1]), float(header[2])
    if len(acceleration) != count or count < 2 or not math.isfinite(dt) or dt <= 0:
        raise ValueError("Cantidad de muestras o DT inválidos.")
    conversion = {"g": 9.80665, "m/s2": 1.0, "cm/s2": 0.01}.get(spec.get("units"))
    if conversion is None or not all(math.isfinite(x) for x in acceleration):
        raise ValueError("Unidades de aceleración desconocidas o valores no finitos.")
    values = [conversion * x for x in acceleration]
    return values, dt, hashlib.sha256(path.read_bytes()).hexdigest()


def mass_ledger(data, cvm, live_fraction=0.5):
    """Cada peso se cuenta una vez: G tributario, Q tributario y PP de barras."""
    nodes = cvm.node_map(data)
    weights = dict(cvm.seismic_self_weight_nodal(data))
    q = float(data["Q_kN_m2"])
    slab_weight = live_weight = 0.0
    for member in data["elements"]:
        if member["type"] != "viga":
            continue
        dead = float(member.get("deadLoad") or 0)
        live = q * float(member.get("areaTributaria") or member.get("tributaryArea") or 0)
        slab_weight += dead
        live_weight += live_fraction * live
        for end in ("nodeI", "nodeJ"):
            nid = member[end]
            if nid not in nodes:
                raise ValueError(f"Viga {member['id']} sin nodo {nid}.")
            weights[nid] = weights.get(nid, 0.0) + (dead + live_fraction * live) / 2
    if any(not math.isfinite(w) or w < 0 for w in weights.values()):
        raise ValueError("Pesos sísmicos negativos/no finitos.")
    expected = slab_weight + live_weight + sum(cvm.seismic_self_weight_nodal(data).values())
    error = abs(sum(weights.values()) - expected)
    if error > max(1e-7, expected * 1e-10):
        raise ValueError("No se conserva el peso al construir masas.")
    return weights, {"slabG_kN": slab_weight, "includedQ_kN": live_weight,
                     "memberSelfWeight_kN": expected - slab_weight - live_weight,
                     "totalWeight_kN": expected, "totalMass_kN_s2_m": expected / cvm.G_ACCEL,
                     "conservationError_kN": error, "liveFraction": live_fraction}


def setup_model(data, cvm, damping=0.05):
    if not 0 <= damping < 1:
        raise ValueError("Amortiguamiento inválido.")
    cvm.build_model(data)
    ops = cvm.ops
    if cvm.AUTO_ANCHORS:
        raise RuntimeError("Modelo con componentes sin apoyo: se rechazan anclajes automáticos en dinámica.")
    weights, ledger = mass_ledger(data, cvm)
    existing = set(ops.getNodeTags())
    for nid, weight in weights.items():
        if nid not in existing and weight > 0:
            raise RuntimeError(f"Peso asignado a nodo no analítico {nid}.")
        if nid in existing:
            m = weight / cvm.G_ACCEL
            ops.mass(nid, m, m, m, 0., 0., 0.)
    fixed = {s["node"] for s in data["supports"]}
    ledger["restrainedWeight_kN"] = sum(weights.get(n, 0) for n in fixed)
    ledger["freeWeight_kN"] = sum(w for n, w in weights.items() if n not in fixed)
    ledger["nodalMasses"] = [{"nodeTag": n, "mass": w / cvm.G_ACCEL, "fixed": n in fixed}
                             for n, w in sorted(weights.items())]
    ops.constraints(cvm.CONSTRAINT_HANDLER)
    ops.numberer("RCM")
    ops.system("UmfPack")
    values = list(ops.eigen("-genBandArpack", 6))
    if len(values) != 6 or any(not math.isfinite(v) or v <= 1e-8 for v in values):
        raise RuntimeError("Modos inválidos: revise masa, apoyos y diafragmas.")
    omega = [math.sqrt(v) for v in values]
    # Modos 1 y 3: hipótesis explícita, no una calibración experimental.
    w1, w2 = omega[0], omega[2]
    alpha = 2 * damping * w1 * w2 / (w1 + w2)
    beta = 2 * damping / (w1 + w2)
    ops.rayleigh(alpha, 0., beta, 0.)
    return ledger, {"periods_s": [2 * math.pi / w for w in omega],
                    "dampingRatio": damping, "dampingModes": [1, 3],
                    "alphaM": alpha, "betaKInitial": beta,
                    "achievedModalDamping": [alpha / (2*w) + beta*w/2 for w in omega],
                    "diaphragms": list(cvm.DIAPHRAGMS)}


def gravity_state(data, cvm):
    ops = cvm.ops
    live = cvm.transfer_live_load(data, float(data["Q_kN_m2"]))
    loads = cvm.combine_nodal_loads({"G": cvm.dead_nodal_loads(data), "Q": cvm.live_load_set(live)},
                                   {"G": 1., "Q": 0.5})
    cvm.apply_nodal_loads(loads)
    ops.integrator("LoadControl", 0.1)
    ops.algorithm("Linear")
    ops.analysis("Static")
    if ops.analyze(10) != 0:
        raise RuntimeError("Falló el equilibrio gravitacional previo.")
    ops.reactions()
    applied = cvm.total_load_vector(loads, data)
    reaction = [sum(ops.nodeReaction(n)[k] for n in ops.getFixedNodes()) for k in range(3)]
    error = max(abs(applied[k] + reaction[k]) for k in range(3))
    if error > max(0.01, max(abs(x) for x in applied) * 1e-6):
        raise RuntimeError(f"Equilibrio gravitacional inválido: error {error} kN.")
    ops.loadConst("-time", 0.0)
    ops.wipeAnalysis()
    return {"factors": {"G": 1., "Q": 0.5}, "applied_kN": applied,
            "reaction_kN": reaction, "maximumError_kN": error}


def dataset_id(record_id, direction, intensity):
    return f"{record_id}_{direction}_i{round(intensity*100):03d}"


def run(data, model_hash, spec, intensity, direction, output, dt_factor=1, max_seconds=None, tail=2., damping=.05):
    if data.get("units") != "m, kN, kN*m":
        raise ValueError("El módulo requiere un modelo en m, kN y kN*m; no convierte unidades implícitamente.")
    if intensity not in INTENSITIES or direction not in ("X", "Y"):
        raise ValueError("Intensidad o dirección no admitida.")
    if dt_factor < 1 or int(dt_factor) != dt_factor or tail < 0:
        raise ValueError("Subdivisiones/paso de tiempo inválidos.")
    cvm = load_engine()
    ops = cvm.ops
    values, record_dt, record_hash = read_record(spec, BASE / "records")
    # Inicio compatible con condiciones iniciales nulas. Desplazamiento del
    # registro en DT explícito en metadata; no se filtra ni altera su amplitud.
    acceleration = [0.] + values
    duration = (len(acceleration)-1)*record_dt + tail
    if max_seconds is not None:
        if max_seconds <= 0:
            raise ValueError("Duración debe ser positiva.")
        duration = min(duration, max_seconds)
    dt = record_dt / dt_factor
    steps = int(math.floor(duration / dt + 1e-8))
    ledger, modal = setup_model(data, cvm, damping)
    gravity = gravity_state(data, cvm)
    # Excluye solo maestros auxiliares; no presupone un rango de tags reales.
    nodes = sorted(set(ops.getNodeTags()) & {n["id"] for n in data["nodes"]})
    members = sorted(data["elements"], key=lambda e: e["id"])
    identifier = dataset_id(spec["id"], direction, intensity)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    binary = output / (identifier + ".bytes")
    partial = binary.with_suffix(".partial")
    meta_path = output / (identifier + ".json")
    node_map = cvm.node_map(data)
    stride = 1 + len(nodes)*3 + len(members)*12
    ops.timeSeries("Path", 900001, "-dt", record_dt, "-values", *acceleration, "-factor", intensity)
    ops.pattern("UniformExcitation", 900001, 1 if direction == "X" else 2, "-accel", 900001)
    ops.constraints(cvm.CONSTRAINT_HANDLER)
    ops.numberer("RCM")
    ops.system("UmfPack")
    ops.integrator("Newmark", .5, .25)
    ops.algorithm("Linear")
    ops.analysis("Transient")
    maximum = {"value_m": 0., "time": 0., "nodeTag": 0}
    started = time.monotonic()
    try:
        with partial.open("wb") as file:
            file.write(MAGIC + struct.pack("<iii", len(nodes), len(members), steps+1))
            for step in range(steps+1):
                if step and ops.analyze(1, dt) != 0:
                    raise RuntimeError(f"Transient no convergió en paso {step}, t={ops.getTime()}.")
                t = float(ops.getTime())
                row = [t]
                for nid in nodes:
                    u = list(ops.nodeDisp(nid))[:3]
                    magnitude = math.sqrt(sum(v*v for v in u))
                    if magnitude > maximum["value_m"]:
                        maximum = {"value_m": magnitude, "time": t, "nodeTag": nid}
                    row.extend(u)
                for e in members:
                    f = list(ops.eleResponse(e["id"], "localForce"))
                    if len(f) != 12:
                        raise RuntimeError(f"localForce inválido para {e['elementTag']}.")
                    row.extend(f)
                if len(row) != stride or not all(math.isfinite(v) for v in row):
                    raise RuntimeError("Respuesta temporal incompleta/no finita.")
                packed = array("f", row)
                if sys.byteorder != "little":
                    packed.byteswap()
                file.write(packed.tobytes())
                if step % 100 == 0 or step == steps:
                    print(f"PROGRESS {step}/{steps} t={t:.3f}s max={maximum['value_m']*1000:.3f}mm", flush=True)
        partial.replace(binary)
        metadata = {
            "version": 1, "id": identifier, "complete": True, "analysis": "OpenSees elastic linear Transient",
            "recordId": spec["id"], "recordName": spec["name"], "recordSource": spec["source"],
            "recordNote": spec.get("note", ""), "recordHash": record_hash, "modelHash": model_hash,
            "direction": direction, "intensity": intensity, "pgaOriginal_m_s2": max(map(abs, values)),
            "pgaApplied_m_s2": intensity*max(map(abs, values)), "recordDt": record_dt,
            "recordShift_s": record_dt, "recordDuration_s": (len(values)-1)*record_dt,
            "recordAcceleration_m_s2": acceleration, "dt": dt, "duration": steps*dt,
            "truncated": max_seconds is not None and steps*dt < (len(acceleration)-1)*record_dt,
            "frameCount": steps+1, "stride": stride, "binaryFile": binary.name,
            "binarySha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
            "units": "m, s, kN, kN*m; mass kN*s2/m", "nodeResponse": "TOTAL displacement relative to ground, INCLUDING gravity G+0.5Q",
            "forceResponse": "TOTAL local resistant end actions INCLUDING gravity; [Fx,Fy,Fz,Mx,My,Mz] I,J; no inertia forces",
            "nodeTags": nodes, "nodeCoordinates": [node_map[n][c] for n in nodes for c in ("x", "y", "z")],
            "members": [{"id": e["id"], "elementTag": e.get("elementTag",str(e["id"])), "type": e["type"],
                         "nodeI": e["nodeI"], "nodeJ": e["nodeJ"], "width_m": e.get("width_m",.6),
                         "height_m": e.get("height_m",.8), "sectionId": e.get("sectionId", "")} for e in members],
            "mass": ledger, "modal": modal, "gravity": gravity, "maximumDisplacement": maximum,
            "runSeconds": time.monotonic()-started, "engineVersion": ops.version(),
            "capacityEvaluation": "NOT EVALUATED — phase 1; no damage or collapse inference"
        }
        meta_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
        print(f"COMPLETE {meta_path} ({metadata['runSeconds']:.1f}s)", flush=True)
        return metadata
    finally:
        ops.wipe()
        if partial.exists():
            partial.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--record", default="el_centro_1940_ns")
    parser.add_argument("--intensity", type=float, choices=INTENSITIES, default=1.)
    parser.add_argument("--all-intensities", action="store_true")
    parser.add_argument("--direction", choices=("X", "Y"), default="X")
    parser.add_argument("--output", type=Path, default=BASE / "results")
    parser.add_argument("--substeps", type=int, default=1)
    parser.add_argument("--max-seconds", type=float)
    parser.add_argument("--tail", type=float, default=2.)
    parser.add_argument("--damping", type=float, default=.05)
    args = parser.parse_args()
    specs = json.loads((BASE / "records/catalog.json").read_text(encoding="utf-8"))["records"]
    spec = next((r for r in specs if r["id"] == args.record), None)
    if spec is None:
        raise ValueError("Registro no encontrado en catálogo.")
    data = json.loads(args.model.read_text(encoding="utf-8"))
    model_hash = hashlib.sha256(args.model.read_bytes()).hexdigest()
    for intensity in INTENSITIES if args.all_intensities else [args.intensity]:
        run(data, model_hash, spec, intensity, args.direction, args.output,
            args.substeps, args.max_seconds, args.tail, args.damping)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"ERROR: {error}", file=sys.stderr, flush=True)
        sys.exit(1)
