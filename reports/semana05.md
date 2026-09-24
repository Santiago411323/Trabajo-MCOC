# Semana 5 - Visualizador estructural interactivo Unity

**Proyecto:** modelo estructural UANDES, P1L4.
**Unidades:** kN, m, kN.m.
**Viewer:** `P1L4/unity_visualizador`.
**Datos:** `P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json`.
**Base del informe:** `d4e1468` + correcciones de modelo y visor de semana 5 (ver `reports/correccion_modelo_semana5.md`).

---

## 1. Funciones implementadas

Datos de referencia verificados en el JSON exportado (v2.0, modelo corregido): 759 nodos,
817 barras (124 columnas, 453 tramos de viga, 75 barras de muro `muro_eq` y 165 brazos
rigidos que representan los 15 muros estructurales / 75 paneles), 40 apoyos empotrados
(25 columnas + 15 bases de muro), diafragma rigido en 5 niveles, 226 losas,
3472 registros de desplazamiento (496 nodos x 7 casos) y 5719 registros de fuerzas (817 x 7 casos: `G`, `Q`, `EX`, `EY`, `C1`, `C2`, `C3`), en
acciones LOCALES de extremo (`p1l4.elementForceCoordinates = "local"`).

| Funcion | Archivo principal | Estado |
|---|---|---|
| Navegacion | `OrbitCamera.cs` | Implementado |
| Seleccion de elementos | `ElementPicker.cs`, `ElementSelectable.cs` | Implementado |
| Apoyos | `StructureViewer.cs` | Implementado |
| Ejes | `StructureViewer.cs`, `ElementSelectable.cs` | Implementado |
| Cargas | `StructureViewer.cs`, `UnityData.cs` | Implementado |
| Areas tributarias | `StructureViewer.cs` | Implementado |
| Deformada | `DiagramController.cs` | Implementado |
| Diagramas | `DiagramController.cs` | Implementado |
| Superposicion | `DiagramController.cs`, `PMPanel.cs`, `UnityData.cs` | Implementado |
| P-M | `PMPanel.cs`, `UnityData.cs` | Implementado |
| Modificacion del modelo | scripts Python (`P1L2/`, `P1L3/`, `P1L4/exportar_resultados_unity.py`) | No automatica en viewer (flujo manual, seccion 2) |

### Detalle por funcion

- **Navegacion** (`OrbitCamera.cs`): orbita con boton derecho (x/ySpeed con clamp de elevacion -10..80), pan con boton central o flechas, zoom con rueda (distancia 5..120). Presets `ISO` (45,30), `TOP` (0,80), `FRONT` (0,8), `RIGHT` (90,8). `FocusOn(point, distance)` centra el pivot al seleccionar un elemento.
- **Seleccion**: `ElementPicker` hace raycast contra `ElementSelectable` (columnas, vigas, muros); objetos sin datos estructurales caen en `InfoSelectable` (losas/diafragmas), que muestran un panel de info basico. `Selected` es la referencia compartida por el resto de paneles.
- **Apoyos**: los 40 apoyos declarados (25 columnas + 15 bases de muro) se dibujan como cubos empotrados (0.55x0.14x0.55, amarillo) con etiqueta `N{n}\nEmpotrado`; semantica `ux=uy=uz=rx=ry=rz=1`. OpenSees no necesita anclajes automaticos: los nodos restringidos son estos 40 (mas los nodos maestros de diafragma, que solo restringen uz/rx/ry).
- **Ejes**: `CreateGlobalAxes` dibuja ejes X (rojo), Y (verde), Z (azul) globales; `ElementSelectable` reporta ejes locales X', Y', Z' por extremo.
- **Cargas**: D y Q de cada viga como carga uniforme (`eleLoad -beamUniform`, exportada en `p1l4.elementLoads`); sismo pseudoestatico `EX`/`EY` repartido F_i = C*W_i en los nodos de cada piso (total 6843.8 kN por direccion); cargas puntuales `pointLoads` como flechas rojas (solo si |fz| > 0.001).
- **Areas tributarias**: `tributaryList` (5 pisos) resume por piso `area_total`, `carga_total`, cantidad de vigas; ejemplo verificado: `CIELO_1` -> 735.52 m2, 8186.78 kN, 44 vigas; `CIELO_1S` -> 139.33 m2, 1550.84 kN, 7 vigas.
- **Deformada**: con combo activo, amplifica desplazamientos a 6% de la altura de cada edificio (escala aproximada edificio_1 ~67x, edificio_2 ~7.5x); linea gris de referencia (ancho 0.04) + linea verde (0.16) desplazada.
- **Diagramas**: teclas 0-3 (Axial, Corte, Momento) para vigas, columnas y enlaces, dibujados en el plano local dominante y el momento del lado traccionado; momentos parabolicos en los vanos por la carga distribuida + tabla compacta del elemento seleccionado (extremos I/J, centro, maximo absoluto).
- **Superposicion**: la combinacion activa `C1/C2/C3` se propaga a fuerzas, deformada, tabla y P-M; satisfacen `R_Cn = aG*R_G + aQ*R_Q + aEX*R_EX + aEY*R_EY` (verificado, seccion 3).
- **P-M**: curvas `COL70/70_FIBER` (5 puntos) y `W_DPRIME_OPENING_TO_3` (24 puntos) + demanda de la combinacion activa y razon `C = Mdem/Mcap`.

