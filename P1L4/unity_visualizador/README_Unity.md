# P1L4 — Visualizador Unity Estructural (Enriquecido)

Visualizador Unity de la estructura 3D con resultados de análisis estático lineal OpenSeesPy, combinaciones de carga NCh433, diagramas de interacción P-M, deformada y panel de información completo por elemento.

## Funcionalidades principales

- **Estructura 3D** (modelo corregido, JSON v2.0): 759 nodos, 124 columnas, 453 tramos de viga (edificio 2 unido al portico del edificio 1 en x=-10, pisos alineados), 15 muros estructurales (75 paneles) analizados como columna ancha + brazos rigidos (barras `muro_eq`/`brazo_rigido`, no se dibujan: el muro se ve como panel), 40 apoyos empotrados, diafragma rigido por nivel.
- **Selector de combinaciones de carga**: C1 = G+0.5Q+0.3EX+0.2EY, C2 = G+0.5Q+0.3EX-0.2EY, C3 = G+0.5Q-0.3EX+0.2EY (NCh433).
- **Diagrams OpenSees** (tecla 0–3): Axial, Corte, Momento — con fuerzas internas reales del combo activo.
- **Tabla compacta de valores**: al seleccionar un elemento y activar axial/corte/momento, muestra I, centro, J y maximo absoluto del diagrama.
- **Deformada** (tecla 5): desplazamientos del combo activo escalados al 6% de la altura de cada edificio.
- **Panel de información por elemento** (click izquierdo): sección, material, restricciones, ejes locales, fuerzas interpoladas, trazabilidad OpenSees→Unity→resultados.
- **Diagrama P-M interactivo** (click izquierdo en columna/muro): curva de capacidad HA con punto de demanda (N, M) del elemento en el combo activo.

## Arquitectura

```
P1L4/
├── exportar_resultados_unity.py   ← Generador del JSON enriquecido (requiere venv con openseespy)
├── unity_visualizador/
│   ├── Assets/
│   │   ├── Resources/
│   │   │   ├── estructura_completo_unity.json   ← JSON base (P1L2)
│   │   │   └── estructura_p1l4_unity.json       ← JSON enriquecido (generado por el exportador)
│   │   └── Scripts/
│   │       ├── StructureData.cs       ← Modelo de datos C# (deserializa el JSON enriquecido)
│   │       ├── StructureViewer.cs     ← Constructor de la escena + selector de combinaciones
│   │       ├── ElementSelectable.cs   ← Comportamiento de selección + info completa
│   │       ├── ElementPicker.cs       ← Raycast click + panel de info + trigger P-M
│   │       ├── DiagramController.cs   ← Diagramas de fuerza + deformada
│   │       ├── PMPanel.cs             ← Panel de diagrama P-M interactivo (nuevo)
│   │       ├── UnityData.cs           ← Datos estáticos compartidos entre scripts
│   │       ├── InfoSelectable.cs      ← Info para diafragmas/losas
│   │       ├── OrbitCamera.cs         ← Cámara orbital (click derecho rotar, rueda zoom)
│   │       └── Editor/
│   │           └── MCOCSetup.cs       ← Setup automático de la escena
│   ├── Packages/                      ← Unity Package Manager
│   ├── ProjectSettings/               ← Configuración del proyecto Unity
│   └── README_Unity.md                ← Este archivo
```

## 1. Generar datos (Python)

Requiere el venv del proyecto con `openseespy` instalado:

```bat
.venv\Scripts\python.exe -X utf8 P1L4\exportar_resultados_unity.py
```

Opciones:
- `--q-kg-m2 500` — carga viva Q en kg/m² (default 500 → 4.903 kN/m²)
- `--sc 0.20` — coeficiente sismico (default 0.20)

El exportador:
1. Carga `estructura_completo_unity.json` (P1L2).
2. Corre análisis estático lineal con OpenSeesPy para 3 combinaciones (C1, C2, C3).
3. Corrige la conectividad (`corregir_conectividad` en P1L3): parte las vigas en uniones T/X y une el edificio 2 al portico del edificio 1.
4. Corre G, Q, EX, EY, C1, C2, C3 con D y Q como carga uniforme por viga (`eleLoad -beamUniform`) y sismo repartido F_i = C·W_i.
5. Extrae desplazamientos (496 nodos con barras x 7 casos) y acciones LOCALES de extremo `eleResponse(id, "localForce")` (817 barras x 7 casos). Declara `p1l4.elementForceCoordinates = "local"`.
6. Genera curvas P-M: COL70/70_FIBER (5 puntos) y W_DPRIME_OPENING_TO_3 (24 puntos envolvente).
7. Genera demandas P-M por muro y por combinacion activa.
8. Guarda `estructura_p1l4_unity.json` (~4.4 MB) en `P1L4/unity_visualizador/Assets/Resources/`.

