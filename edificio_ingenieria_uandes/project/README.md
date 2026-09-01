# UANDES Structural Geometry

Modelo geometrico inicial del Edificio de Ingenieria UANDES.

No contiene analisis estructural. Solo prepara geometria, nodos, fundaciones, vigas de fundacion, muros/pilares, viewers y chequeos.

## Ejecutar

Desde la raiz del repositorio:

```bat
.venv\Scripts\python.exe edificio_ingenieria_uandes\project\main.py
```

## Archivos

- `geometry_data.py`: datos parametricos, grillas, niveles y entidades estructurales.
- `geometry.py`: clases de datos.
- `structural_geometry.json`: base de datos unica de geometria.
- `checks.py`: chequeos geometricos.
- `viewer_2d.py`: viewer 2D Plotly.
- `viewer_3d.py`: viewer 3D Plotly.
- `build_opensees.py`: inicializa OpenSees y crea solo nodos con coordenadas validas.
- `main.py`: genera JSON, ejecuta chequeos y crea viewers.
- `outputs/structural_2d.html`: viewer 2D generado.
- `outputs/structural_3d.html`: viewer 3D generado.

## Datos pendientes

No se inventan dimensiones. Quedan pendientes hasta recibir informacion explicita:

- Distancias entre ejes X.
- Distancias entre EJE 1, EJE 2 y EJE 3.
- Cota de subterrraneo.
- Cota de piso 1.
- Dimensiones de pilares.
- Dimensiones de fundaciones.
- Espesores y longitudes exactas de muros perimetrales.
- Poligonos reales de radier.
