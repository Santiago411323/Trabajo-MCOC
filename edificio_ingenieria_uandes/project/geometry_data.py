from geometry import Beam, Column, Foundation, FoundationBeam, Node, Radier, Slab, Stair, StairWall, StructuralWall, to_dict_list
from math import hypot


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
EXTERIOR_RADIER_TOP_Z = -7.97
EXTERIOR_FOUNDATION_WALL_THICKNESS = 0.20
EXTERIOR_FOUNDATION_BEAM_WIDTH = 0.20
EXTERIOR_FOUNDATION_BEAM_HEIGHT = 1.505
STAIR_WIDTH = 4.30
STAIR_THICKNESS = 0.15
STAIR_FIRST_RUN = 4.17
STAIR_LANDING_RUN = 2.63
STAIR_FIRST_SLOPE = 0.5246
STAIR_SECOND_SLOPE = 0.5262
STAIR_ENTRY_EXTENSION = 1.22
STAIR_LANDING_Z = -5.945
STAIR_LANDING_WALL_TOP_Z = -5.15

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
SLAB_LEVELS = ["CIELO_1S", "CIELO_1", "CIELO_2", "CIELO_3", "CIELO_4"]
SLAB_THICKNESS = 0.15
SLAB_REINFORCEMENT_BY_LEVEL = {
    "CIELO_1S": ["BOTTOM_F", "CEILING"],
    "CIELO_1": ["BOTTOM_F", "CEILING"],
    "CIELO_2": ["BOTTOM_F", "CEILING"],
    "CIELO_3": ["BOTTOM_F", "CEILING"],
    "CIELO_4": ["BOTTOM_F", "CEILING"],
}
SLAB_LEVEL_PREFIX = {
    "CIELO_1S": "S",
    "CIELO_1": "1",
    "CIELO_2": "2",
    "CIELO_3": "3",
    "CIELO_4": "4",
}

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
    "B_PRIME",
    "E1",
]

GRID_Y_AXIS_NAMES = ["8B", "8A", "1", "1A_PRIME", "2", "2A_PRIME", "3"]
SLAB_GRID_X_AXIS_NAMES = ["A_PRIME", "A", "B", "C", "C_PRIME", "D", "D_PRIME"]
SLAB_GRID_Y_AXIS_NAMES = ["1", "1A_PRIME", "2", "2A_PRIME", "3"]

# Distancias entregadas por el usuario, convertidas de cm a m.
GRID_X_SPANS = [3.75, 3.75, 3.75, 5.00, 5.00, 5.00, 2.58, 2.42, 0.225]
GRID_Y_SPANS = [4.30, 6.42, 4.265, 4.635, 2.985, 4.265]

grid_x = {}
grid_y = {}

COLUMN_BX = 0.70
COLUMN_BY = 0.70

# Muro eje D' con apertura.
D_WALL_OPENING_START_FROM_GRID_1 = 6.15
D_WALL_OPENING_LENGTH = 2.40
ELEVATOR_TOP_WALL_Y = 3.205
ELEVATOR_WALL_LENGTH = 2.945
ELEVATOR_SLAB_VOID_PANELS = {
    ("C_PRIME", "D", "1A_PRIME", "2"),
    ("D", "D_PRIME", "1A_PRIME", "2"),
}

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
    ("P03", "D_PRIME", "1"),
    ("P04", "A", "2"),
    ("P05", "B", "2"),
    ("P06", "C", "2"),
    ("P07", "B", "3"),
    ("P08", "C", "3"),
]

EXTERIOR_NODE_X_AXES = ["B_PRIME", "E1"]
EXTERIOR_NODE_Y_AXES = ["8B", "8A"]

