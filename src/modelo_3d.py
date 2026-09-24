"""
Modelo Estructural 3D - Marco Simple 1 Piso
=============================================
OpenSeesPy - Analisis estatico lineal

Geometria:
  - 1 vano en X (6 m)
  - 1 vano en Y (6 m)
  - 1 nivel (3.5 m)

Secciones:
  - Columnas: 40 cm x 40 cm
  - Vigas: 30 cm x 50 cm

Materiales:
  - Concreto: f'c = 25 MPa

Autor: Grupo MCOC
Fecha: Agosto 2026
"""

import openseespy.opensees as ops
import numpy as np
import json
import os

# ============================================================
# CONFIGURACION
# ============================================================
LX = 6.0           # Longitud total X (m)
LY = 6.0           # Longitud total Y (m)
LZ = 3.5           # Altura total Z (m)
NVANOS_X = 1       # 1 vano en X
NVANOS_Y = 1       # 1 vano en Y
NNIVELES = 1       # 1 nivel

VANO_X = LX / NVANOS_X  # 6 m
VANO_Y = LY / NVANOS_Y  # 6 m
PISO = LZ / NNIVELES    # 3.5 m

# Secciones
B_COL = 0.40       # Ancho columna (m)
H_COL = 0.40       # Altura columna (m)
B_VIGA = 0.30      # Ancho viga (m)
H_VIGA = 0.50      # Altura viga (m)

# Materiales
FC = 25.0          # Resistencia concreto (MPa)
EC = 4700.0 * np.sqrt(FC)  # Modulo elastico concreto (MPa)
FY = 420.0         # Fluencia acero (MPa)
NU = 0.2           # Coeficiente de Poisson
G = EC / (2 * (1 + NU))  # Modulo de cortante (MPa)

# Cargas
Q_LOSA = 6.0       # Carga muerta de losa (kN/m2)
Q_VIVA = 2.0       # Carga viva (kN/m2)
GAMMA_D = 1.2      # Factor de carga muerta
GAMMA_L = 1.6      # Factor de carga viva
PESO_ESPECIFICO = 24.0  # kN/m3 concreto

# ============================================================
# FUNCIONES AUXILIARES
# ============================================================

def crear_nodos():
    """Crear nodos: 4 en base + 4 en techo = 8 nodos"""
    nodo_id = 1
    nodo_map = {}
    coordenadas = {}

    nx = NVANOS_X + 1  # 2
    ny = NVANOS_Y + 1  # 2
    nz = NNIVELES + 1  # 2

    for k in range(nz):
        for j in range(ny):
            for i in range(nx):
                x = i * VANO_X
                y = j * VANO_Y
                z = k * PISO
                ops.node(nodo_id, x, y, z)
                nodo_map[(i, j, k)] = nodo_id
                coordenadas[nodo_id] = (x, y, z)
                nodo_id += 1

    return nodo_map, coordenadas


def calcular_propiedades_seccion(b, h):
    """Calcular A, Iy, Iz, J para seccion rectangular"""
    A = b * h
    Iy = b * h**3 / 12.0
    Iz = b**3 * h / 12.0
    J = min(b, h)**3 * max(b, h) / 3.0
    return A, Iy, Iz, J


def crear_elementos(nodo_map):
    """Crear columnas y vigas"""
    elem_id = 1
    elem_map = {}

    nz = NNIVELES + 1
    ny = NVANOS_Y + 1
    nx = NVANOS_X + 1

    A_col, Iy_col, Iz_col, J_col = calcular_propiedades_seccion(B_COL, H_COL)
    A_vig, Iy_vig, Iz_vig, J_vig = calcular_propiedades_seccion(B_VIGA, H_VIGA)

    # --- 4 COLUMNAS (base -> techo) ---
    for k in range(NNIVELES):
        for j in range(ny):
            for i in range(nx):
                n1 = nodo_map[(i, j, k)]
                n2 = nodo_map[(i, j, k + 1)]
                ops.element("elasticBeamColumn", elem_id, n1, n2,
                            A_col, EC, G, J_col, Iy_col, Iz_col, 1)
                elem_map[("col", i, j, k)] = elem_id
                elem_id += 1

    # --- 2 VIGAS EN X (nivel 1) ---
    for k in range(1, nz):
        for j in range(ny):
            for i in range(NVANOS_X):
                n1 = nodo_map[(i, j, k)]
                n2 = nodo_map[(i + 1, j, k)]
                ops.element("elasticBeamColumn", elem_id, n1, n2,
                            A_vig, EC, G, J_vig, Iy_vig, Iz_vig, 2)
                elem_map[("vig_x", i, j, k)] = elem_id
                elem_id += 1

    # --- 2 VIGAS EN Y (nivel 1) ---
    for k in range(1, nz):
        for j in range(NVANOS_Y):
            for i in range(nx):
                n1 = nodo_map[(i, j, k)]
                n2 = nodo_map[(i, j + 1, k)]
                ops.element("elasticBeamColumn", elem_id, n1, n2,
                            A_vig, EC, G, J_vig, Iy_vig, Iz_vig, 2)
                elem_map[("vig_y", i, j, k)] = elem_id
                elem_id += 1

    return elem_map, elem_id