---

## 2. Modificacion

Dos modificaciones completas, con flujo `interfaz/dato -> modelo -> OpenSees -> resultados -> Unity`.
La primera es automatica; la segunda es manual y reproducible (requiere reexportar el JSON).

### Modificacion A (automatica): punto de traccion pura en la curva P-M del muro

Cambia la informacion de capacidad del muro `W_DPRIME_OPENING_TO_3`.

1. **Dato**: fy y As total del muro (seccion de borde).
2. **Modelo**: `P1L3/carga_viva_sismo.py::wall_pm_curve()`; agrega el punto de traccion
   pura `P_t = -fy * As_tot` con `M = 0` y reordena la envolvente por P creciente.
3. **Resultados**: regenera `P1L3/resultados/part_e_wall.json` (24 puntos), `P_M_wall.png`
   y `carga_viva_sismo.json`.
4. **Unity**: `P1L4/exportar_resultados_unity.py` lee el conteo de puntos de forma dinamica
   (`{len(wall_points)}` en `interpretation` y nota con `curva_muro_n`) y regenera
   `estructura_p1l4_unity.json`; `PMPanel.cs` dibuja la envolvente con la etiqueta del punto.

Flujo reproducible:

```text
.venv\Scripts\python.exe P1L3\carga_viva_sismo.py --muro-pm
.venv\Scripts\python.exe P1L4\exportar_resultados_unity.py
```

Verificacion:

```text
> json: len(curvas['W_DPRIME_OPENING_TO_3'].points) == 24
> primer punto:  {"label": "P/Pn0=-0.07", "P_kN": -3610.066950093103, "M_kN_m": 0.0}
> py_compile de ambos scripts: OK
```

### Modificacion B (manual, reproducible): demanda P-M por combinacion en el panel y deformada con escala por edificio

1. **Interfaz/dato**: seleccion de elemento + combo activo (`C1/C2/C3`).
2. **Modelo/OpenSees**: el analisis lineal exporta 7 casos (`G`, `Q`, `EX`, `EY`, `C1`, `C2`, `C3`)
   con fuerzas internas (12 componentes por extremo) y desplazamientos por nodo y combo.
3. **Resultados**: `wallRegistry.demands` guarda por panel y por combo `P_kN`/`M_kN_m` (75/75 paneles), ahora obtenidos del analisis (barras `muro_eq`).
4. **Unity**: `PMPanel.cs` superpone el punto de la combinacion activa y reporta
   `P`, `M`, `Mcap`, `C = Mdem/Mcap`; `DiagramController.cs` dibuja la deformada del combo
   activo con escalas por edificio (6% de la altura, referencia gris + linea verde).

