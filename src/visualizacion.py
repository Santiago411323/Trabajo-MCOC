"""
Script de Visualizacion 3D - Marco Simple 1 Piso
==================================================
Grafica: geometria original, deformada y reacciones
"""

import json
import os
import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D
from mpl_toolkits.mplot3d.art3d import Line3DCollection

# Cargar datos
base = os.path.dirname(os.path.dirname(__file__))
coord_file = os.path.join(base, "resultados", "coordenadas_nodos.json")
elem_file = os.path.join(base, "resultados", "elementos.json")
res_file = os.path.join(base, "resultados", "resultados_modelo.json")
out_dir = os.path.join(base, "resultados")

with open(coord_file, "r") as f:
    coordenadas = {int(k): tuple(v) for k, v in json.load(f).items()}
with open(elem_file, "r") as f:
    elem_map_raw = json.load(f)
with open(res_file, "r") as f:
    resultados = json.load(f)

elem_map = {}
for k, v in elem_map_raw.items():
    key = tuple(k.strip("()").split(", ")) if "(" in k else k
    if "(" in k:
        parts = k.strip("()").split(", ")
        key = (parts[0].strip("'"), int(parts[1]), int(parts[2]), int(parts[3]))
    else:
        key = k
    elem_map[key] = v


def graficar_geometria():
    """Grafica la geometria original del marco"""
    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection='3d')

    # Coordenadas de nodos en techo (k=1)
    nodos_base = {k: v for k, v in coordenadas.items() if v[2] == 0}
    nodos_techo = {k: v for k, v in coordenadas.items() if v[2] == 3.5}

    # Dibujar columnas (base -> techo)
    for n_base, n_techo in zip(nodos_base.values(), nodos_techo.values()):
        xs = [n_base[0], n_techo[0]]
        ys = [n_base[1], n_techo[1]]
        zs = [n_base[2], n_techo[2]]
        ax.plot(xs, ys, zs, 'b-', linewidth=2)

    # Dibujar vigas en techo
    nodos_orden = list(nodos_techo.values())
    # Vigas en X (0->1, 2->3)
    for i in range(0, 4, 2):
        xs = [nodos_orden[i][0], nodos_orden[i+1][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+1][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+1][2]]
        ax.plot(xs, ys, zs, 'g-', linewidth=3)
    # Vigas en Y (0->2, 1->3)
    for i in range(2):
        xs = [nodos_orden[i][0], nodos_orden[i+2][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+2][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+2][2]]
        ax.plot(xs, ys, zs, 'g-', linewidth=3)

    # Nodos
    for nodo, coord in coordenadas.items():
        color = 'red' if coord[2] == 0 else 'orange'
        ax.scatter(*coord, c=color, s=80, zorder=5)
        ax.text(coord[0]+0.15, coord[1]+0.15, coord[2]+0.15, f'N{nodo}', fontsize=9)

    # Losa (superficie semitransparente)
    if len(nodos_techo) >= 4:
        pts = np.array(list(nodos_techo.values()))
        from mpl_toolkits.mplot3d.art3d import Poly3DCollection
        verts = [[pts[0], pts[1], pts[3], pts[2]]]
        poly = Poly3DCollection(verts, alpha=0.15, facecolor='cyan', edgecolor='cyan', linewidth=1)
        ax.add_collection3d(poly)

    ax.set_xlabel('X (m)')
    ax.set_ylabel('Y (m)')
    ax.set_zlabel('Z (m)')
    ax.set_title('Geometria Original - 4 Columnas, 1 Piso', fontsize=14)
    ax.view_init(elev=25, azim=-55)

    plt.tight_layout()
    path = os.path.join(out_dir, "geometria.png")
    plt.savefig(path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"Guardado: geometria.png")