def definir_apoyos(nodo_map):
    """Apoyos fijos en la base (z=0)"""
    ny = NVANOS_Y + 1
    nx = NVANOS_X + 1
    for j in range(ny):
        for i in range(nx):
            nodo = nodo_map[(i, j, 0)]
            ops.fix(nodo, 1, 1, 1, 1, 1, 1)


def definir_transformaciones():
    """
    geomTransf 1 (vecxz=1,0,0): Para columnas (along Z)
    geomTransf 2 (vecxz=0,0,1): Para vigas X e Y
      - local z = +Z (upward), gravedad = -local z -> wz = -w
    """
    ops.geomTransf("Linear", 1, 1, 0, 0)
    ops.geomTransf("Linear", 2, 0, 0, 1)


def calcular_carga_losa():
    """
    Carga de losa sobre vigas.
    Para losa bidireccional cuadrada: w = q * L / 4
    """
    Q_TOTAL = GAMMA_D * Q_LOSA + GAMMA_L * Q_VIVA
    W_LOSA = Q_TOTAL * VANO_X / 4.0
    print(f"\n--- CARGAS ---")
    print(f"Carga losa total (D+L): {Q_TOTAL:.2f} kN/m2")
    print(f"Carga en vigas: {W_LOSA:.2f} kN/m")
    return W_LOSA


def aplicar_cargas(elem_map, W_LOSA):
    """Aplicar carga de losa en vigas"""
    ny = NVANOS_Y + 1
    nx = NVANOS_X + 1

    # VIGAS EN X
    for k in range(1, NNIVELES + 1):
        for j in range(ny):
            for i in range(NVANOS_X):
                tag = elem_map[("vig_x", i, j, k)]
                ops.eleLoad("-ele", tag, "-type", "-beamUniform", 0, -W_LOSA)

    # VIGAS EN Y
    for k in range(1, NNIVELES + 1):
        for j in range(NVANOS_Y):
            for i in range(nx):
                tag = elem_map[("vig_y", i, j, k)]
                ops.eleLoad("-ele", tag, "-type", "-beamUniform", 0, -W_LOSA)


def configurar_patron():
    """Definir timeSeries y pattern (debe ir ANTES de eleLoad)"""
    ops.timeSeries("Linear", 1)
    ops.pattern("Plain", 1, 1)


def ejecutar_analisis():
    """Ejecutar analisis estatico lineal"""
    ops.system("BandGeneral")
    ops.numberer("RCM")
    ops.constraints("Plain")
    ops.integrator("LoadControl", 1.0)
    ops.algorithm("Linear")
    ops.analysis("Static")
    ops.analyze(1)
    ops.loadConst("-time", 0.0)
    ops.reactions()


def extraer_resultados(nodo_map, coordenadas, elem_map, num_elem):
    """Extraer todos los resultados"""
    resultados = {}
    ny = NVANOS_Y + 1
    nx = NVANOS_X + 1

    # DESPLAZAMIENTOS
    desplazamientos = {}
    for k in range(NNIVELES + 1):
        master = nodo_map[(0, 0, k)]
        disp = ops.nodeDisp(master)
        desplazamientos[f"nivel_{k}"] = {
            "nodo_master": master,
            "ux": float(disp[0]),
            "uy": float(disp[1]),
            "uz": float(disp[2]),
            "rx": float(disp[3]),
            "ry": float(disp[4]),
            "rz": float(disp[5]),
        }
    resultados["desplazamientos"] = desplazamientos

    # REACCIONES
    reacciones = {}
    for j in range(ny):
        for i in range(nx):
            nodo = nodo_map[(i, j, 0)]
            rx = ops.nodeReaction(nodo, 1)
            ry = ops.nodeReaction(nodo, 2)
            rz = ops.nodeReaction(nodo, 3)
            reacciones[f"nodo_{nodo}_({i},{j},0)"] = {
                "fx": float(rx),
                "fy": float(ry),
                "fz": float(rz),
            }
    resultados["reacciones"] = reacciones

    # FUERZAS DE ELEMENTOS
    fuerzas_elem = {}
    for tag in range(1, num_elem):
        try:
            f = ops.eleForce(tag)
            if len(f) >= 6:
                fuerzas_elem[f"elemento_{tag}"] = {
                    "N1": float(f[0]),
                    "V2_1": float(f[1]),
                    "V3_1": float(f[2]),
                    "T1": float(f[3]),
                    "M2_1": float(f[4]),
                    "M3_1": float(f[5]),
                    "N2": float(f[6]) if len(f) > 6 else 0,
                    "V2_2": float(f[7]) if len(f) > 7 else 0,
                    "V3_2": float(f[8]) if len(f) > 8 else 0,
                    "T2": float(f[9]) if len(f) > 9 else 0,
                    "M2_2": float(f[10]) if len(f) > 10 else 0,
                    "M3_2": float(f[11]) if len(f) > 11 else 0,
                }
        except Exception:
            pass
    resultados["fuerzas_elementos"] = fuerzas_elem

    return resultados


