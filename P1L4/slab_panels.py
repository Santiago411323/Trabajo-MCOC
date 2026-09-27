"""Reconstruye paneles rectangulares de losa desde recintos cerrados por vigas.

Las losas son superficies de carga, no elementos shell. Cada panel queda
limitado por vigas horizontales/verticales en sus cuatro lados. El reparto
tributario usa lineas a 45 grados para losas bidireccionales (b/a <= 2) y
franjas unidireccionales para b/a > 2.
"""

from __future__ import annotations

from collections import Counter, defaultdict
import argparse
import json
import math
from pathlib import Path


TOL = 0.025


def _close(a, b, tol=TOL):
    return abs(a - b) <= tol


def _merge(intervals):
    merged = []
    for lo, hi in sorted((min(a, b), max(a, b)) for a, b in intervals):
        if not merged or lo > merged[-1][1] + TOL:
            merged.append([lo, hi])
        else:
            merged[-1][1] = max(merged[-1][1], hi)
    return merged


def _covers(records, lo, hi):
    merged = _merge((max(lo, r[1]), min(hi, r[2])) for r in records
                    if r[2] >= lo - TOL and r[1] <= hi + TOL)
    if not merged or merged[0][0] > lo + TOL:
        return False
    cursor = merged[0][1]
    for start, end in merged[1:]:
        if start > cursor + TOL:
            return False
        cursor = max(cursor, end)
    return cursor >= hi - TOL


def _line_records(beams, nodes, z):
    horizontal = defaultdict(list)
    vertical = defaultdict(list)
    for beam in beams:
        ni, nj = nodes[beam["nodeI"]], nodes[beam["nodeJ"]]
        if max(abs(ni["z"] - z), abs(nj["z"] - z)) > TOL:
            continue
        if _close(ni["y"], nj["y"]):
            key = round((ni["y"] + nj["y"]) * 0.5, 3)
            horizontal[key].append((beam["id"], min(ni["x"], nj["x"]), max(ni["x"], nj["x"])))
        elif _close(ni["x"], nj["x"]):
            key = round((ni["x"] + nj["x"]) * 0.5, 3)
            vertical[key].append((beam["id"], min(ni["y"], nj["y"]), max(ni["y"], nj["y"])))
    return horizontal, vertical


def _has_interior_beam(x0, x1, y0, y1, horizontal, vertical):
    for y, records in horizontal.items():
        if y0 + TOL < y < y1 - TOL and any(min(x1, r[2]) - max(x0, r[1]) > TOL for r in records):
            return True
    for x, records in vertical.items():
        if x0 + TOL < x < x1 - TOL and any(min(y1, r[2]) - max(y0, r[1]) > TOL for r in records):
            return True
    return False


def _rectangles_at_level(beams, nodes, z):
    horizontal, vertical = _line_records(beams, nodes, z)
    xs, ys = sorted(vertical), sorted(horizontal)
    candidates = []
    for ix, x0 in enumerate(xs):
        for x1 in xs[ix + 1:]:
            for iy, y0 in enumerate(ys):
                for y1 in ys[iy + 1:]:
                    if not (_covers(horizontal[y0], x0, x1) and _covers(horizontal[y1], x0, x1) and
                            _covers(vertical[x0], y0, y1) and _covers(vertical[x1], y0, y1)):
                        continue
                    if _has_interior_beam(x0, x1, y0, y1, horizontal, vertical):
                        continue
                    candidates.append((round(x0, 6), round(y0, 6), round(x1, 6), round(y1, 6)))
    # Remove duplicate numeric candidates while retaining deterministic order.
    return sorted(set(candidates), key=lambda r: (r[1], r[0], r[3], r[2]))


def _overlap_ratio(a, b):
    ix = max(0.0, min(a[2], b[2]) - max(a[0], b[0]))
    iy = max(0.0, min(a[3], b[3]) - max(a[1], b[1]))
    intersection = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - intersection
    return intersection / union if union > 0 else 0.0