def graficar_deformada():
    """Grafica la estructura deformada"""
    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection='3d')

    FACTOR = 15
    despl = resultados["desplazamientos"]

    # Coordenadas deformadas de nodos techo
    def deformado_techo():
        resultado = {}
        for nodo, coord in coordenadas.items():
            if coord[2] == 3.5:
                nivel_key = "nivel_1"
                dx = despl[nivel_key]["ux"]
                dy = despl[nivel_key]["uy"]
                dz = despl[nivel_key]["uz"]
                resultado[nodo] = (coord[0] + FACTOR*dx, coord[1] + FACTOR*dy, coord[2] + FACTOR*dz)
        return resultado

    def deformado_base():
        resultado = {}
        for nodo, coord in coordenadas.items():
            if coord[2] == 0:
                resultado[nodo] = coord
        return resultado

    nodos_base = deformado_base()
    nodos_techo = deformado_techo()

    # Columnas deformadas
    for n_base, n_techo in zip(nodos_base.values(), nodos_techo.values()):
        xs = [n_base[0], n_techo[0]]
        ys = [n_base[1], n_techo[1]]
        zs = [n_base[2], n_techo[2]]
        ax.plot(xs, ys, zs, 'b-', linewidth=2, alpha=0.7)

    # Vigas deformadas
    nodos_orden = list(nodos_techo.values())
    for i in range(0, 4, 2):
        xs = [nodos_orden[i][0], nodos_orden[i+1][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+1][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+1][2]]
        ax.plot(xs, ys, zs, 'r-', linewidth=3)
    for i in range(2):
        xs = [nodos_orden[i][0], nodos_orden[i+2][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+2][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+2][2]]
        ax.plot(xs, ys, zs, 'r-', linewidth=3)

    # Nodos deformados
    for nodo, coord in nodos_techo.items():
        ax.scatter(*coord, c='red', s=80, zorder=5)
        ax.text(coord[0]+0.15, coord[1]+0.15, coord[2]+0.15, f'N{nodo}', fontsize=9)

    # Geometria original (gris punteado)
    nodos_techo_orig = {k: v for k, v in coordenadas.items() if v[2] == 3.5}
    nodos_orden_orig = list(nodos_techo_orig.values())
    for n_base, n_techo in zip(nodos_base.values(), nodos_techo_orig.values()):
        xs = [n_base[0], n_techo[0]]
        ys = [n_base[1], n_techo[1]]
        zs = [n_base[2], n_techo[2]]
        ax.plot(xs, ys, zs, 'gray', linewidth=1, linestyle='--', alpha=0.4)
    for i in range(0, 4, 2):
        xs = [nodos_orden_orig[i][0], nodos_orden_orig[i+1][0]]
        ys = [nodos_orden_orig[i][1], nodos_orden_orig[i+1][1]]
        zs = [nodos_orden_orig[i][2], nodos_orden_orig[i+1][2]]
        ax.plot(xs, ys, zs, 'gray', linewidth=1, linestyle='--', alpha=0.4)
    for i in range(2):
        xs = [nodos_orden_orig[i][0], nodos_orden_orig[i+2][0]]
        ys = [nodos_orden_orig[i][1], nodos_orden_orig[i+2][1]]
        zs = [nodos_orden_orig[i][2], nodos_orden_orig[i+2][2]]
        ax.plot(xs, ys, zs, 'gray', linewidth=1, linestyle='--', alpha=0.4)

    # Losa deformada
    pts = np.array(list(nodos_techo.values()))
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    verts = [[pts[0], pts[1], pts[3], pts[2]]]
    poly = Poly3DCollection(verts, alpha=0.15, facecolor='cyan', edgecolor='cyan', linewidth=1)
    ax.add_collection3d(poly)

    # Límites dinámicos
    all_pts = np.array(list(nodos_base.values()) + list(nodos_techo.values()))
    margin = 1.0
    ax.set_xlim(all_pts[:,0].min()-margin, all_pts[:,0].max()+margin)
    ax.set_ylim(all_pts[:,1].min()-margin, all_pts[:,1].max()+margin)
    ax.set_zlim(all_pts[:,2].min()-margin, all_pts[:,2].max()+margin)

    ax.set_xlabel('X (m)')
    ax.set_ylabel('Y (m)')
    ax.set_zlabel('Z (m)')
    ax.set_title(f'Deformada (x{FACTOR}) - 4 Columnas, 1 Piso', fontsize=14)
    ax.view_init(elev=25, azim=-55)
    ax.legend(['Original', '', 'Deformada'], loc='upper left')

    plt.tight_layout()
    path = os.path.join(out_dir, "deformada.png")
    plt.savefig(path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"Guardado: deformada.png")


