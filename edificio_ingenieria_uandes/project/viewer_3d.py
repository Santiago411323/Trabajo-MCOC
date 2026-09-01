from pathlib import Path
from math import hypot

import plotly.graph_objects as go


def add_line(fig, p1, p2, name, color, width=5, visible=True, hovertext=""):
    if None in [*p1, *p2]:
        return False
    fig.add_trace(go.Scatter3d(
        x=[p1[0], p2[0]],
        y=[p1[1], p2[1]],
        z=[p1[2], p2[2]],
        mode="lines",
        name=name,
        line=dict(color=color, width=width),
        visible=visible,
        hovertext=hovertext,
        hoverinfo="text",
    ))
    return True


def box_mesh(center_x, center_y, width, length, z_top, height):
    if None in [center_x, center_y, width, length, z_top, height]:
        return None
    x0, x1 = center_x - width / 2, center_x + width / 2
    y0, y1 = center_y - length / 2, center_y + length / 2
    z0, z1 = z_top - height, z_top
    vertices = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return vertices, faces


def add_box(fig, mesh_data, name, color, hovertext="", opacity=0.55):
    if mesh_data is None:
        return False
    vertices, faces = mesh_data
    x, y, z = zip(*vertices)
    i, j, k = zip(*faces)
    fig.add_trace(go.Mesh3d(x=x, y=y, z=z, i=i, j=j, k=k, name=name, color=color, opacity=opacity, hovertext=hovertext, hoverinfo="text"))
    return True


def wall_mesh(wall):
    if None in [wall["x1"], wall["y1"], wall["x2"], wall["y2"], wall["thickness"], wall["z_bottom"], wall["z_top"]]:
        return None
    dx = wall["x2"] - wall["x1"]
    dy = wall["y2"] - wall["y1"]
    length = hypot(dx, dy)
    if length <= 0:
        return None
    nx = -dy / length * wall["thickness"] / 2
    ny = dx / length * wall["thickness"] / 2
    z0 = wall["z_bottom"]
    z1 = wall["z_top"]
    vertices = [
        (wall["x1"] + nx, wall["y1"] + ny, z0),
        (wall["x1"] - nx, wall["y1"] - ny, z0),
        (wall["x2"] - nx, wall["y2"] - ny, z0),
        (wall["x2"] + nx, wall["y2"] + ny, z0),
        (wall["x1"] + nx, wall["y1"] + ny, z1),
        (wall["x1"] - nx, wall["y1"] - ny, z1),
        (wall["x2"] - nx, wall["y2"] - ny, z1),
        (wall["x2"] + nx, wall["y2"] + ny, z1),
    ]
    faces = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return vertices, faces


def beam_mesh(beam, start, end):
    if None in [beam["width"], beam["height"], *start, *end]:
        return None
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    length = hypot(dx, dy)
    if length <= 0:
        return None
    nx = -dy / length * beam["width"] / 2
    ny = dx / length * beam["width"] / 2
    z0 = start[2] - beam["height"] / 2
    z1 = start[2] + beam["height"] / 2
    vertices = [
        (start[0] + nx, start[1] + ny, z0),
        (start[0] - nx, start[1] - ny, z0),
        (end[0] - nx, end[1] - ny, z0),
        (end[0] + nx, end[1] + ny, z0),
        (start[0] + nx, start[1] + ny, z1),
        (start[0] - nx, start[1] - ny, z1),
        (end[0] - nx, end[1] - ny, z1),
        (end[0] + nx, end[1] + ny, z1),
    ]
    faces = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return vertices, faces


def slab_mesh(slab, reinforcement):
    if None in [slab["x1"], slab["x2"], slab["y1"], slab["y2"], slab["z_top"], slab["thickness"]]:
        return None
    x0, x1 = sorted([slab["x1"], slab["x2"]])
    y0, y1 = sorted([slab["y1"], slab["y2"]])
    if reinforcement == "BOTTOM_F":
        z0 = slab["z_top"] - slab["thickness"]
        z1 = z0 + 0.015
    else:
        z1 = slab["z_top"]
        z0 = z1 - 0.015
    vertices = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return vertices, faces