No es automatica: el JSON debe regenerarse cada vez que cambien 1-3 (comando `exportar_resultados_unity.py`).
Pasos manuales:

```text
1. Correr el analisis y exportar:  .venv\Scripts\python.exe P1L4\exportar_resultados_unity.py
2. Copiar estructura_p1l4_unity.json a Assets\Resources\ (si no se genero ahi)
3. Abrir/recargar Unity para que Resources se reimporte
4. Resultado esperado: tabla y punto P-M del combo activo coinciden; deformada 6% altura por edificio
```

---

## 3. Superposicion interactiva

Se verifican los tres estados `C1`, `C2`, `C3` contra los resultados numericos del JSON.

Regla aplicada sobre fuerzas internas (componentes `G`,`Q`,`EX`,`EY`,`C1`,`C2`,`C3` del mismo id):

```text
R_C1 = 1.0*R_G + 0.5*R_Q + 0.3*R_EX + 0.2*R_EY
R_C2 = 1.0*R_G + 0.5*R_Q + 0.3*R_EX - 0.2*R_EY
R_C3 = 1.0*R_G + 0.5*R_Q - 0.3*R_EX + 0.2*R_EY
```

| Estado | Verificion numerica | Resultado |
|---|---|---|
| `C1` | Suma ponderada vs `elementForces` de C1 (817 barras, 12 componentes) | Error maximo absoluto < `1e-6` kN / kN.m |
| `C2` | Idem para C2 | Error maximo absoluto < `1e-6` kN / kN.m |
| `C3` | Idem para C3 | Error maximo absoluto < `1e-6` kN / kN.m |

Complementados con desplazamientos del modelo corregido: C1 uz min = -0.0303 m (N346), C2 = -0.0317 m (N322),
C3 = -0.0316 m (N322); max|ux| = 0.0100 m (C3, N338); max|uy| = 0.0073 m (C2, N347).
El modelo anterior daba uz = -0.092 m por las vigas desconectadas en voladizo.
Errores de superposicion del modelo corregido < 1e-6 kN (script `P1L4/tests/verificar_modelo.py`).

En el viewer, cambiar `C1/C2/C3` en la barra superior debe actualizar simultaneamente la deformada,
los diagramas, la tabla de valores y el punto rojo del P-M a la misma demanda.

---

## 4. Sidequest: carga movil

Implementada en `MobileLoadController.cs` (viga seleccionada).

- **Regla fisica**: carga puntual `P` sobre la viga seleccionada tratada como viga local **empotrada-empotrada** (Vz/My).
  La persona camina sobre la losa: su carga se reparte a los apoyos que la rodean (la viga o muro mas cercano en
  +X, -X, +Z y -Z) por franjas cruzadas: una franja en X y otra en Z, simplemente apoyadas, se reparten P segun su
  rigidez bajo la carga (k = L/(a*b)^2) y cada franja reparte su parte por la regla de la palanca. La fraccion de la
  viga varia suave con la distancia: 100 % sobre la viga, 47 % a 1 m y 12 % a 3 m de E1_13 (bahia 5 x 8.9 m).
  La viga seleccionada recibe su fraccion como carga puntual en la proyeccion de la persona; el panel lista cada
  apoyo con su % y kN y verifica que la suma sea 100 %.
  Boton **Caminar automatico**: sobre una viga recorre de I a J y vuelve; los ajustes manuales lo detienen.
- **Panel** (`PanelRect = (360, 150, 380, 372)`, arrastrable): toggle `Activar carga movil`, boton
  `Caminar automatico`, `P [kN]` (0..300, botones +/-10), `Posicion X` y `Posicion Z` sobre la losa del
  nivel (0..1, botones +/-0.05, ya no saltan a 0 %/100 %), toggles `Axial`, `Vz`, `My`.
- **Viga local empotrada-empotrada** (`FrameForces.EvaluateFixedFixedPointLoad`, a = x, b = L - x):

```text
RI = P' b^2 (L + 2a) / L^3        MI = -P' a b^2 / L^2        P' = P * (1 - d/b_trib)
V(x) = RI (x < a),  RI - P' (x >= a)
M(x) = MI + RI x - P' <x - a>
Caso patron P = 100 kN, L = 10 m, a = 5 m: RI = 50 kN, MI = MJ = -125 kN*m, M centro = +125 kN*m
```

