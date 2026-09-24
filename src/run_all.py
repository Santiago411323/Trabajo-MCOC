"""
Script Principal - Ejecutar todo el pipeline
=============================================
1. Ejecuta el modelo OpenSeesPy
2. Ejecuta la verificación
3. Genera visualizaciones

Uso: python run_all.py
"""

import subprocess
import sys
import os


def run_script(script_name, description):
    """Ejecutar un script de Python"""
    print(f"\n{'='*60}")
    print(f"  {description}")
    print(f"{'='*60}")

    script_path = os.path.join(os.path.dirname(__file__), script_name)
    result = subprocess.run(
        [sys.executable, script_path],
        capture_output=False,
        text=True
    )

    if result.returncode != 0:
        print(f"\n  ERROR ejecutando {script_name}")
        return False
    return True


def main():
    print("=" * 60)
    print("  PIPELINE COMPLETO - MODELO ESTRUCTURAL 3D")
    print("=" * 60)

    steps = [
        ("modelo_3d.py", "PASO 1: Ejecutando modelo OpenSeesPy"),
        ("verificacion.py", "PASO 2: Ejecutando verificación"),
        ("visualizacion.py", "PASO 3: Generando visualizaciones"),
    ]

    all_ok = True
    for script, desc in steps:
        if not run_script(script, desc):
            all_ok = False
            break

    print("\n" + "=" * 60)
    if all_ok:
        print("  PIPELINE COMPLETADO EXITOSAMENTE")
        print("  Resultados en: resultados/")
    else:
        print("  PIPELINE FALLÓ - Revisar errores arriba")
    print("=" * 60)


if __name__ == "__main__":
    main()
