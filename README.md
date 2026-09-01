# Proyecto MCOC

Codigos en Python para analisis estructural 2D y 3D.

## Estructura

- `semana_pasada_marco_2d/`: trabajo anterior del marco 2D, con reacciones, axial, corte, momento, deformada y diagramas.
- `semana_actual_benchmark_3d/`: entregable actual del benchmark 3D OpenSeesPy con informe, verificacion, defensa e imagen del modelo.
- `unity_visualizador/`: scripts C# para visualizar la estructura 3D en Unity y seleccionar elementos.
- `semana_actual_benchmark_3d/sap2000_comparacion/`: guia para comparar el benchmark 3D con SAP2000.
- `edificio_ingenieria_uandes/project/`: geometria estructural inicial parametrica del edificio de Ingenieria UANDES.
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

## Entrega Canvas

- Repositorio: `https://github.com/Santiago411323/Trabajo-MCOC`
- Informe Markdown: `semana_actual_benchmark_3d/LAB_semana1_benchmark_3D.md`
- Archivo de verificacion: `semana_actual_benchmark_3d/resultados_verificacion_3d.md`
- Guia de defensa: `semana_actual_benchmark_3d/DEFENSA_individual_3D.md`

## Instalar dependencias

```bat
pip install -r requirements.txt
```