SUPERSTRUCTURE_BEAM_SPECS = [
    ("A_PRIME", "1", "A_PRIME", "3", 0.40, 0.80, "V40/80"),
    ("A_PRIME", "1", "D_PRIME", "1", 0.60, 0.80, "V60/80"),
    ("A_PRIME", "3", "D_PRIME", "3", 0.60, 0.80, "V60/80"),
    ("A", "1", "A", "3", 0.60, 0.80, "V60/80"),
    ("A_B_MID", "1", "A_B_MID", "3", 0.60, 0.80, "V60/80"),
    ("B", "1", "B", "3", 0.60, 0.80, "V60/80"),
    ("B_C_MID", "1", "B_C_MID", "3", 0.60, 0.80, "V60/80"),
    ("C", "1", "C", "3", 0.60, 0.80, "V60/80"),
    ("C_CPRIME_500", "1", "C_CPRIME_500", "3", 0.60, 0.80, "V60/80"),
    ("A_PRIME", "1A_PRIME", "A_B_MID", "1A_PRIME", 0.30, 0.80, "V30/80"),
    ("A", "2", "D_PRIME", "2", 0.60, 0.80, "V60/80"),
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

    # The exterior axes are an offset extension and must not move the original
    # building grid, whose axis 1 remains the origin.
    grid_x.update({
        "B_PRIME": grid_x["C"] + 3.224,
        "E1": grid_x["C"] + 3.224 + 7.05,
    })
    grid_y.update({
        "1": 0.0,
        "1A_PRIME": 4.265,
        "2": 8.90,
        "2A_PRIME": 11.885,
        "3": 16.15,
    })
    grid_y["8A"] = grid_y["1"] - 6.42
    grid_y["8B"] = grid_y["8A"] - 4.30


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


def create_slab(slab_number, level, gx1, gx2, gy1, gy2):
    return Slab(
        id=f"L{slab_number}",
        grid_x1=gx1,
        grid_x2=gx2,
        grid_y1=gy1,
        grid_y2=gy2,
        x1=grid_x.get(gx1),
        x2=grid_x.get(gx2),
        y1=grid_y.get(gy1),
        y2=grid_y.get(gy2),
        thickness=SLAB_THICKNESS,
        level=level,
        z_top=levels.get(level),
        reinforcement=SLAB_REINFORCEMENT_BY_LEVEL[level],
    )


def create_geometry():
    refresh_grids()

    nodes = []
    columns = []
    beams = []
    slabs = []
    foundations = []
    foundation_beams = []
    walls = []
    radiers = []
    stairs = []
    stair_walls = []

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

    # Nodes for the exterior extension. Their future stair/radier elements are
    # intentionally not invented until dimensions and connectivity are supplied.
    for gx in EXTERIOR_NODE_X_AXES:
        for gy in EXTERIOR_NODE_Y_AXES:
            for level in VERTICAL_LEVEL_SEQUENCE:
                get_or_create_node(gx, gy, level)

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

    for level in SLAB_LEVELS:
        slab_index = 1
        for row_index, (gy1, gy2) in enumerate(zip(SLAB_GRID_Y_AXIS_NAMES, SLAB_GRID_Y_AXIS_NAMES[1:]), start=1):
            for col_index, (gx1, gx2) in enumerate(zip(SLAB_GRID_X_AXIS_NAMES, SLAB_GRID_X_AXIS_NAMES[1:]), start=1):
                if (gx1, gx2, gy1, gy2) in ELEVATOR_SLAB_VOID_PANELS:
                    continue
                slab_number = f"{SLAB_LEVEL_PREFIX[level]}{slab_index:02d}"
                slabs.append(create_slab(slab_number, level, gx1, gx2, gy1, gy2))
                slab_index += 1

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
    d_prime_x = grid_x.get("D_PRIME")
    a_prime_x = grid_x.get("A_PRIME")
    c_prime_x = grid_x.get("C_PRIME")
    y_1 = grid_y.get("1")
    y_1a_prime = grid_y.get("1A_PRIME")
    y_3 = grid_y.get("3")

    if None not in [d_prime_x, c_prime_x]:
        elevator_wall_end_y = ELEVATOR_TOP_WALL_Y + ELEVATOR_WALL_LENGTH
        add_wall_for_each_storey(walls, "W_DPRIME_ELEVATOR_TOP_TO_CPRIME", "D_PRIME", "ELEVATOR_TOP", "C_PRIME", "ELEVATOR_TOP", d_prime_x, ELEVATOR_TOP_WALL_Y, c_prime_x, ELEVATOR_TOP_WALL_Y, 0.30)
        add_wall_for_each_storey(walls, "W_CPRIME_ELEVATOR_SIDE", "C_PRIME", "ELEVATOR_TOP", "C_PRIME", "OPENING_START_294_5CM", c_prime_x, ELEVATOR_TOP_WALL_Y, c_prime_x, elevator_wall_end_y, 0.25)
        add_wall_for_each_storey(walls, "W_DPRIME_ELEVATOR_SIDE", "D_PRIME", "ELEVATOR_TOP", "D_PRIME", "OPENING_START_294_5CM", d_prime_x, ELEVATOR_TOP_WALL_Y, d_prime_x, elevator_wall_end_y, 0.25)

    if None not in [d_prime_x, y_1, y_3]:
        opening_start_y = y_1 + D_WALL_OPENING_START_FROM_GRID_1
        opening_end_y = opening_start_y + D_WALL_OPENING_LENGTH
        add_wall_for_each_storey(walls, "W_DPRIME_1_TO_ELEVATOR_TOP", "D_PRIME", "1", "D_PRIME", "ELEVATOR_TOP", d_prime_x, y_1, d_prime_x, ELEVATOR_TOP_WALL_Y, 0.25)
        add_wall_for_each_storey(walls, "W_DPRIME_OPENING_TO_3", "D_PRIME", "OPENING_END", "D_PRIME", "3", d_prime_x, opening_end_y, d_prime_x, y_3, 0.25)

    exterior_c_x = grid_x.get("C")
    exterior_bp_x = grid_x.get("B_PRIME")
    exterior_e1_x = grid_x.get("E1")
    exterior_8b_y = grid_y.get("8B")
    exterior_8a_y = grid_y.get("8A")
    if None not in [exterior_c_x, exterior_bp_x, exterior_e1_x, exterior_8b_y, exterior_8a_y]:
        exterior_wall_end_x = exterior_c_x - 1.22
        foundation_wall_specs = [
            ("FW_E1_8B_TO_C_PLUS_122", exterior_e1_x, exterior_8b_y, exterior_wall_end_x, exterior_8b_y),
            ("FW_E1_8B_TO_8A", exterior_e1_x, exterior_8b_y, exterior_e1_x, exterior_8a_y),
            ("FW_E1_8A_TO_C_PLUS_122", exterior_e1_x, exterior_8a_y, exterior_wall_end_x, exterior_8a_y),
            ("FW_BPRIME_8B_TO_8A", exterior_bp_x, exterior_8b_y, exterior_bp_x, exterior_8a_y),
        ]
        for wall_id, x1, y1, x2, y2 in foundation_wall_specs:
            walls.append(StructuralWall(
                id=wall_id,
                grid_x1="EXTERIOR",
                grid_y1="EXTERIOR",
                grid_x2="EXTERIOR",
                grid_y2="EXTERIOR",
                x1=x1,
                y1=y1,
                x2=x2,
                y2=y2,
                thickness=EXTERIOR_FOUNDATION_WALL_THICKNESS,
                z_bottom=EXTERIOR_RADIER_TOP_Z,
                z_top=None,
                status="ACTIVE_PENDING_WALL_HEIGHT",
            ))

        foundation_beams.append(FoundationBeam(
            id="FB_EXT_C_8B_TO_8A",
            node_i=get_or_create_node("C", "8B", "FOUNDATION"),
            node_j=get_or_create_node("C", "8A", "FOUNDATION"),
            width=EXTERIOR_FOUNDATION_BEAM_WIDTH,
            height=EXTERIOR_FOUNDATION_BEAM_HEIGHT,
            z=EXTERIOR_RADIER_TOP_Z,
        ))

        radiers.append(Radier(
            id="R_EXT_001",
            boundary=[
                [exterior_wall_end_x, exterior_8b_y],
                [exterior_e1_x, exterior_8b_y],
                [exterior_e1_x, exterior_8a_y],
                [exterior_wall_end_x, exterior_8a_y],
            ],
            z_top=EXTERIOR_RADIER_TOP_Z,
        ))

        stair_x1, stair_x2 = exterior_c_x, exterior_e1_x
        stair_y1 = (exterior_8a_y + exterior_8b_y) / 2
        stair_y2 = stair_y1
        total_plan_run = stair_x2 - stair_x1
        final_run = total_plan_run - STAIR_FIRST_RUN - STAIR_LANDING_RUN
        first_stair_base_z = EXTERIOR_RADIER_TOP_Z

        def point_at(distance):
            ratio = distance / total_plan_run
            return stair_x1 + (stair_x2 - stair_x1) * ratio, stair_y1 + (stair_y2 - stair_y1) * ratio

        p0 = point_at(0.0)
        p1 = point_at(STAIR_FIRST_RUN)
        p2 = point_at(STAIR_FIRST_RUN + STAIR_LANDING_RUN)
        p3 = point_at(total_plan_run)
        base_segments = [
            ("EXTENSION", stair_x1 - STAIR_ENTRY_EXTENSION, stair_x1, 0.0, 0.0, 0.0),
            ("TRAMO_1", p0[0], p1[0], 0.0, STAIR_FIRST_RUN * STAIR_FIRST_SLOPE, STAIR_FIRST_SLOPE),
            ("DESCANSO", p1[0], p2[0], STAIR_FIRST_RUN * STAIR_FIRST_SLOPE, STAIR_FIRST_RUN * STAIR_FIRST_SLOPE, 0.0),
            ("TRAMO_2", p2[0], p3[0], STAIR_FIRST_RUN * STAIR_FIRST_SLOPE, STAIR_FIRST_RUN * STAIR_FIRST_SLOPE + final_run * STAIR_SECOND_SLOPE, STAIR_SECOND_SLOPE),
        ]
        for storey_index, storey_base_z in enumerate([first_stair_base_z + index * FLOOR_HEIGHT for index in range(4)], start=1):
            storey_segments = []
            for segment_id, x_start, x_end, rise_start, rise_end, slope in base_segments:
                segment = {
                    "id": f"L{storey_index}_{segment_id}",
                    "x1": x_start,
                    "y1": stair_y1,
                    "x2": x_end,
                    "y2": stair_y1,
                    "z1": storey_base_z + rise_start,
                    "z2": storey_base_z + rise_end,
                    "slope": slope,
                }
                if segment_id == "DESCANSO":
                    segment["obra_gruesa"] = storey_base_z + rise_start
                storey_segments.append(segment)
            stairs.append(Stair(
                id=f"ESC_EXT_{storey_index:03d}",
                x1=stair_x1,
                y1=stair_y1,
                x2=stair_x2,
                y2=stair_y2,
                width=STAIR_WIDTH,
                thickness=STAIR_THICKNESS,
                segments=storey_segments,
            ))
            for side, y_side in [("8B", exterior_8b_y), ("8A", exterior_8a_y)]:
                for segment in storey_segments:
                    wall_z1 = storey_base_z + (STAIR_LANDING_WALL_TOP_Z - EXTERIOR_RADIER_TOP_Z) if segment["id"].endswith("DESCANSO") else segment["z1"]
                    wall_z2 = wall_z1 if segment["id"].endswith("DESCANSO") else segment["z2"]
                    stair_walls.append(StairWall(
                        id=f"MW_{storey_index}_{side}_{segment['id']}",
                        side=side,
                        x1=segment["x1"],
                        y1=y_side,
                        z1=wall_z1,
                        x2=segment["x2"],
                        y2=y_side,
                        z2=wall_z2,
                        thickness=EXTERIOR_FOUNDATION_WALL_THICKNESS,
                        top_level="-5.15 m" if segment["id"].endswith("DESCANSO") else "VAR",
                    ))

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
            "slab_note": "Slabs include planta armadura inferior (F) and planta armadura cielo from cielo 1 subterraneo to cielo piso 4.",
            "exterior_extension_note": "Exterior radier and foundation walls are modeled; staircase dimensions remain pending.",
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
            "slab_thickness": SLAB_THICKNESS,
            "stair_width": STAIR_WIDTH,
            "stair_thickness": STAIR_THICKNESS,
        },
        "nodes": to_dict_list(nodes),
        "columns": to_dict_list(columns),
        "beams": to_dict_list(beams),
        "slabs": to_dict_list(slabs),
        "walls": to_dict_list(walls),
        "foundations": to_dict_list(foundations),
        "foundation_beams": to_dict_list(foundation_beams),
        "radiers": to_dict_list(radiers),
        "stairs": to_dict_list(stairs),
        "stair_walls": to_dict_list(stair_walls),
    }
