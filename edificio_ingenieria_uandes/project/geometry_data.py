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

# Altura repetida informada por el usuario: 396 cm = 3.96 m.
FLOOR_HEIGHT = 3.96

CIELO_1S_Z = -4.01
CIELO_1_Z = -0.05
CIELO_2_Z = 3.91
CIELO_3_Z = 7.87
CIELO_4_Z = 11.83

levels = {
    "FOUNDATION": CIELO_1S_Z - RADIER_THICKNESS,
    "CIELO_1S": CIELO_1S_Z,
    "CIELO_1": CIELO_1_Z,
    "CIELO_2": CIELO_2_Z,
    "CIELO_3": CIELO_3_Z,
    "CIELO_4": CIELO_4_Z,
}

LEVEL_NODE_BASE = {
    "FOUNDATION": 1000,
    "CIELO_1S": 2000,
    "CIELO_1": 3000,
    "CIELO_2": 4000,
    "CIELO_3": 5000,
    "CIELO_4": 6000,
}

VERTICAL_LEVEL_SEQUENCE = ["FOUNDATION", "CIELO_1S", "CIELO_1", "CIELO_2", "CIELO_3", "CIELO_4"]
FLOOR_BEAM_LEVELS = ["CIELO_1S", "CIELO_1", "CIELO_2", "CIELO_3", "CIELO_4"]

# Ejes de planta. Se agregan ejes auxiliares solo donde el usuario definio vigas
# desplazadas desde ejes principales.
GRID_X_AXIS_NAMES = [
    "A_PRIME",
    "A",
    "A_B_MID",
    "B",
    "B_C_MID",
    "C",
    "C_CPRIME_500",
    "C_PRIME",
    "D",
    "D_PRIME",
]

GRID_Y_AXIS_NAMES = ["1", "1A_PRIME", "2", "2A_PRIME", "3"]

# Distancias entregadas por el usuario, convertidas de cm a m.
GRID_X_SPANS = [3.75, 3.75, 3.75, 5.00, 5.00, 5.00, 2.58, 2.42, 0.225]
GRID_Y_SPANS = [4.265, 4.635, 2.985, 4.265]

grid_x = {}
grid_y = {}

COLUMN_BX = 0.70
COLUMN_BY = 0.70

# Muro eje D con apertura.
D_WALL_OPENING_START_FROM_COLUMN_EDGE = 5.75
D_WALL_OPENING_LENGTH = 2.40

# Muros de esquina interpretados desde las imagenes enviadas por el usuario.
AP1_VERTICAL_WALL_THICKNESS = 0.60
AP1_VERTICAL_WALL_DOWN = 1.095
AP1_VERTICAL_WALL_UP = 1.825
AP1_HORIZONTAL_WALL_THICKNESS = 0.30
AP1_HORIZONTAL_WALL_LENGTH = 1.45

AP3_VERTICAL_WALL_THICKNESS = 0.60
AP3_VERTICAL_WALL_DOWN = 1.825
AP3_VERTICAL_WALL_UP = 1.09
AP3_HORIZONTAL_WALL_THICKNESS = 0.30
AP3_HORIZONTAL_WALL_LENGTH = 1.85

STRUCTURAL_POSITIONS = [
    ("P01", "B", "1"),
    ("P02", "C", "1"),
    ("P03", "D", "1"),
    ("P04", "A", "2"),
    ("P05", "B", "2"),
    ("P06", "C", "2"),
    ("P07", "B", "3"),
    ("P08", "C", "3"),
]

