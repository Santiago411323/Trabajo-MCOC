from pathlib import Path

import plotly.graph_objects as go


def valid_xy(item):
    return item.get("x") is not None and item.get("y") is not None


def hover_text(item, item_type):
    parts = [f"ID: {item.get('id')}", f"TYPE: {item_type}"]
    for key in ["x", "y", "level", "width", "height", "length", "thickness", "bx", "by"]:
        if key in item:
            parts.append(f"{key.upper()}: {item.get(key)}")
    return "<br>".join(parts)


def create_viewer_2d(geometry, output_path):
    fig = go.Figure()

    for axis_name, x in geometry["grids"]["x"].items():
        if x is not None:
            fig.add_trace(go.Scatter(x=[x, x], y=[-1, 1], mode="lines+text", name=f"GRID X {axis_name}", text=[axis_name, ""], line=dict(color="lightgray", dash="dash")))

    for axis_name, y in geometry["grids"]["y"].items():
        if y is not None:
            fig.add_trace(go.Scatter(x=[-1, 1], y=[y, y], mode="lines+text", name=f"GRID Y {axis_name}", text=[axis_name, ""], line=dict(color="lightgray", dash="dash")))

    nodes = [node for node in geometry["nodes"] if valid_xy(node)]
    if nodes:
        fig.add_trace(go.Scatter(
            x=[node["x"] for node in nodes],
            y=[node["y"] for node in nodes],
            mode="markers+text",
            name="NODES",
            text=[str(node["id"]) for node in nodes],
            hovertext=[hover_text(node, "NODE") for node in nodes],
            hoverinfo="text",
        ))

    node_map = {node["id"]: node for node in geometry["nodes"]}
    for collection_name, color in [("beams", "blue"), ("foundation_beams", "orange")]:
        for element in geometry[collection_name]:
            ni = node_map.get(element["node_i"])
            nj = node_map.get(element["node_j"])
            if not ni or not nj or not valid_xy(ni) or not valid_xy(nj):
                continue
            fig.add_trace(go.Scatter(
                x=[ni["x"], nj["x"]],
                y=[ni["y"], nj["y"]],
                mode="lines",
                name=collection_name.upper(),
                line=dict(color=color, width=4),
                hovertext=hover_text(element, element["type"]),
                hoverinfo="text",
            ))

    for foundation in geometry["foundations"]:
        if foundation["center_x"] is not None and foundation["center_y"] is not None:
            fig.add_trace(go.Scatter(
                x=[foundation["center_x"]],
                y=[foundation["center_y"]],
                mode="markers",
                name="FOUNDATIONS",
                marker=dict(symbol="square", size=14, color="brown"),
                hovertext=hover_text(foundation, foundation["type"]),
                hoverinfo="text",
            ))

    for wall in geometry["walls"]:
        if None not in [wall["x1"], wall["y1"], wall["x2"], wall["y2"]]:
            fig.add_trace(go.Scatter(
                x=[wall["x1"], wall["x2"]],
                y=[wall["y1"], wall["y2"]],
                mode="lines",
                name="WALLS",
                line=dict(color="red", width=6),
                hovertext=hover_text(wall, wall["type"]),
                hoverinfo="text",
            ))

    fig.update_layout(
        title="UANDES structural geometry - 2D viewer",
        xaxis=dict(title="X [m]", scaleanchor="y", scaleratio=1),
        yaxis=dict(title="Y [m]"),
        legend=dict(groupclick="toggleitem"),
    )

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.write_html(output_path)
    return output_path
