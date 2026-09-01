import openseespy.opensees as ops


def build_opensees_nodes_only(geometry):
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)

    created_nodes = []
    skipped_nodes = []

    for node in geometry["nodes"]:
        if None in [node["x"], node["y"], node["z"]]:
            skipped_nodes.append(node["id"])
            continue
        ops.node(node["id"], node["x"], node["y"], node["z"])
        created_nodes.append(node["id"])

    return {
        "created_nodes": created_nodes,
        "skipped_nodes_missing_coordinates": skipped_nodes,
        "elements_created": 0,
        "note": "Elements are not created until E, A, G, J, Iy and Iz are explicitly provided.",
    }
