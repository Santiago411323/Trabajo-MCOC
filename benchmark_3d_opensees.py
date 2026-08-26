import matplotlib.pyplot as plt
import openseespy.opensees as ops


# ============================================================
# BENCHMARK 3D OPENSEES - SEMANA 1
# ============================================================
# Unidades:
# - Longitud: m
# - Fuerza: kN
# - Momento: kN*m
# - Esfuerzo: kN/m2


# -------------------------
# 1. Datos del problema
# -------------------------

Lx = 6.0       # vano en direccion X, m
Ly = 5.0       # vano en direccion Y, m
H = 3.2        # altura de piso, m

# Material: acero ASTM A36 aproximado
E = 200e6      # kN/m2
nu = 0.30
G = E / (2 * (1 + nu))

# Secciones propuestas
# Columnas: perfil tubular cuadrado 30x30x1.2 cm
b_col = 0.30
t_col = 0.012
A_col = b_col**2 - (b_col - 2 * t_col)**2
Iy_col = (b_col**4 - (b_col - 2 * t_col)**4) / 12
Iz_col = Iy_col
J_col = 2 * Iy_col

# Vigas: seccion rectangular 25x40 cm
b_viga = 0.25
h_viga = 0.40
A_viga = b_viga * h_viga
Iy_viga = h_viga * b_viga**3 / 12
Iz_viga = b_viga * h_viga**3 / 12
J_viga = Iy_viga + Iz_viga

# Losa como carga superficial equivalente
q_losa = 6.0   # kN/m2, peso propio + sobrecarga supuesta

# Descarga aproximada por ancho tributario hacia las cuatro vigas perimetrales.
# Se reparte la losa entre vigas X e Y para que la carga total sea q_losa*Lx*Ly.
w_vigas_x = q_losa * Ly / 4  # kN/m, vigas paralelas a X
w_vigas_y = q_losa * Lx / 4  # kN/m, vigas paralelas a Y


# -------------------------
# 2. Geometria
# -------------------------

nodes = {
    1: (0.0, 0.0, 0.0),
    2: (Lx, 0.0, 0.0),
    3: (Lx, Ly, 0.0),
    4: (0.0, Ly, 0.0),
    5: (0.0, 0.0, H),
    6: (Lx, 0.0, H),
    7: (Lx, Ly, H),
    8: (0.0, Ly, H),
}

columns = {
    1: (1, 5),
    2: (2, 6),
    3: (3, 7),
    4: (4, 8),
}

beams_x = {
    5: (5, 6),
    7: (8, 7),
}

beams_y = {
    6: (6, 7),
    8: (5, 8),
}

elements = {**columns, **beams_x, **beams_y}


def build_model():
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)

    for tag, coord in nodes.items():
        ops.node(tag, *coord)

    # Bases empotradas
    for tag in [1, 2, 3, 4]:
        ops.fix(tag, 1, 1, 1, 1, 1, 1)

    # Transformaciones geometricas.
    # En vigas horizontales se usa vecxz vertical para que el eje local z quede vertical.
    ops.geomTransf("Linear", 1, 1, 0, 0)  # columnas verticales
    ops.geomTransf("Linear", 2, 0, 0, 1)  # vigas horizontales

    for ele, (ni, nj) in columns.items():
        ops.element("elasticBeamColumn", ele, ni, nj, A_col, E, G, J_col, Iy_col, Iz_col, 1)

    for ele, (ni, nj) in beams_x.items():
        ops.element("elasticBeamColumn", ele, ni, nj, A_viga, E, G, J_viga, Iy_viga, Iz_viga, 2)

    for ele, (ni, nj) in beams_y.items():
        ops.element("elasticBeamColumn", ele, ni, nj, A_viga, E, G, J_viga, Iy_viga, Iz_viga, 2)

    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)

    # Carga de losa descargada a las vigas. En este modelo el eje local z de las
    # vigas horizontales coincide con el eje global Z, por eso Wz es negativo.
    for ele in beams_x:
        ops.eleLoad("-ele", ele, "-type", "-beamUniform", 0.0, -w_vigas_x)

    for ele in beams_y:
        ops.eleLoad("-ele", ele, "-type", "-beamUniform", 0.0, -w_vigas_y)


def run_analysis():
    ops.system("BandGeneral")
    ops.numberer("Plain")
    ops.constraints("Plain")
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    return ops.analyze(1)


