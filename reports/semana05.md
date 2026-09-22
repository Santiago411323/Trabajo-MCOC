# Semana 5 - Visualizador estructural interactivo Unity

**Proyecto:** modelo estructural UANDES, P1L4.
**Unidades:** kN, m, kN.m.
**Viewer:** `P1L4/unity_visualizador`.
**Datos:** `P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json`.

## 1. Alcance

Se implemento un visualizador Unity para revisar el modelo estructural exportado desde OpenSeesPy, con seleccion de elementos, informacion de demanda-capacidad, curvas P-M, modos de resultados y una sidequest de carga movil sobre vigas.

El objetivo de la interfaz es responder rapidamente preguntas de revision estructural:

| Pregunta | Implementacion en viewer |
|---|---|
| Donde esta el elemento | Panel derecho con piso/nivel, nodos, edificio de origen y centro aproximado |
| Como esta apoyado | Restricciones de nodos y apoyo `fixed` en el panel para columnas de arranque |
| Que lo carga | Resumen tributario, combinaciones `C1/C2/C3` y carga movil local |
| Como se deforma | Modo `Deformada` desde barra superior |
| Que fuerzas tiene | Diagramas `Axial`, `Corte`, `Momento` y tabla de demandas |
| Cuanta capacidad tiene | Panel P-M con punto activo y razon `C = Mdem/Mcap` |

## 2. Funciones Principales

| Funcion | Archivo principal | Estado |
|---|---|---|
| Barra superior con combinaciones, resultados y vistas | `StructureViewer.cs` | Implementado |
| Filtro por piso y toggles de capas | `StructureViewer.cs` | Implementado |
| Seleccion de vigas, columnas y muros | `ElementPicker.cs`, `ElementSelectable.cs` | Implementado |
| Panel derecho con ubicacion y demandas | `ElementSelectable.cs`, `ElementPicker.cs` | Implementado |
| Curvas P-M de columnas/muros | `PMPanel.cs`, `UnityData.cs` | Implementado |
| Diagramas globales de resultados | `DiagramController.cs` | Implementado |
| Vistas ISO/TOP/FRONT/RIGHT | `OrbitCamera.cs` | Implementado |
| Carga movil local sobre viga seleccionada | `MobileLoadController.cs` | Implementado |

## 3. Modificaciones de Semana 5

### Modificacion 1: UI de revision estructural

Se reorganizo la interfaz para que el usuario pueda cambiar combinacion activa (`C1`, `C2`, `C3`), resultado graficado y vista de camara desde una barra superior. El panel izquierdo quedo reservado para capas, filtros y resumen tributario; el panel derecho muestra solamente la informacion del elemento seleccionado.

La tabla de demandas se simplifico para mostrar solo la combinacion activa. Esto evita mezclar en pantalla valores de distintas combinaciones y hace que el punto rojo en el diagrama P-M coincida con los valores mostrados en la tabla.

### Modificacion 2: demanda-capacidad P-M

Para columnas y muros se agrego una lectura directa de demanda-capacidad:

```text
C = Mdem / Mcap
```

El viewer grafica la envolvente P-M de capacidad y superpone solamente el punto de la combinacion activa. El panel reporta `P`, `M`, `Mcap` y `C`, de modo que la decision visual y la tabla usan la misma demanda.

### Modificacion 3: apoyos y base visual

Las columnas que llegan al nivel inferior visible se alinean al mismo nivel de base para evitar sobrepasos visuales. Solo esas columnas reciben un apoyo inferido de texto `fixed` en el panel; las columnas intermedias no reciben apoyos artificiales.

La semantica usada para el apoyo inferido es empotramiento de 6 GDL:

```text
ux = uy = uz = rx = ry = rz = 1
```

## 4. Resultados Usados

El JSON exportado incluye casos base y combinaciones:

| Caso | Descripcion |
|---|---|
| `G` | Permanente |
| `Q` | Viva |
| `EX` | Sismo pseudoestatico X |
| `EY` | Sismo pseudoestatico Y |
| `C1`, `C2`, `C3` | Combinaciones de revision en viewer |

Demandas representativas usadas para validar la lectura P-M:

| Combo | `P` [kN] | `M` [kN.m] |
|---|---:|---:|
| `G` | 2.364 | 1.856 |
| `Q` | 1.834 | 1.405 |
| `EX` | 78.598 | 185.569 |
| `EY` | -63.968 | 135.616 |
| `C1` | 14.067 | 31.913 |
| `C2` | 39.654 | 85.052 |
| `C3` | -33.092 | 79.938 |

## 5. Verificaciones de Superposicion

La linealidad de las combinaciones se verifico con los casos base exportados desde OpenSeesPy. La respuesta combinada se obtiene como suma ponderada de respuestas independientes:

```text
R_combo = aG R_G + aQ R_Q + aEX R_EX + aEY R_EY
```

| Verificacion | Resultado |
|---|---|
| Desplazamientos | Error maximo del orden de precision numerica |
| Reacciones globales | Conservan equilibrio con los patrones base |
| Fuerzas internas | La tabla del viewer y el punto P-M leen la misma combinacion activa |

La prueba numerica de Semana 3 comparo suma ponderada contra corrida explicita simultanea y obtuvo errores maximos de `7.15e-16 m` en desplazamientos, `8.89e-11 kN` en reacciones globales y `1.59e-8 kN` en fuerzas internas. Semana 5 reutiliza esa base para visualizacion interactiva.

## 6. Sidequest: Carga Movil

Se implemento una carga puntual movil sobre la viga seleccionada. El usuario controla la magnitud `P [kN]` y la posicion `x/L`.

El reparto local se calcula como viga simplemente apoyada equivalente:

```text
RI = P (1 - x/L)
RJ = P (x/L)
RI + RJ = P
```

El panel muestra reaccion izquierda, reaccion derecha y error de conservacion. Por defecto se muestra solo la flecha de carga; los diagramas locales de axial, corte y momento aparecen solo si el usuario activa sus toggles.

Esta sidequest es una herramienta visual y didactica dentro de Unity. No reejecuta OpenSees en tiempo real ni reemplaza el analisis global del modelo.

## 7. Limitaciones y Validacion Pendiente

El codigo C# fue revisado con `git diff --check`; no se detectaron errores de whitespace, solo avisos normales de CRLF en Windows.

La validacion final debe hacerse en Unity Editor porque no hay compilador C# disponible desde terminal en este entorno. Se debe revisar visualmente:

| Revision en Unity | Esperado |
|---|---|
| Seleccionar columna de base | Panel muestra apoyo `fixed` en el nodo inferior |
| Seleccionar columna intermedia | No aparece apoyo artificial si no llega a base |
| Activar `C1/C2/C3` | Tabla y punto P-M cambian juntos |
| Seleccionar viga | Panel de carga movil controla flecha y conserva `RI + RJ = P` |
| Toggles de carga movil | Diagramas aparecen solo al activarlos |