def rebuild_slabs(data):
    nodes = {n["id"]: n for n in data.get("nodes", [])}
    beams = [e for e in data.get("elements", []) if e.get("type") == "viga" and
             e.get("nodeI") in nodes and e.get("nodeJ") in nodes]
    levels = sorted({round((nodes[e["nodeI"]]["z"] + nodes[e["nodeJ"]]["z"]) * 0.5, 3)
                     for e in beams if abs(nodes[e["nodeI"]]["z"] - nodes[e["nodeJ"]]["z"]) <= TOL})
    old = list(data.get("slabs", []))
    labels = defaultdict(Counter)
    for slab in old:
        labels[round(float(slab["z"]), 3)][slab.get("nivel") or ""] += 1

    used_ids, slabs = set(), []
    for level_index, z in enumerate(levels, start=1):
        level_label = labels[z].most_common(1)[0][0] if labels[z] else f"NIVEL_{z:g}"
        for panel_index, (x0, y0, x1, y1) in enumerate(_rectangles_at_level(beams, nodes, z), start=1):
            best, best_score = None, 0.0
            for candidate in old:
                if candidate.get("id") in used_ids or abs(float(candidate["z"]) - z) > TOL:
                    continue
                bounds = (min(candidate["x0"], candidate["x1"]), min(candidate["y0"], candidate["y1"]),
                          max(candidate["x0"], candidate["x1"]), max(candidate["y0"], candidate["y1"]))
                score = _overlap_ratio((x0, y0, x1, y1), bounds)
                if score > best_score:
                    best, best_score = candidate, score
            if best is not None and best_score >= 0.98:
                slab_id = str(best["id"])
                used_ids.add(best["id"])
            else:
                slab_id = f"L{level_index}_{panel_index:03d}"
                suffix = 1
                while slab_id in used_ids:
                    suffix += 1
                    slab_id = f"L{level_index}_{panel_index:03d}_{suffix}"
                used_ids.add(slab_id)
            slabs.append(dict(id=slab_id, nivel=level_label, x0=x0, y0=y0, x1=x1, y1=y1, z=z))
    return slabs


def _integrate_width(length, short, two_way, start, end):
    if not two_way:
        return (end - start) * short * 0.5
    breaks = {start, end, 0.0, length, length * 0.5, short * 0.5, length - short * 0.5}
    points = sorted(max(start, min(end, p)) for p in breaks if start - 1e-9 <= p <= end + 1e-9)
    points = sorted(set(points + [start, end]))

    def width(s):
        return max(0.0, min(s, length - s, short * 0.5))

    area = 0.0
    for a, b in zip(points, points[1:]):
        area += (width(a) + width(b)) * 0.5 * (b - a)
    return area


def _side_receivers(records, lo, hi, side_length, short, two_way, active):
    clipped = [(beam, max(lo, a), min(hi, b)) for beam, a, b in records
               if b >= lo - TOL and a <= hi + TOL]
    points = sorted({lo, hi, *(a for _, a, _ in clipped), *(b for _, _, b in clipped)})
    result = defaultdict(float)
    for a, b in zip(points, points[1:]):
        if b - a <= 1e-8:
            continue
        active_beams = sorted({beam for beam, start, end in clipped if start <= (a + b) * 0.5 + TOL and end >= (a + b) * 0.5 - TOL})
        if not active_beams:
            continue
        area = _integrate_width(side_length, short, two_way, a - lo, b - lo) if active else 0.0
        for beam in active_beams:
            result[beam] += area / len(active_beams)
    return result


