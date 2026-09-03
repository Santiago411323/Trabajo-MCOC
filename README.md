# Proyecto MCOC

Codigos en Python para analisis estructural 2D y 3D.

## Estructura

- `semana_pasada_marco_2d/`: trabajo anterior del marco 2D, con reacciones, axial, corte, momento, deformada y diagramas.
- `semana_actual_benchmark_3d/`: entregable actual del benchmark 3D OpenSeesPy con informe, verificacion, defensa e imagen del modelo.
- `semana_actual_benchmark_3d/sap2000_comparacion/`: guia para comparar el benchmark 3D con SAP2000.
- `edificio_ingenieria_uandes/project/edificio 1/`: edificio 1 (real Ingenieria UANDES, pasillos) con `analisis_gravedad.py` (areas tributarias, cargas D/L separadas, combinaciones 1.4D y 1.2D+1.6L, reduccion nodal), `resultados/` y su `unity_visualizador/` (diagramas de axial, corte y momento).
- `edificio_2/`: edificio 2 con `modelo_python/` (areas tributarias, cargas D/L, combinaciones) y su `unity_visualizador/` propio.
- `requirements.txt`: dependencias de Python.

## Ejecutar

Marco 2D de la semana pasada:

```bat
python semana_pasada_marco_2d\analisis_marco.py
```

Si `python` no funciona en Windows:

```bat
py semana_pasada_marco_2d\analisis_marco.py
```

Benchmark 3D de esta semana:

```bat
python semana_actual_benchmark_3d\benchmark_3d_opensees.py
```

El benchmark 3D genera `semana_actual_benchmark_3d\benchmark_3d_modelo.png`, `semana_actual_benchmark_3d\resultados_verificacion_3d.md` y muestra desplazamientos, reacciones, equilibrio global y fuerzas internas.

Edificio 2:

```bat
python "edificio_2\modelo_python\main.py"
```

Unity del edificio 2:

```text
edificio_2/unity_visualizador
```

Edificio 1 (analisis de gravedad):

```bat
py "edificio_ingenieria_uandes\project\edificio 1\analisis_gravedad.py"
```

Unity del edificio 1 (genera/Abre `edificio_ingenieria_uandes/project/edificio 1/unity_visualizador`):

```text
edificio_ingenieria_uandes/project/edificio 1/unity_visualizador
```

## Entrega Canvas

- Repositorio: `https://github.com/Santiago411323/Trabajo-MCOC`
- Informe Markdown: `semana_actual_benchmark_3d/LAB_semana1_benchmark_3D.md`
- Archivo de verificacion: `semana_actual_benchmark_3d/resultados_verificacion_3d.md`
- Guia de defensa: `semana_actual_benchmark_3d/DEFENSA_individual_3D.md`

## Instalar dependencias

```bat
pip install -r requirements.txt
```