def create_viewer_3d(geometry, output_path):
    fig = go.Figure()
    node_map = {node["id"]: node for node in geometry["nodes"]}
    trace_levels = []
    trace_parts = []

    def register(levels, part="STRUCTURE"):
        trace_levels.append(set(levels))
        trace_parts.append(part)

    def level_from_z(z):
        for level_name, level_z in geometry["levels"].items():
            if z == level_z:
                return level_name
        return None

    for node in geometry["nodes"]:
        if None in [node["x"], node["y"], node["z"]]:
            continue
        fig.add_trace(go.Scatter3d(x=[node["x"]], y=[node["y"]], z=[node["z"]], mode="markers+text", name="NODES", text=[str(node["id"])], marker=dict(size=4), hovertext=f"ID: {node['id']}<br>TYPE: NODE<br>LEVEL: {node['level']}", hoverinfo="text"))
        register([node["level"]])

    for column in geometry["columns"]:
        ni = node_map.get(column["node_i"])
        nj = node_map.get(column["node_j"])
        if ni and nj:
            if None not in [column["x_center"], column["y_center"], column["bx"], column["by"], column["z_top"]] and column["z_bottom"] is not None:
                if add_box(fig, box_mesh(column["x_center"], column["y_center"], column["bx"], column["by"], column["z_top"], column["z_top"] - column["z_bottom"]), "COLUMNS", "black", hovertext=f"ID: {column['id']}<br>TYPE: COLUMN<br>DIM: {column['bx']} x {column['by']} m"):
                    register([ni["level"], nj["level"]])
            else:
                if add_line(fig, (ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]), "COLUMNS", "black", hovertext=f"ID: {column['id']}<br>TYPE: COLUMN"):
                    register([ni["level"], nj["level"]])

    for beam in geometry["beams"]:
        ni = node_map.get(beam["node_i"])
        nj = node_map.get(beam["node_j"])
        if ni and nj:
            start = (ni["x"], ni["y"], ni["z"])
            end = (nj["x"], nj["y"], nj["z"])
            hovertext = f"ID: {beam['id']}<br>TYPE: BEAM<br>LEVEL: {beam['level']}<br>SECTION: {beam['width']} x {beam['height']} m"
            if add_box(fig, beam_mesh(beam, start, end), "BEAMS", "blue", hovertext=hovertext, opacity=0.75):
                register([beam["level"]])
            elif add_line(fig, start, end, "BEAMS", "blue", hovertext=hovertext):
                register([beam["level"]])

    for slab in geometry.get("slabs", []):
        if "BOTTOM_F" in slab["reinforcement"]:
            if add_box(fig, slab_mesh(slab, "BOTTOM_F"), "LOSAS INFERIORES", "cyan", hovertext=f"ID: {slab['id']}<br>TYPE: SLAB<br>LEVEL: {slab['level']}<br>THICKNESS: {slab['thickness']} m<br>REINF: BOTTOM_F", opacity=0.32):
                register([slab["level"]], "SLAB_BOTTOM")
        if "CEILING" in slab["reinforcement"]:
            if add_box(fig, slab_mesh(slab, "CEILING"), "LOSAS SUPERIORES", "deepskyblue", hovertext=f"ID: {slab['id']}<br>TYPE: SLAB<br>LEVEL: {slab['level']}<br>THICKNESS: {slab['thickness']} m<br>REINF: CEILING", opacity=0.22):
                register([slab["level"]], "SLAB_TOP")

    for wall in geometry["walls"]:
        if None in [wall["x1"], wall["y1"]]:
            continue
        z_bottom = wall["z_bottom"] if wall["z_bottom"] is not None else 0.0
        z_top = wall["z_top"] if wall["z_top"] is not None else z_bottom + 0.5
        if wall["x1"] == wall["x2"] and wall["y1"] == wall["y2"]:
            fig.add_trace(go.Scatter3d(
                x=[wall["x1"]],
                y=[wall["y1"]],
                z=[z_bottom],
                mode="markers+text",
                name="WALLS",
                text=[wall["id"]],
                marker=dict(size=8, symbol="diamond", color="red"),
                hovertext=f"ID: {wall['id']}<br>TYPE: STRUCTURAL_WALL<br>STATUS: pending length/thickness",
                hoverinfo="text",
            ))
            register([level_from_z(z_top) or "FOUNDATION"])
        else:
            if add_box(fig, wall_mesh(wall), "WALLS", "red", hovertext=f"ID: {wall['id']}<br>TYPE: STRUCTURAL_WALL<br>THICKNESS: {wall['thickness']} m"):
                register([level_from_z(wall["z_top"]) or "FOUNDATION"])

    for fb in geometry["foundation_beams"]:
        ni = node_map.get(fb["node_i"])
        nj = node_map.get(fb["node_j"])
        if ni and nj:
            if add_line(fig, (ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]), "FOUNDATION BEAMS", "orange", hovertext=f"ID: {fb['id']}<br>TYPE: FOUNDATION_BEAM"):
                register(["FOUNDATION"])

    for foundation in geometry["foundations"]:
        if add_box(fig, box_mesh(foundation["center_x"], foundation["center_y"], foundation["width"], foundation["length"], geometry["levels"]["FOUNDATION"], foundation["thickness"]), "FOUNDATIONS", "brown", hovertext=f"ID: {foundation['id']}<br>TYPE: {foundation['type']}"):
            register(["FOUNDATION"])

    for radier in geometry["radiers"]:
        boundary = radier["boundary"]
        if len(boundary) >= 3 and radier["z_top"] is not None:
            # Radier polygon meshing is intentionally deferred until real boundary points are supplied.
            pass

    level_buttons = [
        ("FOUNDATIONS ONLY", "FOUNDATION"),
        ("CIELO 1S", "CIELO_1S"),
        ("CIELO 1", "CIELO_1"),
        ("CIELO 2", "CIELO_2"),
        ("CIELO 3", "CIELO_3"),
        ("CIELO 4", "CIELO_4"),
    ]

    buttons = [dict(label="SHOW ALL", method="update", args=[{"visible": [True] * len(fig.data)}])]
    for label, level in level_buttons:
        buttons.append(dict(
            label=label,
            method="update",
            args=[{"visible": [level in levels for levels in trace_levels]}],
        ))

    slab_buttons = [
        dict(label="LOSAS ON", method="update", args=[{"visible": [True] * len(fig.data)}]),
        dict(label="SIN LOSAS", method="update", args=[{"visible": [part not in ["SLAB_BOTTOM", "SLAB_TOP"] for part in trace_parts]}]),
        dict(label="SOLO LOSAS INF", method="update", args=[{"visible": [part == "SLAB_BOTTOM" or part == "STRUCTURE" for part in trace_parts]}]),
        dict(label="SOLO LOSAS SUP", method="update", args=[{"visible": [part == "SLAB_TOP" or part == "STRUCTURE" for part in trace_parts]}]),
    ]

    fig.update_layout(
        title="UANDES structural geometry - 3D viewer",
        scene=dict(xaxis_title="X [m]", yaxis_title="Y [m]", zaxis_title="Z [m]", aspectmode="data"),
        updatemenus=[
            dict(buttons=buttons, x=0.0, y=1.12),
            dict(buttons=slab_buttons, x=0.38, y=1.12),
        ]
    )

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.write_html(output_path)
    return output_path
