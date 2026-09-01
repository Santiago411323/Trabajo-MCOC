from geometry import Beam, Column, Foundation, FoundationBeam, Node, Radier, StructuralWall, to_dict_list


GEOMETRY_TOLERANCE = 0.005

H20 = 0.20
H60 = 0.60
H100 = 1.00
H120 = 1.20

FOUNDATION_HEIGHTS = {
    "h20": H20,
    "h60": H60,
    "h100": H100,
    "h120": H120,
}

RADIER_THICKNESS = 0.15

ISOLATED_FOOTING = "ISOLATED_FOOTING"
STRIP_FOOTING = "STRIP_FOOTING"
FOUNDATION_BEAM = "FOUNDATION_BEAM"
FOUNDATION_WALL = "FOUNDATION_WALL"
RADIER = "RADIER"

# These values are intentionally None until exact elevations are provided.
basement_z = None
level1_z = None

levels = {
    "FOUNDATION": 0.0,
    "BASEMENT": basement_z,
    "LEVEL_1": level1_z,
}

# Axis names identified from the written instructions. Spans remain empty until
# exact distances are provided by the user.
GRID_X_AXIS_NAMES = [
    "LEFT_PERIMETER",
    "INTERIOR_LEFT_CENTRAL",
    "INTERIOR_CENTRAL",
    "RIGHT_SECTOR",
    "RIGHT_PERIMETER",
]

GRID_Y_AXIS_NAMES = ["EJE_1", "EJE_2", "EJE_3"]

# Fill later with exact distances between consecutive axes, in meters.
GRID_X_SPANS = []
GRID_Y_SPANS = []

grid_x = {}
grid_y = {}


def generate_grid_coordinates(axis_names, spans):
    coordinates = {}

    if not axis_names:
        return coordinates

    coordinates[axis_names[0]] = 0.0
    current = 0.0

    for index, axis_name in enumerate(axis_names[1:], start=1):
        if index - 1 < len(spans):
            current += spans[index - 1]
            coordinates[axis_name] = current
        else:
            coordinates[axis_name] = None

    return coordinates


def refresh_grids():
    global grid_x, grid_y
    grid_x = generate_grid_coordinates(GRID_X_AXIS_NAMES, GRID_X_SPANS)
    grid_y = generate_grid_coordinates(GRID_Y_AXIS_NAMES, GRID_Y_SPANS)


def structural_node(node_id, grid_x_name, grid_y_name, level):
    x = grid_x.get(grid_x_name)
    y = grid_y.get(grid_y_name)
    z = levels.get(level)
    return Node(node_id, grid_x_name, grid_y_name, level, x, y, z)


STRUCTURAL_POSITIONS = [
    ("P01", "INTERIOR_LEFT_CENTRAL", "EJE_1"),
    ("P02", "INTERIOR_CENTRAL", "EJE_1"),
    ("P03", "RIGHT_SECTOR", "EJE_1"),
    ("P04", "INTERIOR_LEFT_CENTRAL", "EJE_2"),
    ("P05", "INTERIOR_CENTRAL", "EJE_2"),
    ("P06", "RIGHT_SECTOR", "EJE_2"),
    ("P07", "INTERIOR_LEFT_CENTRAL", "EJE_3"),
    ("P08", "INTERIOR_CENTRAL", "EJE_3"),
    ("P09", "RIGHT_SECTOR", "EJE_3"),
]

PERIMETER_WALLS = [
    ("W_LEFT", "LEFT_PERIMETER", "EJE_1", "LEFT_PERIMETER", "EJE_3"),
    ("W_RIGHT", "RIGHT_PERIMETER", "EJE_1", "RIGHT_PERIMETER", "EJE_3"),
]