SUPERSTRUCTURE_BEAM_SPECS = [
    ("A_PRIME", "1", "A_PRIME", "3", 0.40, 0.80, "V40/80"),
    ("A_PRIME", "1", "D", "1", 0.60, 0.80, "V60/80"),
    ("A_PRIME", "3", "D", "3", 0.60, 0.80, "V60/80"),
    ("A", "1", "A", "3", 0.60, 0.80, "V60/80"),
    ("A_B_MID", "1", "A_B_MID", "3", 0.60, 0.80, "V60/80"),
    ("B", "1", "B", "3", 0.60, 0.80, "V60/80"),
    ("B_C_MID", "1", "B_C_MID", "3", 0.60, 0.80, "V60/80"),
    ("C", "1", "C", "3", 0.60, 0.80, "V60/80"),
    ("C_CPRIME_500", "1", "C_CPRIME_500", "3", 0.60, 0.80, "V60/80"),
    ("A_PRIME", "1A_PRIME", "A_B_MID", "1A_PRIME", 0.30, 0.80, "V30/80"),
    ("A", "2", "D", "2", 0.60, 0.80, "V60/80"),
    ("A_PRIME", "2A_PRIME", "A_B_MID", "2A_PRIME", 0.30, 0.80, "V30/80"),
]


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


def add_wall_for_each_storey(walls, wall_id, gx1, gy1, gx2, gy2, x1, y1, x2, y2, thickness):
    for level_index, (bottom_level, top_level) in enumerate(zip(VERTICAL_LEVEL_SEQUENCE, VERTICAL_LEVEL_SEQUENCE[1:]), start=1):
        walls.append(StructuralWall(
            id=f"{wall_id}_L{level_index}",
            grid_x1=gx1,
            grid_y1=gy1,
            grid_x2=gx2,
            grid_y2=gy2,
            x1=x1,
            y1=y1,
            x2=x2,
            y2=y2,
            thickness=thickness,
            z_bottom=levels[bottom_level],
            z_top=levels[top_level],
            status="ACTIVE",
        ))


