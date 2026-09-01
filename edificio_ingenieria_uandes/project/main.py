import json
from pathlib import Path

from build_opensees import build_opensees_nodes_only
from checks import run_all_checks
from geometry_data import FOUNDATION_HEIGHTS, RADIER_THICKNESS, create_geometry
from viewer_2d import create_viewer_2d
from viewer_3d import create_viewer_3d


BASE_DIR = Path(__file__).resolve().parent
OUTPUT_DIR = BASE_DIR / "outputs"
GEOMETRY_PATH = BASE_DIR / "structural_geometry.json"


def save_geometry(geometry):
    with open(GEOMETRY_PATH, "w", encoding="utf-8") as file:
        json.dump(geometry, file, indent=2)


def load_geometry():
    with open(GEOMETRY_PATH, "r", encoding="utf-8") as file:
        return json.load(file)


def print_summary(geometry, errors, warnings, viewer_2d_path, viewer_3d_path):
    print("========================================")
    print("UANDES STRUCTURAL MODEL")
    print("========================================")
    print("Geometry loaded.")
    print()
    print("Radier thickness:")
    print(f"{RADIER_THICKNESS:.3f} m")
    print()
    print("Foundation heights available:")
    for value in FOUNDATION_HEIGHTS.values():
        print(f"{value:.3f} m")
    print()
    print(f"Nodes: {len(geometry['nodes'])}")
    print(f"Columns: {len(geometry['columns'])}")
    print(f"Beams: {len(geometry['beams'])}")
    print(f"Walls: {len(geometry['walls'])}")
    print(f"Foundation beams: {len(geometry['foundation_beams'])}")
    print(f"Foundations: {len(geometry['foundations'])}")
    print(f"Radiers: {len(geometry['radiers'])}")
    print()
    print(f"Geometry errors: {len(errors)}")
    for error in errors[:20]:
        print(f"- {error}")
    if len(errors) > 20:
        print(f"- ... {len(errors) - 20} more")
    print()
    print(f"Geometry warnings: {len(warnings)}")
    for warning in warnings[:20]:
        print(f"- {warning}")
    if len(warnings) > 20:
        print(f"- ... {len(warnings) - 20} more")
    print()
    print("2D viewer:")
    print(viewer_2d_path)
    print()
    print("3D viewer:")
    print(viewer_3d_path)
    print("========================================")


def main():
    geometry = create_geometry()
    save_geometry(geometry)
    geometry = load_geometry()

    errors, warnings = run_all_checks(geometry)
    opensees_status = build_opensees_nodes_only(geometry)
    geometry["opensees_status"] = opensees_status
    save_geometry(geometry)

    viewer_2d_path = create_viewer_2d(geometry, OUTPUT_DIR / "structural_2d.html")
    viewer_3d_path = create_viewer_3d(geometry, OUTPUT_DIR / "structural_3d.html")
    print_summary(geometry, errors, warnings, viewer_2d_path, viewer_3d_path)


if __name__ == "__main__":
    main()
