# Auditoría de vigas — 5 de octubre de 2026

Se revisaron **444 vigas** del JSON utilizado por P1L4. La geometría iPhone coincide: **sí**.

Coordenadas OpenSees: X/Y en planta y Z elevación; unidades en metros. Tolerancia geométrica: 0,1 mm. Esta auditoría no cambia barras, cargas, IDs ni resultados.

- Solapes de longitud positiva: 0.
- Uniones colineales sin columna/viga perpendicular ni otra conexión detectada: 0, agrupadas en 0 tramos continuos.
- Uniones sin esos separadores pero con conexiones de muro, cambio de sección u otra condición: 0.
- Uniones colineales justificadas por columna o viga perpendicular: 328.

## Vigas superpuestas

No hay vigas con solape exacto ni parcial de sus ejes. Esto no confunde dos vigas contiguas que solo comparten un extremo.

## Tramos físicos continuos candidatos

| Vigas actuales (orden geométrico) | Nivel Z | Desde (X,Y,Z) | Hasta (X,Y,Z) | Longitud | Nodos intermedios |
| --- | --- | --- | --- | --- | --- |

## Casos que requieren decisión

| Vigas | Nodo(s) | Posición | Motivo |
| --- | --- | --- | --- |

## Hipótesis después de eliminar duplicadas

Evaluación solo en memoria: se excluyen ninguna barra (no se detectaron duplicadas). El auditor no recalcula ni escribe el modelo.

Quedan 0 tramos candidatos y 0 conexiones ambiguas.


Origen de los solapes: el generador `edificio 1/modelo_pasillos.py` añade los bordes del voladizo Y− sobre vigas ya existentes; `P1L3/carga_viva_sismo.py` subdivide las vigas largas en esos nodos, pero no elimina esas vigas coincidentes. Los nodos X=7,51 persisten en las vigas aunque las columnas de esos niveles fueron eliminadas.


## Ejemplo E1_113 / E1_114

La unión E1_112.2 / E1_113 en (5.0, -7.25, 12.0) sí se separa por: viga perpendicular E1_132.

## Diferencia entre viga física y barra analítica

Una viga física continua puede estar representada por varias barras OpenSees para conectar otros componentes o distribuir cargas. No se deben sumar ni promediar sus resultados para inventar un nuevo ID. Si se decide fusionar barras del cálculo, hay que conservar cargas y conexiones, reanalizar y regenerar resultados y correspondencias Unity/AR/VR. Una agrupación solo visual debe conservar los IDs analíticos de cada tramo.

Datos completos, incluidas todas las uniones justificadas y conexiones de muro: `auditoria_vigas.json`.
