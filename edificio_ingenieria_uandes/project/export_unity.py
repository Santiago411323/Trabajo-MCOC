import json
from pathlib import Path


def export_unity_structure(geometry, output_path):
    node_ids = set()
    for collection_name in ["columns", "beams", "foundation_beams"]:
        for element in geometry[collection_name]:
            node_ids.add(element["node_i"])
            node_ids.add(element["node_j"])
    for support in geometry.get("supports", []):
        node_ids.add(support["node"])

    nodes = []
    for node in geometry["nodes"]:
        if node["id"] not in node_ids:
            continue
        if None in [node["x"], node["y"], node["z"]]:
            continue
        nodes.append({"id": node["id"], "x": node["x"], "y": node["y"], "z": node["z"]})

    valid_node_ids = {node["id"] for node in nodes}
    elements = []
    element_id = 1

    for column in geometry["columns"]:
        if column["node_i"] not in valid_node_ids or column["node_j"] not in valid_node_ids:
            continue
        elements.append({
            "id": element_id,
            "type": "columna",
            "nodeI": column["node_i"],
            "nodeJ": column["node_j"],
            "uniformLoad": 0.0,
            "axialI": 0.0,
            "axialJ": 0.0,
            "shearI": 0.0,
            "shearJ": 0.0,
            "momentI": 0.0,
            "momentJ": 0.0,
        })
        element_id += 1

    for collection_name in ["beams", "foundation_beams"]:
        for beam in geometry[collection_name]:
            if beam["node_i"] not in valid_node_ids or beam["node_j"] not in valid_node_ids:
                continue
            elements.append({
                "id": element_id,
                "type": "viga",
                "nodeI": beam["node_i"],
                "nodeJ": beam["node_j"],
                "uniformLoad": 0.0,
                "axialI": 0.0,
                "axialJ": 0.0,
                "shearI": 0.0,
                "shearJ": 0.0,
                "momentI": 0.0,
                "momentJ": 0.0,
            })
            element_id += 1

    supports = []
    for support in geometry.get("supports", []):
        if support["node"] not in valid_node_ids:
            continue
        supports.append({
            "node": support["node"],
            "type": support["type"],
            "ux": support["ux"],
            "uy": support["uy"],
            "uz": support["uz"],
            "rx": support["rx"],
            "ry": support["ry"],
            "rz": support["rz"],
        })

    diaphragms = []
    for diaphragm in geometry.get("rigid_diaphragms", []):
        diaphragms.append({
            "id": diaphragm["id"],
            "level": diaphragm["level"],
            "z": diaphragm["z"],
            "x1": diaphragm["x1"],
            "x2": diaphragm["x2"],
            "y1": diaphragm["y1"],
            "y2": diaphragm["y2"],
            "type": diaphragm["type"],
        })

    data = {
        "units": "m, kN, kN*m",
        "nodes": nodes,
        "elements": elements,
        "pointLoads": [],
        "supports": supports,
        "rigidDiaphragms": diaphragms,
    }

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as file:
        json.dump(data, file, indent=2)

    return output_path
