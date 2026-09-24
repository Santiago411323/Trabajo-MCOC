"""
Script de Verificacion - Marco Simple 1 Piso
==============================================
Compara resultados del modelo OpenSeesPy con estimaciones manuales.
"""

import json
import os
import numpy as np

# ============================================================
# CONSTANTES DEL MODELO (mismas que modelo_3d.py)
# ============================================================
LX = 6.0
LY = 6.0
LZ = 3.5
NVANOS_X = 1
NVANOS_Y = 1
NNIVELES = 1
VANO_X = LX / NVANOS_X
VANO_Y = LY / NVANOS_Y
PISO = LZ / NNIVELES

B_COL = 0.40
H_COL = 0.40
B_VIGA = 0.30
H_VIGA = 0.50

FC = 25.0
EC = 4700.0 * np.sqrt(FC)
NU = 0.2
G = EC / (2 * (1 + NU))

Q_LOSA = 6.0
Q_VIVA = 2.0
GAMMA_D = 1.2
GAMMA_L = 1.6
PESO_ESPECIFICO = 24.0


def cargar_resultados():
    base = os.path.dirname(os.path.dirname(__file__))
    res_file = os.path.join(base, "resultados", "resultados_modelo.json")
    with open(res_file, "r", encoding="utf-8") as f:
        return json.load(f)


def verificar_carga_total():
    """Calcular carga total aplicada en el modelo"""
    Q_TOTAL = GAMMA_D * Q_LOSA + GAMMA_L * Q_VIVA
    W_LOSA = Q_TOTAL * VANO_X / 4.0

    num_vigas_x = NVANOS_X * (NVANOS_Y + 1) * NNIVELES  # 1*2*1 = 2
    num_vigas_y = NVANOS_Y * (NVANOS_X + 1) * NNIVELES  # 1*2*1 = 2
    long_total = num_vigas_x * VANO_X + num_vigas_y * VANO_Y
    CARGA_LOSA_TOTAL = W_LOSA * long_total

    return {
        "w_losa": W_LOSA,
        "q_total": Q_TOTAL,
        "carga_losa_total": CARGA_LOSA_TOTAL,
    }


def verificar_resultados(resultados):
    print("=" * 70)
    print("VERIFICACION DE RESULTADOS - MARCO 4 COLUMNAS, 1 PISO")
    print("=" * 70)

    cargas = verificar_carga_total()

    # 1. Equilibrio vertical
    print("\n1. EQUILIBRIO VERTICAL")
    print("-" * 50)
    total_fz = sum(d["fz"] for d in resultados["reacciones"].values())
    error_eq = abs(total_fz - cargas["carga_losa_total"]) / cargas["carga_losa_total"] * 100
    print(f"   Carga losa: {cargas['carga_losa_total']:.2f} kN")
    print(f"   w por viga: {cargas['w_losa']:.2f} kN/m")
    print(f"   Suma Fz:    {total_fz:.2f} kN")
    print(f"   Error:      {error_eq:.4f} %")
    print(f"   {'[OK]' if error_eq < 1 else '[ERROR]'}")

    # 2. Reacciones
    print("\n2. REACCIONES POR APOYO")
    print("-" * 50)
    for nodo, datos in resultados["reacciones"].items():
        print(f"   {nodo}: Fz={datos['fz']:.2f} kN")

    # 3. Simetria
    print("\n3. SIMETRIA")
    print("-" * 50)
    fzs = [d["fz"] for d in resultados["reacciones"].values()]
    print(f"   Fz max: {max(fzs):.2f}, Fz min: {min(fzs):.2f}")
    if max(fzs) > 0:
        var = (max(fzs) - min(fzs)) / max(fzs) * 100
        print(f"   Variacion: {var:.2f}%")

    # 4. Desplazamientos
    print("\n4. DESPLAZAMIENTOS")
    print("-" * 50)
    for nivel, datos in resultados["desplazamientos"].items():
        print(f"   {nivel}: ux={datos['ux']:.6e}, uy={datos['uy']:.6e}, uz={datos['uz']:.6e}")

    # 5. Fuerzas de elementos
    print("\n5. FUERZAS DE ELEMENTOS")
    print("-" * 50)
    for tag, datos in resultados["fuerzas_elementos"].items():
        print(f"   {tag}: N={datos['N1']:.2f}, V3={datos['V3_1']:.2f}, M2={datos['M2_1']:.2f}")

    # 6. Verificacion manual de columna
    print("\n6. VERIFICACION MANUAL - Columna esquina")
    print("-" * 50)
    # Cada columna esquina carga: tributary area = L/2 * L/2 = 3*3 = 9 m2
    carga_col = cargas["q_total"] * (VANO_X / 2) * (VANO_Y / 2)
    print(f"   Area tributaria: {VANO_X/2:.1f} x {VANO_Y/2:.1f} = {(VANO_X/2)*(VANO_Y/2):.1f} m2")
    print(f"   Carga axial esperada: {carga_col:.2f} kN")

    return {
        "total_fz": total_fz,
        "carga_esperada": cargas["carga_losa_total"],
        "error_pct": error_eq,
    }


def generar_reporte(resultados, verificacion):
    cargas = verificar_carga_total()
    reporte = f"""# Reporte de Verificacion - Marco 4 Columnas, 1 Piso

## Datos del Modelo
| Parametro | Valor |
|-----------|-------|
| Geometria | {LX} m x {LY} m x {LZ} m |
| Columnas | {B_COL*100:.0f} x {H_COL*100:.0f} cm |
| Vigas | {B_VIGA*100:.0f} x {H_VIGA*100:.0f} cm |
| f'c | {FC} MPa |
| EC | {EC:.0f} MPa |
| Carga losa | {Q_LOSA} kN/m2 (D) + {Q_VIVA} kN/m2 (L) |

## Verificacion de Equilibrio

| Concepto | Valor |
|----------|-------|
| Carga total en vigas | {cargas['carga_losa_total']:.2f} kN |
| Suma de reacciones Fz | {verificacion['total_fz']:.2f} kN |
| Error | {verificacion['error_pct']:.4f} % |

## Desplazamientos

| Nivel | ux (m) | uy (m) | uz (m) |
|-------|--------|--------|--------|
"""
    for nivel, datos in resultados["desplazamientos"].items():
        reporte += f"| {nivel} | {datos['ux']:.6e} | {datos['uy']:.6e} | {datos['uz']:.6e} |\n"

    reporte += """
## Fuerzas de Elementos

| Elemento | N (kN) | V2 (kN) | V3 (kN) | M2 (kN*m) | M3 (kN*m) |
|----------|--------|---------|---------|-----------|-----------|
"""
    for tag, datos in resultados["fuerzas_elementos"].items():
        reporte += f"| {tag} | {datos['N1']:.2f} | {datos['V2_1']:.2f} | {datos['V3_1']:.2f} | {datos['M2_1']:.2f} | {datos['M3_1']:.2f} |\n"

    return reporte


def main():
    resultados = cargar_resultados()
    verificacion = verificar_resultados(resultados)

    reporte = generar_reporte(resultados, verificacion)
    out_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), "resultados")
    report_file = os.path.join(out_dir, "verificacion.md")
    with open(report_file, "w", encoding="utf-8") as f:
        f.write(reporte)
    print(f"\nReporte generado: {report_file}")


if __name__ == "__main__":
    main()
