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
    trace_levels = []
    all_levels = ["FOUNDATION", "CIELO_1S", "CIELO_1", "CIELO_2", "CIELO_3", "CIELO_4"]

    def register(levels):
        trace_levels.append(set(levels))

    def level_from_z(z):
        for level_name, level_z in geometry["levels"].items():
            if z == level_z:
                return level_name
        return None

    x_values = [x for x in geometry["grids"]["x"].values() if x is not None]
    y_values = [y for y in geometry["grids"]["y"].values() if y is not None]
    xmin, xmax = (min(x_values) - 1, max(x_values) + 1) if x_values else (-1, 1)
    ymin, ymax = (min(y_values) - 1, max(y_values) + 1) if y_values else (-1, 1)

    for axis_name, x in geometry["grids"]["x"].items():
        if x is None:
            continue
        fig.add_trace(go.Scatter(
            x=[x, x],
            y=[ymin, ymax],
            mode="lines+text",
            name=f"GRID X {axis_name}",
            text=[axis_name, ""],
            line=dict(color="lightgray", dash="dash"),
        ))
        register(all_levels)

    for axis_name, y in geometry["grids"]["y"].items():
        if y is None:
            continue
        fig.add_trace(go.Scatter(
            x=[xmin, xmax],
            y=[y, y],
            mode="lines+text",
            name=f"GRID Y {axis_name}",
            text=[axis_name, ""],
            line=dict(color="lightgray", dash="dash"),
        ))
        register(all_levels)

    nodes = [node for node in geometry["nodes"] if valid_xy(node)]
    for level in all_levels:
        level_nodes = [node for node in nodes if node["level"] == level]
        if not level_nodes:
            continue
        fig.add_trace(go.Scatter(
            x=[node["x"] for node in level_nodes],
            y=[node["y"] for node in level_nodes],
            mode="markers+text",
            name=f"NODES {level}",
            text=[str(node["id"]) for node in level_nodes],
            hovertext=[hover_text(node, "NODE") for node in level_nodes],
            hoverinfo="text",
        ))
        register([level])

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
            register([element.get("level", "FOUNDATION")])

    for foundation in geometry["foundations"]:
        if foundation["center_x"] is None or foundation["center_y"] is None:
            continue
        fig.add_trace(go.Scatter(
            x=[foundation["center_x"]],
            y=[foundation["center_y"]],
            mode="markers",
            name="FOUNDATIONS",
            marker=dict(symbol="square", size=14, color="brown"),
            hovertext=hover_text(foundation, foundation["type"]),
            hoverinfo="text",
        ))
        register(["FOUNDATION"])

    for wall in geometry["walls"]:
        if None in [wall["x1"], wall["y1"], wall["x2"], wall["y2"]]:
            continue
        wall_level = level_from_z(wall["z_top"]) or "FOUNDATION"
        if wall["x1"] == wall["x2"] and wall["y1"] == wall["y2"]:
            fig.add_trace(go.Scatter(
                x=[wall["x1"]],
                y=[wall["y1"]],
                mode="markers+text",
                name="WALLS",
                text=[wall["id"]],
                marker=dict(symbol="diamond", size=16, color="red"),
                hovertext=hover_text(wall, wall["type"]),
                hoverinfo="text",
            ))
            register([wall_level])
            continue
        fig.add_trace(go.Scatter(
            x=[wall["x1"], wall["x2"]],
            y=[wall["y1"], wall["y2"]],
            mode="lines",
            name="WALLS",
            line=dict(color="red", width=6),
            hovertext=hover_text(wall, wall["type"]),
            hoverinfo="text",
        ))
        register([wall_level])

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

    fig.update_layout(
        title="UANDES structural geometry - 2D viewer",
        xaxis=dict(title="X [m]", scaleanchor="y", scaleratio=1),
        yaxis=dict(title="Y [m]"),
        legend=dict(groupclick="toggleitem"),
        updatemenus=[dict(buttons=buttons)],
    )

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.write_html(output_path)
    return output_path