## 2. Abrir en Unity

1. Copiar la carpeta `P1L4/unity_visualizador` completa (o usar el `Assets/` como tu Unity project si ya tienes uno).
2. Abrir Unity Hub → Open → seleccionar `P1L4/unity_visualizador`.
3. Dejar que Unity regenere `Library/` (excluido del gitignore).
4. Crear una escena vacía.
5. Ir al menú **MCOC → Crear Visualizador** (o confiar en la creación automática del editor script).

El `MCOCSetup.cs` (editor-only) crea automáticamente: StructureViewer, Camera + ElementPicker + OrbitCamera, Light, y guarda la escena.

## 3. Uso

### Combinaciones de carga

Usa el toolbar en la parte superior izquierda para seleccionar entre C1, C2, C3. El diagrama de fuerzas y la deformada se actualizan según la combinación seleccionada.

### Diagramas de fuerzas (teclas)

- `0` — Ocultar diagramas
- `1` — Axial N (rojo)
- `2` — Corte (naranja)
- `3` — Momento (magenta, dibujado del lado traccionado)
- `5` — Deformada (verde)

Vigas, columnas y enlaces muestran corte y momento. Cada barra se dibuja en el plano local de su componente dominante (Vz/My en z local, Vy/Mz en y local).

Las fuerzas provienen de `eleResponse(id, "localForce")` (12 acciones locales de extremo). Los esfuerzos de seccion se obtienen con `FrameForces.Evaluate` por equilibrio: la carga uniforme real del analisis ((f_i+f_j)/L) produce el momento parabolico del vano. No se agrega ninguna parabola artificial.
Al seleccionar una viga, columna o muro, aparece una tabla compacta con los valores del diagrama activo: extremos I/J, centro y maximo absoluto. Para momento/corte se muestran tambien componentes locales My/Mz o Vy/Vz segun corresponda.

### Panel de información (click)

Al hacer click en cualquier columna, viga o muro:
- ID Unity y OpenSees (elementTag)
- Nodos extremos, piso, edificio
- Sección y material (fc', fy, barras, As, rho)
- Restricciones en cada extremo (empotrado/pasador/etc.)
- Ejes locales X' Y' Z'
- Fuerzas/demanda: N, Vy, Vz, T, My, Mz segun corresponda
- Trazabilidad: OpenSees tag → Unity obj → combo activo → sección/capacidad

### Diagrama P-M interactivo

Al hacer click en una **columna** o **muro** (con curva disponible):
- Se abre un panel con la curva P-M de capacidad.
- Punto de demanda del combo activo, rotulado con C1/C2/C3 y expresion completa.
- Demandas estimadas por muro; al seleccionar otro muro cambia el punto de demanda.
- Para columnas, el punto de demanda (N, M) proviene de las fuerzas internas reales del combo activo.
- Se muestra información de material, barras y porcentaje de acero.

Columnas disponibles con P-M: todas las 129 columnas (COL70/70 → curva COL70/70_FIBER).
Muros: los 75 paneles muestran su demanda P/M del analisis (combo activo) sobre la curva P-M de su propia seccion (W_DPRIME_OPENING_TO_3 con su curva de fibra; el resto `W_t x L`, armadura supuesta 2 capas phi12@200).

### Controles de cámara

- Click izquierdo + arrastrar: rotar
- Click derecho + arrastrar: pan (desplazar)
- Rueda del mouse: zoom
- Botones toggles en panel superior: Columnas, Vigas, Muros, Apoyos, Diafragmas, Nodos, IDs, Ejes locales

## Notas técnicas

- Las fuerzas internas están en **coordenadas locales** del elemento: [N, Vy, Vz, T, My, Mz] × 2 extremos.
- El mapping de ejes globales → Unity es: X→x, Y→z, Z→y (el eje vertical del edificio es Z global, Y de Unity).
- Los muros equivalentes son visualizaciones de las paredes de la estructura real (grosor 0.2–0.25 m). La curva P-M es representativa.
- El edificio completo integra edificio 1 + edificio 2, incluyendo los muros equivalentes del edificio 2.


## Cambios semana 5 (correccion del modelo)

- Ver `reports/correccion_modelo_semana5.md` para el detalle y la verificacion.
- Carga movil: boton **Caminar automatico** (sobre una viga seleccionada recorre la viga de I a J y vuelve; si no, recorre la losa). Los botones `-`/`+` ya no saltan a 0 %/100 %. La carga de la persona sobre la losa se reparte a los apoyos que la rodean (viga o muro mas cercano en +X, -X, +Z, -Z; reparto por franjas cruzadas, suave con la distancia) y la viga seleccionada recibe su fraccion aunque la persona no la pise; el panel muestra el reparto completo.
- El panel "Diagramas de ..." acepta vigas, columnas y enlaces.
