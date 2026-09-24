# Auditoría de diagramas y apoyos P1L4

Se revisaron 333 vigas y 129 columnas, en G, Q, EX, EY, C1, C2 y C3: **3.234 registros de fuerzas**. Se reprodujeron los siete análisis con OpenSees 3.8.0, sin modificar los datos ni los scripts de producción. Los resultados guardados coinciden exactamente con `ops.eleForce` de la nueva corrida; el problema no es un JSON desactualizado.

La revisión encuentra errores de interpretación de resultados, métodos de dibujo incompatibles y limitaciones del modelo estructural. Corregir únicamente el aspecto de los gráficos no resuelve esas causas.

## 1. Fuerzas globales interpretadas como locales — prioridad alta

`P1L3/carga_viva_sismo.py:1431` extrae `ops.eleForce`. El exportador P1L4 copia esos valores sin transformación, pero los describe como N, Vy, Vz, T, My, Mz locales.

Se contrastó cada registro con `ops.eleResponse(id, 'localForce')`: la rotación global→local, usando exactamente las transformaciones del modelo, coincide con OpenSees en los 3.234 registros (diferencia máxima observada: 0).

La documentación oficial distingue [globalForce y localForce](https://opensees.berkeley.edu/OpenSees/manuals/usermanual/259.htm). Los ejes deben construirse con el `vecxz` de [geomTransf Linear](https://opensees.github.io/OpenSeesDocumentation/user/manual/model/geomTransf/Linear.html).

| Caso | Vigas con componentes incorrectas | Columnas con componentes incorrectas |
|---|---:|---:|
| G | 143 | 129 |
| Q | 143 | 129 |
| EX | 111 | 89 |
| EY | 111 | 89 |
| C1 | 143 | 129 |
| C2 | 143 | 129 |
| C3 | 143 | 129 |

Se compararon las cinco componentes en I, 25%, centro, 75% y J, con tolerancia absoluta de 0,01 kN para fuerzas y 0,01 kN·m para momentos. El panel nuevo sólo se abre para vigas; en columnas se auditó la misma interpretación de las componentes, que también se utiliza en otros consumidores. Valores nulos o ejes coincidentes pueden ocultar el error.

Ejemplo: **E1_84, C1, centro del vano** (viga orientada en Y global, L=7,25 m):

| Componente | Fórmula del panel aplicada al JSON actual | Misma fórmula con fuerzas locales verificadas |
|---|---:|---:|
| My [kN·m] | -195,89 | 8,28 |
| Mz [kN·m] | 585,69 | -8,54 |
| Vy [kN] | -151,17 | 12,76 |
| Vz [kN] | -49,90 | -49,90 |
| N [kN, tracción positiva] | 12,76 | 151,17 |

Estos valores corregidos representan **el modelo actual**, no una corrección de su aplicación de cargas ni de sus conexiones.

La auditoría anterior de `B3003_V60/80` no detectaba esta causa: esa viga está alineada con +X global y su transformación es la identidad. Revisar sólo esa viga no valida el resto del edificio.

El panel nuevo reutilizó `ConvertOpenSeesEndForcesToInternalForces` y heredó este problema. Sus fórmulas de equilibrio no sustituyen la transformación a ejes locales.

## 2. Coexisten métodos de reconstrucción incompatibles — prioridad alta

- Panel nuevo: `DiagramController.cs:477` utiliza conversión de acciones nodales a esfuerzos internos y equilibrio. Es correcto para las cargas actualmente aplicadas una vez que recibe fuerzas locales y longitud analítica.
- Información del elemento: `ElementSelectable.cs:358` interpola directamente las acciones resistentes de I y J. Como tienen sentidos opuestos, puede producir un axial que cruza por cero cuando el esfuerzo interno es constante.
- Diagramas antiguos, tabla inferior y base de carga móvil: `DiagramController.cs:716`, `:779`, `:1020` usan interpolación directa y agregan `abs(uniformLoad)*L²*t*(1-t)/2` a Mz.
- El menú Python repite ese agregado en `P1L3/carga_viva_sismo.py:2283`.

Ese término parabólico no está ligado a los factores activos ni a las cargas realmente aplicadas. Para B3003, `uniformLoad=32,66560791725` kN/m y L=10 m: agrega **408,32 kN·m** en el centro, incluso con las fuerzas de todos los casos puestas a cero. Este defecto corresponde a la ruta antigua, no a la fórmula del panel nuevo.

Por eso cambiar de panel puede cambiar el resultado numérico. Debe existir una sola evaluación de esfuerzos internos para gráficos, tabla, selección y demanda-capacidad.

## 3. Las cargas de gravedad se concentran en los extremos — prioridad alta

El exportador usa `dead_nodal_loads` y las cargas nodales de `transfer_live_load`. `apply_nodal_loads` aplica fuerzas sin momentos nodales. No se utiliza `eleLoad` distribuida sobre las vigas en la corrida que alimenta P1L4.

Con ese modelo, entre los nodos de una barra descargada en su interior se obtiene axial y corte constantes y momento lineal. Dibujar una parábola posteriormente no convierte ese análisis en el de una viga cargada a lo largo del vano. Tampoco repartir qL/2 en los extremos reproduce los efectos flexionales de una carga uniforme en una viga con continuidad rotacional.

Se ejecutó una prueba independiente en OpenSees para L=6 m, q=10 kN/m, extremos restringidos en flexión:

- Dos fuerzas nodales de 30 kN: fuerzas internas de la viga iguales a cero; las cargas llegan directamente a los apoyos.
- `eleLoad -beamUniform` de 10 kN/m: cortes de extremo de 30 kN y momentos de empotramiento de magnitud 30 kN·m.

Se dejó libre únicamente el grado axial descargado de J para evitar un sistema de ecuaciones vacío; ambos extremos permanecen restringidos en flexión. Cambiar la carga a distribuida exige reanalizar y evitar contar nuevamente las mismas cargas nodales.

## 4. Los apoyos mostrados y los usados no son lo mismo — prioridad alta

`StructureViewer.cs:322` y `:323` llaman `CreateFixedSupport` para **ambos extremos de todos los elementos**. Es una etiqueta fabricada, no una consulta a `data.supports`. También ocurre con los muros.

El JSON contiene 26 apoyos, todos con seis restricciones. De las 924 incidencias de extremos de vigas y columnas, sólo 26 corresponden a nodos de esos apoyos. Los otros 898 aparecen igualmente como `fixed` en la información de selección.

Esto no significa que esos 898 extremos deban ser articulados: una unión viga-columna comparte desplazamientos y rotaciones con otros elementos. Una unión rígida entre barras no equivale a un empotramiento al suelo. El modelo usa `elasticBeamColumn` sin liberaciones de extremo.

Además, `P1L3/carga_viva_sismo.py:516` empotra automáticamente un nodo de cada componente sin apoyo declarado:

- 27 componentes de barras en total; 2 contienen apoyos declarados.
- **25 componentes adicionales, cada una formada por una viga desconectada**, reciben un empotramiento automático.
- Ejemplos: B3010_V60/80 → nodo 256, B3013_V60/80 → 257, B3017_V30/80 → 261.
- Otros 252 nodos sin conexión a barras se fijan automáticamente. Algunos corresponden a geometría que el modelo de barras no utiliza; no equivalen a 252 apoyos físicos cargados.
- OpenSees termina con 303 nodos restringidos: 26 declarados + 25 anclajes de componentes + 252 aislados.

Diez anclajes añadidos tienen reacción gravitacional no nula. En G absorben **253,45 kN** que no figuran en la suma de reacciones sobre apoyos declarados; en C1 absorben **355,16 kN**. El equilibrio global cierra al incluir todos los apoyos efectivamente creados.

Estos anclajes sí alteran el análisis. No conviene eliminarlos sin resolver antes la conectividad prevista: eso puede dejar mecanismos. La auditoría no cambió ni fusionó nodos.

## 5. Alcance del modelo y geometría visual

- Los 75 muros y 5 diafragmas del JSON no se crean como elementos de muro ni restricciones de diafragma en `build_model` de P1L3. El exportador estima las demandas P-M de muros por otro procedimiento. No son fuerzas internas extraídas de esos muros en el análisis del pórtico.
- Hay 70 grupos de nodos con coordenadas coincidentes. Coincidir visualmente no garantiza compartir grados de libertad; algunos pertenecen a geometría de losas o muros, por lo que no deben fusionarse automáticamente.
- `ClampColumnVisualEnds` modifica las cotas de 40 columnas del edificio 2 (por ejemplo, 0,16→0 y 4,12→4 m). Las columnas usan después esos extremos visuales en consultas. Las longitudes/ejes del cálculo deben venir de los nodos originales, aunque se conserve una corrección puramente visual.
- Los ejes locales que se imprimen en Unity se construyen con otra regla, distinta de `geomTransf`; hay que usar una definición común y transformar a coordenadas Unity sólo para dibujar.
- P-M utiliza `-forces[0]` como compresión y los índices 4/5 como momentos. Actualmente toma componentes globales; incluso al cambiar la exportación a local habrá que revisar el signo axial: con N interno=-FxI y compresión positiva, P=FxI. Cambiar sólo el exportador dejaría un error de signo.
- La carga móvil usa una viga aislada empotrada-empotrada en `MobileLoadController.cs:718`. Es una hipótesis local adicional, no la respuesta de toda la estructura; en `ElementSelectable` se suma a Vy/Mz aunque la carga vertical de las vigas horizontales del modelo actúa en el plano Vz/My. El panel nuevo muestra sólo la combinación base, mientras que el texto del elemento puede incluir ese agregado móvil.

## Corrección propuesta, en orden

1. Fijar un contrato de exportación explícito: acciones locales, ejes, longitud analítica, unidades, estado del análisis y cargas aplicadas. Obtener `localForce` directamente; mantener lo global separado si hace falta trazabilidad.
2. Unificar signos y evaluación de esfuerzos internos en todos los consumidores. No sumar cargas desde `uniformLoad` si no pertenecen al caso analizado. Validar G=Q=EX=EY=0 y barras orientadas en X, Y y Z.
3. Mostrar las restricciones reales y distinguir apoyo al suelo, unión entre barras y anclaje automático.
4. Resolver las 25 vigas desconectadas y definir la participación de muros/diafragmas según el modelo deseado. Aplicar las cargas distribuidas de forma consistente si se quieren estudiar los esfuerzos de vano por gravedad.
5. Reanalizar, exportar y comprobar casos patrón, equilibrio, superposición y correspondencia entre paneles. El diseño de los paneles puede permanecer igual.

No se modificaron scripts de producción, apoyos, cargas, geometría ni JSON de resultados durante esta revisión. Se añadieron el script de auditoría y sus informes. La comprobación certifica la reproducción numérica del modelo existente; no valida sus hipótesis estructurales.

## Reproducibilidad

Ejecutar con un Python 3.12 que pueda importar los paquetes del proyecto:

```powershell
python P1L4/auditar_diagramas.py --reanalyze
```

El lanzador de `.venv` apunta a una instalación de Python que no se pudo iniciar. Para esta revisión se usó Python 3.12.14 disponible en el entorno de trabajo y los paquetes OpenSees 3.8.0 ya instalados en `.venv/Lib/site-packages`, sin reinstalar ni modificar el entorno.

`reports/auditoria_diagramas.json` contiene los resultados por cada elemento/caso, ejemplos completos, componentes desconectadas, anclajes, comparaciones directas con OpenSees y el caso patrón. SHA-256 del JSON de Unity auditado: `83ede2d48b1467246ef21146245a83b39f4becd13736113acba0242c64724c1e`.