def create_geometry():
    refresh_grids()

    nodes = []
    columns = []
    beams = []
    foundations = []
    foundation_beams = []
    walls = []
    radiers = []

    position_to_nodes = {}

    for idx, (position_id, gx, gy) in enumerate(STRUCTURAL_POSITIONS, start=1):
        foundation_node_id = 1000 + idx
        basement_node_id = 2000 + idx
        level1_node_id = 3000 + idx

        n_foundation = structural_node(foundation_node_id, gx, gy, "FOUNDATION")
        n_basement = structural_node(basement_node_id, gx, gy, "BASEMENT")
        n_level1 = structural_node(level1_node_id, gx, gy, "LEVEL_1")
        nodes.extend([n_foundation, n_basement, n_level1])

        position_to_nodes[position_id] = {
            "FOUNDATION": foundation_node_id,
            "BASEMENT": basement_node_id,
            "LEVEL_1": level1_node_id,
        }

        foundations.append(Foundation(
            id=f"F{idx:03d}",
            type=ISOLATED_FOOTING,
            center_x=n_foundation.x,
            center_y=n_foundation.y,
            width=None,
            length=None,
            thickness=None,
            level="FOUNDATION",
            supporting_element=position_id,
        ))

        columns.append(Column(
            id=f"C2{idx:03d}",
            grid_x=gx,
            grid_y=gy,
            x_center=n_foundation.x,
            y_center=n_foundation.y,
            bx=None,
            by=None,
            z_bottom=n_foundation.z,
            z_top=n_basement.z,
            node_i=foundation_node_id,
            node_j=basement_node_id,
        ))

        columns.append(Column(
            id=f"C3{idx:03d}",
            grid_x=gx,
            grid_y=gy,
            x_center=n_basement.x,
            y_center=n_basement.y,
            bx=None,
            by=None,
            z_bottom=n_basement.z,
            z_top=n_level1.z,
            node_i=basement_node_id,
            node_j=level1_node_id,
        ))

    beam_id = 3001
    for row in ["EJE_1", "EJE_2", "EJE_3"]:
        row_positions = [pos for pos in STRUCTURAL_POSITIONS if pos[2] == row]
        for left, right in zip(row_positions, row_positions[1:]):
            beams.append(Beam(
                id=f"B{beam_id}",
                node_i=position_to_nodes[left[0]]["LEVEL_1"],
                node_j=position_to_nodes[right[0]]["LEVEL_1"],
                width=None,
                height=None,
                level="LEVEL_1",
            ))
            beam_id += 1

    fb_id = 1001
    for row in ["EJE_1", "EJE_2", "EJE_3"]:
        row_positions = [pos for pos in STRUCTURAL_POSITIONS if pos[2] == row]
        for left, right in zip(row_positions, row_positions[1:]):
            foundation_beams.append(FoundationBeam(
                id=f"FB{fb_id}",
                node_i=position_to_nodes[left[0]]["FOUNDATION"],
                node_j=position_to_nodes[right[0]]["FOUNDATION"],
                width=None,
                height=None,
                z=levels["FOUNDATION"],
            ))
            fb_id += 1

    for wall_id, gx1, gy1, gx2, gy2 in PERIMETER_WALLS:
        walls.append(StructuralWall(
            id=wall_id,
            grid_x1=gx1,
            grid_y1=gy1,
            grid_x2=gx2,
            grid_y2=gy2,
            x1=grid_x.get(gx1),
            y1=grid_y.get(gy1),
            x2=grid_x.get(gx2),
            y2=grid_y.get(gy2),
            thickness=None,
            z_bottom=levels["FOUNDATION"],
            z_top=levels["LEVEL_1"],
        ))

    # No radier polygon is assumed until an explicit boundary is provided.
    radiers.append(Radier(id="R001", boundary=[], z_top=levels["FOUNDATION"]))

    return {
        "metadata": {
            "name": "UANDES Structural Model",
            "units": {"length": "m", "force": "kN"},
            "geometry_tolerance": GEOMETRY_TOLERANCE,
            "note": "Geometry is parametric. Missing dimensions are kept as null and are not invented.",
        },
        "levels": levels,
        "grids": {"x": grid_x, "y": grid_y},
        "constants": {
            "foundation_heights": FOUNDATION_HEIGHTS,
            "radier_thickness": RADIER_THICKNESS,
        },
        "nodes": to_dict_list(nodes),
        "columns": to_dict_list(columns),
        "beams": to_dict_list(beams),
        "walls": to_dict_list(walls),
        "foundations": to_dict_list(foundations),
        "foundation_beams": to_dict_list(foundation_beams),
        "radiers": to_dict_list(radiers),
    }