def draw_model():
    fig = plt.figure(figsize=(8, 6))
    ax = fig.add_subplot(111, projection="3d")

    for ele, (ni, nj) in elements.items():
        xi, yi, zi = nodes[ni]
        xj, yj, zj = nodes[nj]
        color = "black" if ele in columns else "tab:blue"
        ax.plot([xi, xj], [yi, yj], [zi, zj], color=color, linewidth=3)
        ax.text((xi + xj) / 2, (yi + yj) / 2, (zi + zj) / 2, f"E{ele}", fontsize=8)

    for tag, (x, y, z) in nodes.items():
        ax.scatter(x, y, z, color="black", s=20)
        ax.text(x, y, z + 0.08, f"N{tag}", fontsize=8)

    # Losa representada como plano semitransparente
    xs = [0, Lx, Lx, 0, 0]
    ys = [0, 0, Ly, Ly, 0]
    zs = [H, H, H, H, H]
    ax.plot(xs, ys, zs, color="tab:green", linewidth=1.5)
    ax.text(Lx / 2, Ly / 2, H + 0.15, f"Losa q = {q_losa:.1f} kN/m2", color="tab:green")

    ax.set_title("Benchmark 3D OpenSees - geometria y losa")
    ax.set_xlabel("X [m]")
    ax.set_ylabel("Y [m]")
    ax.set_zlabel("Z [m]")
    ax.set_box_aspect((Lx, Ly, H))
    plt.tight_layout()
    plt.savefig("benchmark_3d_modelo.png", dpi=200)
    plt.show()


def print_results(resultado):
    ops.reactions()

    print("\n================ BENCHMARK 3D OPENSEES ================")
    print(f"Resultado del analisis: {resultado}")
    print("0 significa que el analisis corrio correctamente")

    print("\n--- Geometria ---")
    print(f"Vano X = {Lx:.2f} m")
    print(f"Vano Y = {Ly:.2f} m")
    print(f"Altura = {H:.2f} m")

    print("\n--- Material ---")
    print(f"E = {E:.3e} kN/m2")
    print(f"G = {G:.3e} kN/m2")

    print("\n--- Secciones ---")
    print(f"Columnas: A = {A_col:.6f} m2, Iy = {Iy_col:.6e} m4, Iz = {Iz_col:.6e} m4, J = {J_col:.6e} m4")
    print(f"Vigas:    A = {A_viga:.6f} m2, Iy = {Iy_viga:.6e} m4, Iz = {Iz_viga:.6e} m4, J = {J_viga:.6e} m4")

    print("\n--- Cargas de losa descargadas a vigas ---")
    print(f"q_losa = {q_losa:.2f} kN/m2")
    print(f"Vigas paralelas a X: w = {w_vigas_x:.2f} kN/m")
    print(f"Vigas paralelas a Y: w = {w_vigas_y:.2f} kN/m")
    print(f"Carga lineal total aplicada = {-2 * w_vigas_x * Lx - 2 * w_vigas_y * Ly:.3f} kN")

    print("\n--- Desplazamientos nodales superiores ---")
    for tag in [5, 6, 7, 8]:
        ux = ops.nodeDisp(tag, 1)
        uy = ops.nodeDisp(tag, 2)
        uz = ops.nodeDisp(tag, 3)
        rx = ops.nodeDisp(tag, 4)
        ry = ops.nodeDisp(tag, 5)
        rz = ops.nodeDisp(tag, 6)
        print(f"Nodo {tag}: Ux={ux:.6e} m, Uy={uy:.6e} m, Uz={uz:.6e} m, Rx={rx:.6e}, Ry={ry:.6e}, Rz={rz:.6e}")

    print("\n--- Reacciones en bases ---")
    total_rx = total_ry = total_rz = 0.0
    for tag in [1, 2, 3, 4]:
        rx = ops.nodeReaction(tag, 1)
        ry = ops.nodeReaction(tag, 2)
        rz = ops.nodeReaction(tag, 3)
        mx = ops.nodeReaction(tag, 4)
        my = ops.nodeReaction(tag, 5)
        mz = ops.nodeReaction(tag, 6)
        total_rx += rx
        total_ry += ry
        total_rz += rz
        print(f"Nodo {tag}: Rx={rx:.3f} kN, Ry={ry:.3f} kN, Rz={rz:.3f} kN, Mx={mx:.3f}, My={my:.3f}, Mz={mz:.3f}")

    print("\n--- Chequeo de equilibrio global ---")
    print(f"Suma reacciones X = {total_rx:.3f} kN")
    print(f"Suma reacciones Y = {total_ry:.3f} kN")
    print(f"Suma reacciones Z = {total_rz:.3f} kN")
    print(f"Carga vertical total de losa = {-q_losa * Lx * Ly:.3f} kN")

    print("\n--- Fuerzas internas por elemento ---")
    print("Formato OpenSees 3D: [P_i, Vy_i, Vz_i, T_i, My_i, Mz_i, P_j, Vy_j, Vz_j, T_j, My_j, Mz_j]")
    for ele in sorted(elements):
        valores = ops.eleForce(ele)
        valores_txt = ", ".join(f"{v:.3f}" for v in valores)
        print(f"Elemento {ele}: [{valores_txt}]")


if __name__ == "__main__":
    build_model()
    resultado = run_analysis()
    draw_model()
    print_results(resultado)
    ops.wipe()
