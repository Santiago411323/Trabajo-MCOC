# Semana 5 - Visualizador estructural interactivo Unity

**Proyecto:** modelo estructural UANDES, P1L4.
**Unidades:** kN, m, kN.m.
**Viewer:** `P1L4/unity_visualizador`.
**Datos:** `P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json`.
**HEAD del informe:** `7fdc559` (debe coincidir con `git log -1 --oneline`).

---

## 1. Funciones implementadas

Datos de referencia verificados en el JSON exportado: 553 nodos, 462 elementos
(129 columnas, 333 vigas, 75 muros equivalentes), 30 apoyos empotrados,
226 losas, 1659 registros de desplazamiento (553 x 3 combos) y 3234 registros de
fuerzas internas (462 x 7 casos: `G`, `Q`, `EX`, `EY`, `C1`, `C2`, `C3`).

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
- **Apoyos**: los 30 apoyos se dibujan como cubos empotrados (0.55x0.14x0.55, amarillo) con etiqueta `N{n}\nEmpotrado`; semantica `ux=uy=uz=rx=ry=rz=1`. Columnas de base inferior reciben apoyo inferido `fixed`; columnas intermedias no.
- **Ejes**: `CreateGlobalAxes` dibuja ejes X (rojo), Y (verde), Z (azul) globales; `ElementSelectable` reporta ejes locales X', Y', Z' por extremo.
- **Cargas**: carga viva superficial `Q_kN_m2` por losa (p.ej. consulta `query_slab` devuelve `q_Q`, `Q_total`), sismo pseudoestatico `EX`/`EY` con `seismic_coefficient`, y cargas puntuales `pointLoads` dibujadas como flechas rojas (solo si |fz| > 0.001).
- **Areas tributarias**: `tributaryList` (5 pisos) resume por piso `area_total`, `carga_total`, cantidad de vigas; ejemplo verificado: `CIELO_1` -> 735.52 m2, 8186.78 kN, 44 vigas; `CIELO_1S` -> 139.33 m2, 1550.84 kN, 7 vigas.
- **Deformada**: con combo activo, amplifica desplazamientos a 6% de la altura de cada edificio (escala aproximada edificio_1 ~67x, edificio_2 ~7.5x); linea gris de referencia (ancho 0.04) + linea verde (0.16) desplazada.
- **Diagramas**: teclas 0-3 (Axial, Corte, Momento) + tabla compacta de valores del elemento seleccionado (extremos I/J, centro, maximo absoluto).
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
3. **Resultados**: `wallRegistry.demands` guarda por muro y por combo `P_kN`/`M_kN_m` (75/75 muros).
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
| `C1` | Suma ponderada vs `elementForces` de C1 (462 elementos, 12 componentes) | Error maximo absoluto = `0.0` kN / kN.m |
| `C2` | Idem para C2 | Error maximo absoluto = `0.0` kN / kN.m |
| `C3` | Idem para C3 | Error maximo absoluto = `0.0` kN / kN.m |

Complementados con desplazamientos: max|uz| = 0.09153 m en N358; max|ux| = 0.02788 m en N361;
max|uy| = 0.02283 m en N353 (el maximo vertical es identico en los 3 combos porque domina la gravedad).
Consistente con la semana 3 (errores de corridas explicitas del orden de 1e-8 a 1e-16).

En el viewer, cambiar `C1/C2/C3` en la barra superior debe actualizar simultaneamente la deformada,
los diagramas, la tabla de valores y el punto rojo del P-M a la misma demanda.

---

## 4. Sidequest: carga movil

Implementada en `MobileLoadController.cs` (viga seleccionada).

- **Regla fisica**: carga puntual concentrada `P` a distancia `x/L` sobre viga simplemente apoyada equivalente.
- **Panel** (`PanelRect = (360, 92, 330, 204)`): toggle `Activar carga movil`; slider `P [kN]` (0..300,
  paso +/-10); slider `Posicion x/L` (0..1, paso +/-0.05); toggles `Axial`, `Corte`, `Momento`;
  muestra `Viga: <tag>` de la seleccion.
- **Reparto** (reacciones de viga simplemente apoyada):

```text
ri = P * (1 - x/L)
rj = P * (x/L)
mMax = ri * x/L * L = P * x * (1 - x/L)     (momento maximo bajo la carga)
```

- **Conservacion**: el panel muestra `Reparto: I=.. | J=..` y `Conservacion: I+J=.. | error=..`,
  con `error = |P - ri - rj|` (0.000 por construccion) y `P` limitado a [0, 300].
- **Respuesta visual**: esfera roja en la posicion de la carga, flecha cilindrica apuntando hacia abajo,
  y lineas locales segun toggles: axial (rojo, sobre la viga), corte (naranja `ri`/`rj` a cada lado de la
  carga), momento (magenta, triangular con pico `mMax`); escala visual `diagramScale = 0.018`.

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
| Escena objetivo | Modelo completo (553 nodos / 462 elementos) real-time simples |

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
| Superposicion de combos C1/C2/C3 | Error maximo `0.0` sobre los 462 elementos (script de verificacion) |
| Compilacion C# | Commit `c89ab08` compilado en Unity (batchmode); pendiente re-compilar tras `7fdc559` |

Ademas se restauraron seleccion de losas (`InfoSelectable`, segun commit `8ad11d2`) y la deformada
con referencia gris + linea verde, tambien implementadas/verificadas por agente.

