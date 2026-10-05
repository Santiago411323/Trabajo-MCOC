# Auditoría de vigas — 5 de octubre de 2026

Se revisaron **453 vigas** del JSON utilizado por P1L4. La geometría iPhone coincide: **sí**.

Coordenadas OpenSees: X/Y en planta y Z elevación; unidades en metros. Tolerancia geométrica: 0,1 mm. Esta auditoría no cambia barras, cargas, IDs ni resultados.

- Solapes de longitud positiva: 3.
- Uniones colineales sin columna/viga perpendicular ni otra conexión detectada: 4, agrupadas en 4 tramos continuos.
- Uniones sin esos separadores pero con conexiones de muro, cambio de sección u otra condición: 4.
- Uniones colineales justificadas por columna o viga perpendicular: 332.

## Vigas superpuestas

| Vigas | Tipo | Solape (m) |
| --- | --- | --- |
| E1_64.1 / E1_209 | exacto | 2.2 |
| E1_93.1 / E1_210 | exacto | 2.61 |
| E1_112.1 / E1_205 | exacto | 2.2 |

## Tramos físicos continuos candidatos

| Vigas actuales (orden geométrico) | Nivel Z | Desde (X,Y,Z) | Hasta (X,Y,Z) | Longitud | Nodos intermedios |
| --- | --- | --- | --- | --- | --- |
| E1_105 + E1_106 | 12.0 | (5.0, 0.0, 12.0) | (10.0, 0.0, 12.0) | 5.0 | 91 |
| E1_113 + E1_114 | 12.0 | (5.0, -7.25, 12.0) | (10.0, -7.25, 12.0) | 5.0 | 90 |
| E1_149 + E1_150 | 16.0 | (5.0, 0.0, 16.0) | (10.0, 0.0, 16.0) | 5.0 | 120 |
| E1_157 + E1_158 | 16.0 | (5.0, -7.25, 16.0) | (10.0, -7.25, 16.0) | 5.0 | 119 |

## Casos que requieren decisión

| Vigas | Nodo(s) | Posición | Motivo |
| --- | --- | --- | --- |
| E1_64.1 + E1_64.2 | [151] | (2.2, -7.25, 8.0) | otra viga colineal E1_209 |
| E1_64.2 + E1_209 | [151] | (2.2, -7.25, 8.0) | otra viga colineal E1_64.1 |
| E1_93.1 + E1_93.2 | [153] | (0.0, -9.86, 8.0) | otra viga colineal E1_210 |
| E1_93.2 + E1_210 | [153] | (0.0, -9.86, 8.0) | otra viga colineal E1_93.1 |

## Hipótesis después de eliminar duplicadas

Se evaluó **solo en memoria** la geometría sin E1_205, E1_209 y E1_210. No se reanalizó ni se cambiaron los archivos del modelo. Son los bordes especiales del voladizo que coinciden con tramos de vigas originales. Ambas barras de cada par están presentes en los resultados analíticos: la duplicación afecta la rigidez, no solo el dibujo. Conservar las cargas existentes requiere revisar el generador y volver a calcular.

Con esa hipótesis quedan **seis** tramos candidatos a una sola viga física, sin conexiones ambiguas:

- E1_64.1 + E1_64.2: 5.0 m, nivel Z=8.0.
- E1_93.2 + E1_93.1: 4.12 m, nivel Z=8.0.
- E1_105 + E1_106: 5.0 m, nivel Z=12.0.
- E1_113 + E1_114: 5.0 m, nivel Z=12.0.
- E1_149 + E1_150: 5.0 m, nivel Z=16.0.
- E1_157 + E1_158: 5.0 m, nivel Z=16.0.

Origen de los solapes: el generador `edificio 1/modelo_pasillos.py` añade los bordes del voladizo Y− sobre vigas ya existentes; `P1L3/carga_viva_sismo.py` subdivide las vigas largas en esos nodos, pero no elimina esas vigas coincidentes. Los nodos X=7,51 persisten en las vigas aunque las columnas de esos niveles fueron eliminadas.


## Ejemplo E1_113 / E1_114

Comparten el nodo [90] en (7.51, -7.25, 12.0), sin columna ni otra viga. Son 5.0 m continuos y sección V60/80.
La unión E1_112.2 / E1_113 en (5.0, -7.25, 12.0) sí se separa por: viga perpendicular E1_132.

## Diferencia entre viga física y barra analítica

Una viga física continua puede estar representada por varias barras OpenSees para conectar otros componentes o distribuir cargas. No se deben sumar ni promediar sus resultados para inventar un nuevo ID. Si se decide fusionar barras del cálculo, hay que conservar cargas y conexiones, reanalizar y regenerar resultados y correspondencias Unity/AR/VR. Una agrupación solo visual debe conservar los IDs analíticos de cada tramo.

Datos completos, incluidas todas las uniones justificadas y conexiones de muro: `auditoria_vigas.json`.
