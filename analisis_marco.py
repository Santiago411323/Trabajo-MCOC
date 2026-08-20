import numpy as np

# ============================================================
# DATOS GENERALES
# ============================================================

E = 200e6  # kN/m2, acero ASTM A36 aprox. 200 GPa

# Seccion horizontal cuadrada 20 cm x 20 cm
b = 0.20  # m
A_beam = b * b
I_beam = b**4 / 12

# Seccion vertical doble T
# Cambiar estos valores segun el perfil real
A_col = 0.0060      # m2, ejemplo
I_col = 8.0e-5      # m4, ejemplo

# Cargas
q_col = 17.0        # kN/m, carga horizontal sobre columna superior
P_beam = 20.0       # kN, carga puntual vertical

# ============================================================
# NODOS
# Cada nodo tiene coordenadas x, y
# ============================================================

nodes = {
    0: (0.0, 0.0),   # base empotrada
    1: (0.0, 2.0),   # union columna-viga
    2: (0.0, 5.0),   # cabeza columna
    3: (5.0, 2.0),   # punto donde actua P = 20 kN
    4: (8.0, 2.0),   # apoyo derecho
}

# Elementos: nodo_i, nodo_j, A, I
elements = {
    0: (0, 1, A_col, I_col),    # columna inferior
    1: (1, 2, A_col, I_col),    # columna superior
    2: (1, 3, A_beam, I_beam),  # viga tramo izquierdo
    3: (3, 4, A_beam, I_beam),  # viga tramo derecho
}

ndof_per_node = 3
total_dof = len(nodes) * ndof_per_node

# ============================================================
# FUNCIONES DE ELEMENTO DE PORTICO 2D
# ============================================================

def dof_map(n1, n2):
    return [
        3*n1, 3*n1+1, 3*n1+2,
        3*n2, 3*n2+1, 3*n2+2
    ]


def element_geometry(n1, n2):
    x1, y1 = nodes[n1]
    x2, y2 = nodes[n2]
    dx = x2 - x1
    dy = y2 - y1
    L = np.sqrt(dx**2 + dy**2)
    c = dx / L
    s = dy / L
    return L, c, s


def local_stiffness(E, A, I, L):
    EA_L = E*A/L
    EI = E*I

    k = np.array([
        [ EA_L,        0,          0, -EA_L,        0,          0],
        [    0,  12*EI/L**3,  6*EI/L**2,     0, -12*EI/L**3,  6*EI/L**2],
        [    0,   6*EI/L**2,    4*EI/L,      0,  -6*EI/L**2,    2*EI/L],
        [-EA_L,        0,          0,  EA_L,        0,          0],
        [    0, -12*EI/L**3, -6*EI/L**2,     0,  12*EI/L**3, -6*EI/L**2],
        [    0,   6*EI/L**2,    2*EI/L,      0,  -6*EI/L**2,    4*EI/L]
    ])

    return k


def transformation(c, s):
    T = np.array([
        [ c,  s, 0,  0,  0, 0],
        [-s,  c, 0,  0,  0, 0],
        [ 0,  0, 1,  0,  0, 0],
        [ 0,  0, 0,  c,  s, 0],
        [ 0,  0, 0, -s,  c, 0],
        [ 0,  0, 0,  0,  0, 1]
    ])
    return T


# ============================================================
# ENSAMBLAJE GLOBAL
# ============================================================

K = np.zeros((total_dof, total_dof))
F = np.zeros(total_dof)

for eid, (n1, n2, A, I) in elements.items():
    L, c, s = element_geometry(n1, n2)
    k_local = local_stiffness(E, A, I, L)
    T = transformation(c, s)
    k_global = T.T @ k_local @ T

    dofs = dof_map(n1, n2)

    for i in range(6):
        for j in range(6):
            K[dofs[i], dofs[j]] += k_global[i, j]

# ============================================================
# CARGAS NODALES
# ============================================================

# Carga puntual de 20 kN hacia abajo en nodo 3
F[3*3 + 1] += -P_beam

# ============================================================
# CARGA DISTRIBUIDA EN COLUMNA SUPERIOR
# Elemento 1: de nodo 1 a nodo 2
# Carga horizontal global +X de 17 kN/m
# Como la columna es vertical, esa carga corresponde a carga local transversal.
# ============================================================

eid = 1
n1, n2, A, I = elements[eid]
L, c, s = element_geometry(n1, n2)
T = transformation(c, s)

# Para elemento vertical de abajo hacia arriba:
# eje local x va hacia arriba.
# carga global +X equivale a carga local y negativa.
w_local_y = -q_col

f_fixed_local = np.array([
    0,
    w_local_y * L / 2,
    w_local_y * L**2 / 12,
    0,
    w_local_y * L / 2,
    -w_local_y * L**2 / 12
])

# Vector equivalente global
f_fixed_global = T.T @ f_fixed_local

dofs = dof_map(n1, n2)
for i in range(6):
    F[dofs[i]] += f_fixed_global[i]

# ============================================================
# CONDICIONES DE APOYO
# ============================================================

restrained_dofs = []

# Nodo 0 empotrado: Ux, Uy, rotacion
restrained_dofs += [0, 1, 2]

# Nodo 4 apoyo tipo pasador: Ux, Uy
restrained_dofs += [3*4, 3*4 + 1]

free_dofs = [i for i in range(total_dof) if i not in restrained_dofs]

# ============================================================
# SOLUCION
# ============================================================

Kff = K[np.ix_(free_dofs, free_dofs)]
Ff = F[free_dofs]

Uf = np.linalg.solve(Kff, Ff)

U = np.zeros(total_dof)
U[free_dofs] = Uf

R = K @ U - F

# ============================================================
# RESULTADOS
# ============================================================

print("\n================ DESPLAZAMIENTOS NODALES ================")
for nid in nodes:
    ux = U[3*nid]
    uy = U[3*nid + 1]
    rz = U[3*nid + 2]
    print(f"Nodo {nid}: Ux = {ux:.6e} m, Uy = {uy:.6e} m, Rz = {rz:.6e} rad")

print("\n================ REACCIONES ================")
for dof in restrained_dofs:
    nodo = dof // 3
    tipo = ["Rx", "Ry", "Mz"][dof % 3]
    print(f"Nodo {nodo} {tipo}: {R[dof]:.3f} kN o kN*m")

print("\n================ FUERZAS INTERNAS POR ELEMENTO ================")

for eid, (n1, n2, A, I) in elements.items():
    L, c, s = element_geometry(n1, n2)
    T = transformation(c, s)
    k_local = local_stiffness(E, A, I, L)
    dofs = dof_map(n1, n2)

    u_global = U[dofs]
    u_local = T @ u_global

    f_local = k_local @ u_local

    # Restar cargas fijas si el elemento tiene carga distribuida
    if eid == 1:
        f_local -= f_fixed_local

    print(f"\nElemento {eid}: nodo {n1} -> nodo {n2}")
    print(f"Longitud = {L:.3f} m")
    print(f"N_i = {f_local[0]:.3f} kN")
    print(f"V_i = {f_local[1]:.3f} kN")
    print(f"M_i = {f_local[2]:.3f} kN*m")
    print(f"N_j = {f_local[3]:.3f} kN")
    print(f"V_j = {f_local[4]:.3f} kN")
    print(f"M_j = {f_local[5]:.3f} kN*m")
