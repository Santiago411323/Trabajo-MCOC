from pathlib import Path

import plotly.graph_objects as go


def add_line(fig, p1, p2, name, color, width=5, visible=True, hovertext=""):
    if None in [*p1, *p2]:
        return
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


def box_mesh(center_x, center_y, width, length, z_top, height):
    if None in [center_x, center_y, width, length, z_top, height]:
        return None
    x0, x1 = center_x - width / 2, center_x + width / 2
    y0, y1 = center_y - length / 2, center_y + length / 2
    z0, z1 = z_top - height, z_top
    vertices = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    faces = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return vertices, faces


def add_box(fig, mesh_data, name, color, hovertext=""):
    if mesh_data is None:
        return
    vertices, faces = mesh_data
    x, y, z = zip(*vertices)
    i, j, k = zip(*faces)
    fig.add_trace(go.Mesh3d(x=x, y=y, z=z, i=i, j=j, k=k, name=name, color=color, opacity=0.55, hovertext=hovertext, hoverinfo="text"))


def create_viewer_3d(geometry, output_path):
    fig = go.Figure()
    node_map = {node["id"]: node for node in geometry["nodes"]}

    for node in geometry["nodes"]:
        if None in [node["x"], node["y"], node["z"]]:
            continue
        fig.add_trace(go.Scatter3d(x=[node["x"]], y=[node["y"]], z=[node["z"]], mode="markers+text", name="NODES", text=[str(node["id"])], marker=dict(size=4), hovertext=f"ID: {node['id']}<br>TYPE: NODE<br>LEVEL: {node['level']}", hoverinfo="text"))

    for column in geometry["columns"]:
        ni = node_map.get(column["node_i"])
        nj = node_map.get(column["node_j"])
        if ni and nj:
            add_line(fig, (ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]), "COLUMNS", "black", hovertext=f"ID: {column['id']}<br>TYPE: COLUMN")

    for beam in geometry["beams"]:
        ni = node_map.get(beam["node_i"])
        nj = node_map.get(beam["node_j"])
        if ni and nj:
            add_line(fig, (ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]), "BEAMS", "blue", hovertext=f"ID: {beam['id']}<br>TYPE: BEAM")

    for fb in geometry["foundation_beams"]:
        ni = node_map.get(fb["node_i"])
        nj = node_map.get(fb["node_j"])
        if ni and nj:
            add_line(fig, (ni["x"], ni["y"], ni["z"]), (nj["x"], nj["y"], nj["z"]), "FOUNDATION BEAMS", "orange", hovertext=f"ID: {fb['id']}<br>TYPE: FOUNDATION_BEAM")

    for foundation in geometry["foundations"]:
        add_box(fig, box_mesh(foundation["center_x"], foundation["center_y"], foundation["width"], foundation["length"], geometry["levels"]["FOUNDATION"], foundation["thickness"]), "FOUNDATIONS", "brown", hovertext=f"ID: {foundation['id']}<br>TYPE: {foundation['type']}")

    for radier in geometry["radiers"]:
        boundary = radier["boundary"]
        if len(boundary) >= 3 and radier["z_top"] is not None:
            # Radier polygon meshing is intentionally deferred until real boundary points are supplied.
            pass

    fig.update_layout(
        title="UANDES structural geometry - 3D viewer",
        scene=dict(xaxis_title="X [m]", yaxis_title="Y [m]", zaxis_title="Z [m]", aspectmode="data"),
        updatemenus=[dict(buttons=[
            dict(label="SHOW ALL", method="update", args=[{"visible": [True] * len(fig.data)}]),
            dict(label="FOUNDATIONS ONLY", method="update", args=[{"visible": [trace.name in ["FOUNDATIONS", "FOUNDATION BEAMS", "NODES"] for trace in fig.data]}]),
            dict(label="BASEMENT ONLY", method="update", args=[{"visible": [True] * len(fig.data)}]),
            dict(label="LEVEL 1 ONLY", method="update", args=[{"visible": [trace.name in ["BEAMS", "NODES", "COLUMNS"] for trace in fig.data]}]),
        ])]
    )

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.write_html(output_path)
    return output_path