def graficar_reacciones():
    """Grafica las reacciones en los apoyos"""
    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection='3d')

    reacciones = resultados["reacciones"]
    nodos_base = {k: v for k, v in coordenadas.items() if v[2] == 0}

    # Normalizar la maxima magnitud a una longitud de flecha fija
    max_fz = max(abs(d["fz"]) for d in reacciones.values()) or 1.0
    L_FUERZA = 1.5      # longitud maxima de flecha en metros
    sx = L_FUERZA / max(abs(d["fx"]) + 1e-9 for d in reacciones.values())
    s_y = L_FUERZA / max(abs(d["fy"]) + 1e-9 for d in reacciones.values())
    sz = L_FUERZA / max_fz

    for nodo, coord in nodos_base.items():
        key = f"nodo_{nodo}"
        datos = None
        for rk, rv in reacciones.items():
            if rk.startswith(key):
                datos = rv
                break

        if datos:
            fx, fy, fz = datos["fx"], datos["fy"], datos["fz"]

            # Flechas Fx/Fy (horizontales, offset hacia arriba para no chocar con columnas)
            if abs(fx) > 0.01:
                ax.quiver(coord[0], coord[1], 1.0, fx * sx, 0, 0,
                         color='red', arrow_length_ratio=0.12, linewidth=2.5,
                         normalize=False)
                ax.text(coord[0] + fx * sx * 0.5, coord[1], 1.2,
                       f'Fx={fx:.1f}', fontsize=9, color='red', ha='center')

            if abs(fy) > 0.01:
                ax.quiver(coord[0], coord[1], 0.6, 0, fy * s_y, 0,
                         color='blue', arrow_length_ratio=0.12, linewidth=2.5,
                         normalize=False)
                ax.text(coord[0], coord[1] + fy * s_y * 0.5, 0.8,
                       f'Fy={fy:.1f}', fontsize=9, color='blue', ha='center')

            # Flecha Fz (vertical hacia arriba desde el apoyo)
            ax.quiver(coord[0], coord[1], 0, 0, 0, fz * sz,
                     color='green', arrow_length_ratio=0.12, linewidth=3,
                     normalize=False)
            ax.text(coord[0], coord[1], fz * sz + 0.25,
                   f'Fz={fz:.1f} kN', fontsize=11, color='green',
                   ha='center', fontweight='bold')

    # Dibujar estructura base (nodos de apoyo)
    nodos_orden = list(nodos_base.values())
    for i in range(0, 4, 2):
        xs = [nodos_orden[i][0], nodos_orden[i+1][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+1][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+1][2]]
        ax.plot(xs, ys, zs, 'k-', linewidth=1, alpha=0.3)
    for i in range(2):
        xs = [nodos_orden[i][0], nodos_orden[i+2][0]]
        ys = [nodos_orden[i][1], nodos_orden[i+2][1]]
        zs = [nodos_orden[i][2], nodos_orden[i+2][2]]
        ax.plot(xs, ys, zs, 'k-', linewidth=1, alpha=0.3)

    # Nodos de apoyo (soportes triangulares)
    for coord in nodos_base.values():
        ax.scatter(coord[0], coord[1], coord[2], c='k', s=60, zorder=5, marker='^')

    ax.set_xlim(-1.5, 7.5)
    ax.set_ylim(-1.5, 7.5)
    ax.set_zlim(0, 4.5)

    ax.set_xlabel('X (m)')
    ax.set_ylabel('Y (m)')
    ax.set_zlabel('Z (m)')
    ax.set_title('Reacciones en Apoyos - 4 Columnas, 1 Piso', fontsize=14)
    ax.view_init(elev=25, azim=-45)

    from matplotlib.lines import Line2D
    custom_lines = [
        Line2D([0], [0], color='green', lw=3, label='Reacción Fz'),
        Line2D([0], [0], color='red', lw=2.5, label='Reacción Fx'),
        Line2D([0], [0], color='blue', lw=2.5, label='Reacción Fy'),
    ]
    ax.legend(handles=custom_lines, loc='upper left')

    ax.autoscale(enable=False)
    plt.tight_layout()
    path = os.path.join(out_dir, "reacciones.png")
    plt.savefig(path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"Guardado: reacciones.png")


if __name__ == "__main__":
    graficar_geometria()
    graficar_deformada()
    graficar_reacciones()
    print("\nVisualizacion completada.")