def create_geometry():
    refresh_grids()

    nodes = []
    columns = []
    beams = []
    foundations = []
    foundation_beams = []
    walls = []
    radiers = []

    node_registry = {}
    next_node_index_by_level = {level: 1 for level in levels}

    def get_or_create_node(gx, gy, level):
        key = (gx, gy, level)
        if key in node_registry:
            return node_registry[key]

        node_id = LEVEL_NODE_BASE[level] + next_node_index_by_level[level]
        next_node_index_by_level[level] += 1
        node = structural_node(node_id, gx, gy, level)
        nodes.append(node)
        node_registry[key] = node_id
        return node_id

    position_to_nodes = {}

    for idx, (position_id, gx, gy) in enumerate(STRUCTURAL_POSITIONS, start=1):
        position_to_nodes[position_id] = {}

        for level in VERTICAL_LEVEL_SEQUENCE:
            position_to_nodes[position_id][level] = get_or_create_node(gx, gy, level)

        n_foundation = structural_node(position_to_nodes[position_id]["FOUNDATION"], gx, gy, "FOUNDATION")

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

        for level_index, (bottom_level, top_level) in enumerate(zip(VERTICAL_LEVEL_SEQUENCE, VERTICAL_LEVEL_SEQUENCE[1:]), start=1):
            columns.append(Column(
                id=f"C{level_index}{idx:03d}",
                grid_x=gx,
                grid_y=gy,
                x_center=n_foundation.x,
                y_center=n_foundation.y,
                bx=COLUMN_BX,
                by=COLUMN_BY,
                z_bottom=levels[bottom_level],
                z_top=levels[top_level],
                node_i=position_to_nodes[position_id][bottom_level],
                node_j=position_to_nodes[position_id][top_level],
                status="ACTIVE",
            ))

    beam_id = 3001
    for level in FLOOR_BEAM_LEVELS:
        for gx1, gy1, gx2, gy2, width, height, section_name in SUPERSTRUCTURE_BEAM_SPECS:
            beams.append(Beam(
                id=f"B{beam_id}_{section_name}",
                node_i=get_or_create_node(gx1, gy1, level),
                node_j=get_or_create_node(gx2, gy2, level),
                width=width,
                height=height,
                level=level,
                status="ACTIVE",
            ))
            beam_id += 1

    fb_id = 1001
    for row in ["1", "2", "3"]:
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

    d_x = grid_x.get("D")
    a_prime_x = grid_x.get("A_PRIME")
    c_prime_x = grid_x.get("C_PRIME")
    y_1 = grid_y.get("1")
    y_1a_prime = grid_y.get("1A_PRIME")
    y_3 = grid_y.get("3")

    if None not in [d_x, y_1, y_3]:
        opening_start_y = y_1 + COLUMN_BY / 2 + D_WALL_OPENING_START_FROM_COLUMN_EDGE
        opening_end_y = opening_start_y + D_WALL_OPENING_LENGTH
        add_wall_for_each_storey(walls, "W_D_1_TO_OPENING", "D", "1", "D", "OPENING_START", d_x, y_1, d_x, opening_start_y, 0.25)
        add_wall_for_each_storey(walls, "W_D_OPENING_TO_3", "D", "OPENING_END", "D", "3", d_x, opening_end_y, d_x, y_3, 0.25)

    if None not in [d_x, c_prime_x, y_1a_prime]:
        add_wall_for_each_storey(walls, "W_D_1A_TO_CPRIME_1A", "D", "1A_PRIME", "C_PRIME", "1A_PRIME", d_x, y_1a_prime, c_prime_x, y_1a_prime, 0.30)
        add_wall_for_each_storey(walls, "W_CPRIME_1A_TOWARD_2", "C_PRIME", "1A_PRIME", "C_PRIME", "TOWARD_2_282CM", c_prime_x, y_1a_prime, c_prime_x, y_1a_prime + 2.82, 0.25)

    if None not in [a_prime_x, y_1]:
        add_wall_for_each_storey(walls, "W_APRIME_1_VERTICAL", "A_PRIME", "A1_DOWN", "A_PRIME", "A1_UP", a_prime_x, y_1 - AP1_VERTICAL_WALL_DOWN, a_prime_x, y_1 + AP1_VERTICAL_WALL_UP, AP1_VERTICAL_WALL_THICKNESS)
        add_wall_for_each_storey(walls, "W_APRIME_1_HORIZONTAL", "A_PRIME", "1", "A1_ARM", "1", a_prime_x, y_1, a_prime_x + AP1_HORIZONTAL_WALL_LENGTH, y_1, AP1_HORIZONTAL_WALL_THICKNESS)

    if None not in [a_prime_x, y_3]:
        add_wall_for_each_storey(walls, "W_APRIME_3_VERTICAL", "A_PRIME", "A3_DOWN", "A_PRIME", "A3_UP", a_prime_x, y_3 - AP3_VERTICAL_WALL_DOWN, a_prime_x, y_3 + AP3_VERTICAL_WALL_UP, AP3_VERTICAL_WALL_THICKNESS)
        add_wall_for_each_storey(walls, "W_APRIME_3_HORIZONTAL", "A_PRIME", "3", "A3_ARM", "3", a_prime_x, y_3, a_prime_x + AP3_HORIZONTAL_WALL_LENGTH, y_3, AP3_HORIZONTAL_WALL_THICKNESS)

    radiers.append(Radier(id="R001", boundary=[], z_top=levels["FOUNDATION"]))

    return {
        "metadata": {
            "name": "UANDES Structural Model",
            "units": {"length": "m", "force": "kN"},
            "geometry_tolerance": GEOMETRY_TOLERANCE,
            "note": "Geometry is parametric. Missing dimensions are kept as null and are not invented.",
            "plant_levels": {
                "FOUNDATION": "Planta de fundaciones",
                "CIELO_1S": "Planta cielo 1 subterraneo",
                "CIELO_1": "Planta cielo piso 1",
                "CIELO_2": "Planta cielo piso 2",
                "CIELO_3": "Planta cielo piso 3",
                "CIELO_4": "Planta cielo piso 4",
            },
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