- **Reparto aprox. a columnas** (didactico, independiente del reparto losa -> vigas): P se reparte a las 4 columnas mas cercanas del nivel con pesos
  1/(d^2 + 0.05); el panel muestra cada reaccion y `Conservacion: suma=.. | error=..` (0 por construccion).
- **Respuesta visual**: persona animada en la posicion de la carga, flechas de reaccion en las columnas,
  y lineas sobre la viga seleccionada: base OpenSees (tenue) y base + carga movil (fuerte) para axial,
  Vz y My. La tabla/panel de diagramas muestra "OpenSees + carga movil local" solo cuando esta activa.

Aclaracion: es didactica/local dentro de Unity; no reejecuta OpenSees en tiempo real.

---

## 5. UX estructural

Evaluacion de si el viewer responde las preguntas de revision estructural.

| Pregunta | Implementacion | Evaluacion |
|---|---|---|
| Donde esta el elemento | Seleccion + `FocusOn` de camara; panel derecho con piso/nivel, nodos, edificio, centro aproximado | Si |
| Como esta apoyado | Restricciones de nodos por extremo; apoyo inferido `fixed` (6 GDL) en columnas de base | Si (solo base) |
| Que lo carga | Resumen tributario por piso (area, carga total, vigas), combo activo, carga movil local | Si |
| Como se deforma | Modo `Deformada` con escala 6% de altura por edificio + referencia gris | Si |
| Que fuerzas tiene | Diagramas Axial/Corte/Momento (0-3) + tabla I/centro/J/max por combo | Si |
| Cuanta capacidad tiene | Panel P-M (envolvente + punto de demanda activo + `C = Mdem/Mcap`) | Si |

Observaciones:
- La tabla de demandas muestra solo el combo activo, evitando mezclar valores; el punto del P-M usa la misma demanda.
- Nuevas preguntas habilitadas: localizacion rapida (piso/nivel) y trazabilidad OpenSees tag -> JSON -> Unity -> combo -> seccion/capacidad.
- Limitacion UX: requiere click y/o teclado; sin soporte tactil dedicado aun (seccion 6).

---

## 6. Preparacion movil

### Telefono compatible (objetivo de testeo)

| Parametro | Valor |
|---|---|
| Equipo de referencia | Samsung Galaxy A54 5G (o equivalente medio) |
| Pantalla | 6.4" Full-HD+ (1080x2340) |
| GPU | Mali-G68 MP4 |
| RAM | 8 GB |
| SO (objetivo) | Android 13+ |
| Escena objetivo | Modelo completo (759 nodos / 817 barras) real-time simples |

Requisito de render: GPU con GLES 3.0 / Vulkan; 1080p es suficiente para el modelo.

### Estrategia de build

1. **Se intentara una build para iPhone (iOS):** requiere un Mac con Xcode y una cuenta de
   desarrollador de Apple (certificado de firma + provisioning profile) para generar el `.ipa`.
   Unity en Windows no puede compilar iOS, por lo que si no se dispone de ese Mac, se descarta.
2. **Peor de los casos (fallback):** se buscara un dispositivo Android y se aplicara la build
   permitida por Unity en Windows (APK/AAB, IL2CPP + ARM64), que es el flujo documentado a continuacion.

### Build movil inicial

Estado: **pendiente de ejecucion** (requiere modulo Android de Unity y Android SDK/device para el fallback).
Pasos documentados para reproducirla (flujo Android en Windows):

```text
1. Unity Hub -> proyecto P1L4\unity_visualizador -> File / Build Settings
2. Platform = Android -> Switch Platform (Instalar "Android Build Support" si falta)
3. Player Settings:
   - Scripting Backend = IL2CPP
   - Target Architectures = ARM64
   - Minimum API Level = Android 7.0 (API 24) o superior
   - Graphics APIs: Vulkan + GLES3
4. Texture Quality / Quality level bajo para el build movil
5. Build -> "estructura_p1l4_android.apk" (o App Bundle .aab para distribuir)
6. Instalar via adb:  adb install estructura_p1l4_android.apk
```