def imprimir_resultados(resultados):
    """Imprimir resultados resumidos"""
    print("\n" + "=" * 60)
    print("RESULTADOS DEL ANALISIS")
    print("=" * 60)

    print("\n--- DESPLAZAMIENTOS (m) ---")
    for nivel, datos in resultados["desplazamientos"].items():
        print(f"  {nivel} (nodo {datos['nodo_master']}): "
              f"ux={datos['ux']:.6e}, uy={datos['uy']:.6e}, uz={datos['uz']:.6e}")

    print("\n--- REACCIONES EN APOYOS (kN) ---")
    total_fz = 0.0
    for nodo, datos in resultados["reacciones"].items():
        print(f"  {nodo}: Fx={datos['fx']:.2f}, Fy={datos['fy']:.2f}, Fz={datos['fz']:.2f}")
        total_fz += datos["fz"]
    print(f"  ** Suma total Fz reacciones: {total_fz:.2f} kN")

    print("\n--- FUERZAS DE ELEMENTOS ---")
    for tag, datos in resultados["fuerzas_elementos"].items():
        print(f"  {tag}: N={datos['N1']:.2f}, V2={datos['V2_1']:.2f}, "
              f"V3={datos['V3_1']:.2f}, M2={datos['M2_1']:.2f}, M3={datos['M3_1']:.2f}")


def guardar_resultados(resultados, coordenadas, elem_map):
    """Guardar resultados en archivos JSON"""
    out_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), "resultados")
    os.makedirs(out_dir, exist_ok=True)

    with open(os.path.join(out_dir, "resultados_modelo.json"), "w", encoding="utf-8") as f:
        json.dump(resultados, f, indent=2, ensure_ascii=False)

    with open(os.path.join(out_dir, "coordenadas_nodos.json"), "w", encoding="utf-8") as f:
        json.dump({str(k): list(v) for k, v in coordenadas.items()}, f, indent=2)

    elem_serializable = {str(k): v for k, v in elem_map.items()}
    with open(os.path.join(out_dir, "elementos.json"), "w", encoding="utf-8") as f:
        json.dump(elem_serializable, f, indent=2)

    print(f"\nResultados guardados en: {out_dir}")


# ============================================================
# PROGRAMA PRINCIPAL
# ============================================================
def main():
    print("=" * 60)
    print("MODELO ESTRUCTURAL 3D - 4 COLUMNAS, 1 PISO")
    print("OpenSeesPy - Analisis Estatico Lineal")
    print("=" * 60)

    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)

    print(f"\nGeometria: {LX}m x {LY}m x {LZ}m")
    print(f"Columna: {B_COL*100:.0f}x{H_COL*100:.0f} cm")
    print(f"Viga: {B_VIGA*100:.0f}x{H_VIGA*100:.0f} cm")
    print(f"EC = {EC:.0f} MPa, G = {G:.0f} MPa")

    nodo_map, coordenadas = crear_nodos()
    print(f"\nNodos: {len(coordenadas)}")

    definir_apoyos(nodo_map)
    definir_transformaciones()

    elem_map, num_elem = crear_elementos(nodo_map)
    print(f"Elementos: {num_elem - 1} (4 columnas + 4 vigas)")

    W_LOSA = calcular_carga_losa()
    configurar_patron()
    aplicar_cargas(elem_map, W_LOSA)

    print("\nEjecutando analisis...")
    ejecutar_analisis()
    print("Analisis completado")

    resultados = extraer_resultados(nodo_map, coordenadas, elem_map, num_elem)
    imprimir_resultados(resultados)
    guardar_resultados(resultados, coordenadas, elem_map)

    ops.wipe()
    print("\n" + "=" * 60)
    print("FIN DEL ANALISIS")
    print("=" * 60)


if __name__ == "__main__":
    main()