def slab_metadata(data, slabs=None):
    slabs = slabs if slabs is not None else data.get("slabs", [])
    nodes = {n["id"]: n for n in data.get("nodes", [])}
    beams = [e for e in data.get("elements", []) if e.get("type") == "viga" and
             e.get("nodeI") in nodes and e.get("nodeJ") in nodes]
    q_g = float(data.get("q_G", 6.22722275))
    q_q = float(data.get("Q_kN_m2", 4.903325))
    thickness = 0.15
    density = 2500.0
    gravity = 9.80665
    unit_weight = density * gravity / 1000.0
    self_weight = thickness * unit_weight
    additional = q_g - self_weight
    by_level = {}
    rows = []
    for slab in slabs:
        z = round(float(slab["z"]), 3)
        if z not in by_level:
            by_level[z] = _line_records(beams, nodes, z)
        horizontal, vertical = by_level[z]
        x0, x1 = sorted((float(slab["x0"]), float(slab["x1"])))
        y0, y1 = sorted((float(slab["y0"]), float(slab["y1"])))
        dx, dy = x1 - x0, y1 - y0
        short, long = min(dx, dy), max(dx, dy)
        two_way = long / short <= 2.0 + 1e-9
        definitions = [
            ("bottom", horizontal.get(round(y0, 3), []), x0, x1, dx),
            ("right", vertical.get(round(x1, 3), []), y0, y1, dy),
            ("top", horizontal.get(round(y1, 3), []), x0, x1, dx),
            ("left", vertical.get(round(x0, 3), []), y0, y1, dy),
        ]
        edges, aggregate = [], defaultdict(float)
        for side, records, lo, hi, length in definitions:
            active = two_way or _close(length, long)
            receiver_areas = _side_receivers(records, lo, hi, length, short, two_way, active)
            for beam, area in receiver_areas.items():
                aggregate[beam] += area
            edges.append(dict(side=side, area=sum(receiver_areas.values()), beams=sorted(receiver_areas),
                              receivers=[dict(beam=beam, area=area) for beam, area in sorted(receiver_areas.items())],
                              message="" if receiver_areas else "Borde sin receptor"))
        area = dx * dy
        rows.append(dict(id=slab["id"], profile="TWO_WAY" if two_way else "ONE_WAY",
                         thickness=thickness, density=density, unitWeight=unit_weight,
                         finishes=additional, qG=q_g, qQ=q_q, area=area,
                         mass=area * thickness * density, selfWeight=area * self_weight,
                         permanentAdditional=area * additional, totalG=area * q_g, totalQ=area * q_q,
                         ratio=long / short, edges=edges,
                         receivers=[dict(beam=beam, area=trib_area,
                                         selfWeight=trib_area * self_weight,
                                         G=trib_area * q_g, Q=trib_area * q_q)
                                    for beam, trib_area in sorted(aggregate.items())]))
    return rows


def audit(data, slabs=None, metadata=None):
    slabs = slabs if slabs is not None else data.get("slabs", [])
    metadata = metadata if metadata is not None else slab_metadata(data, slabs)
    bad_boundaries = sum(1 for row in metadata for edge in row["edges"] if not edge["beams"])
    max_area_error = max((abs(sum(r["area"] for r in row["receivers"]) - row["area"]) for row in metadata), default=0.0)
    return dict(slabs=len(slabs), badBoundaries=bad_boundaries, maxAreaError=max_area_error)


def update_resource(resource_path):
    resource_path = Path(resource_path)
    data = json.loads(resource_path.read_text(encoding="utf-8-sig"))
    slabs = rebuild_slabs(data)
    data["slabs"] = slabs
    metadata = slab_metadata(data, slabs)
    resource_path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    catalog_path = resource_path.with_name("slab_load_surfaces.json")
    catalog_path.write_text(json.dumps({"slabs": metadata}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return audit(data, slabs, metadata), catalog_path


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Reconstruye losas cerradas por vigas y su reparto tributario.")
    parser.add_argument("resource", nargs="?", default=str(Path(__file__).resolve().parent /
                        "unity_visualizador/Assets/Resources/estructura_p1l4_unity.json"))
    args = parser.parse_args()
    report, catalog = update_resource(args.resource)
    print(json.dumps(report, indent=2))
    print(f"Catalogo: {catalog}")