Nota: antes de la build hay que adaptar la entrada (OrbitCamera usa mouse/teclado).
Para mobile se requiere un esquema tactil (1 dedo orbita, 2 dedos zoom/pan) o el toggle
del punto de mira; esto queda como trabajo pendiente junto al primer APK de prueba.

---

## 7. IA

### Funcionalidad compleja implementada por agente

**Refinamiento de la curva P-M del muro con punto de traccion pura y conteo dinamico en el exportador Unity.**

- Que hace: enriquece la envolvente de capacidad del muro `W_DPRIME` con el punto de traccion
  pura `P_t = -fy*As_tot` (P = -3610.067 kN, M = 0), reordena los 24 puntos por P creciente,
  y hace que el exportador Unity `exportar_resultados_unity.py` emita la curva y su interpretacion
  con conteo dinamico de puntos (`{len(wall_points)}`), evitando numeros hardcodeados.
- Archivos tocados: `P1L3/carga_viva_sismo.py`, `P1L4/exportar_resultados_unity.py`,
  `P1L3/resultados/part_e_wall.json`, `P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json`.

Verificacion:

| Prueba | Resultado |
|---|---|
| `python -m py_compile` en ambos scripts | OK (sin errores de sintaxis) |
| `len(curva W_DPRIME.points)` | 24 |
| Primer punto del arreglo | `{"label": "P/Pn0=-0.07", "P_kN": -3610.066950093103, "M_kN_m": 0.0}` |
| Superposicion de combos C1/C2/C3 | Error maximo < `1e-6` sobre las 817 barras (`P1L4/tests/verificar_modelo.py`, 36 971 comprobaciones PASS) |
| Compilacion C# | Commit `c89ab08` compilado en Unity (batchmode); pendiente re-compilar tras `7fdc559` |

Ademas se restauraron seleccion de losas (`InfoSelectable`, segun commit `8ad11d2`) y la deformada
con referencia gris + linea verde, tambien implementadas/verificadas por agente.



---

## 8. Correcciones de modelo (semana 5, posterior)

Resumen; detalle en `reports/correccion_modelo_semana5.md`:

- Vigas partidas en uniones T/X (99 barras, 25 nodos nuevos en cruces): se eliminan las 25 vigas sueltas con empotramiento automatico y los voladizos.
- Edificio 2 unido al portico del edificio 1 en x = -10 (particion de barras de E1 + 4 enlaces rigidos por el desfase de niveles).
- D y Q como carga uniforme por viga: momentos parabolicos reales en los vanos.
- Sismo repartido por masa nodal (antes todo el piso en un nodo; edificio 2 sin sismo).
- Exportacion `localForce` con contrato explicito, desplazamientos de los 7 casos, equilibrio por caso en el JSON.
- Muros estructurales en el analisis (columna ancha + brazos rigidos, base empotrada) y diafragma rigido por nivel: los muros toman 23 % de G, 52 % del corte EX y 68 % del corte EY. Peso propio de columnas y muros agregado.
- Columna duplicada del edificio 2 en (-10,-7.25) eliminada (sus vigas llegan a la columna del edificio 1).
- Peso propio de vigas (b*(h-0.15)*25) en G y masa sismica; torsion accidental NCh433 (0.10 b_k Z_k/H) en EX/EY.
- Curva P-M propia para cada seccion de muro (75/75 paneles con curva).
- Hallazgos de diseno: E1_255 (columna sobre viga) no cumple P-M con C1/C2; alas del muro en U del edificio 1 en traccion sobre la capacidad de la armadura supuesta.
- Niveles del edificio 2 alineados con los del edificio 1 (desfase <= 16 cm): un diafragma por piso, sin barras de union cortas (evita cortes artificiales).
- Razon P-M en Unity: demandas fuera de la curva se marcan "fuera de curva / no cumple" (antes podian mostrar C = 0).
