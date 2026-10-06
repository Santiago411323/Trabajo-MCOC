# Semana 7 — Guía de defensa individual Unity + OpenSees + AR

Actualizada el 6 de octubre de 2026 a partir del código y los datos del proyecto.
Uso: estudiar, ensayar la demostración y localizar evidencia durante preguntas.
Los ejemplos corresponden al modelo actual; una edición o regeneración puede cambiarlos.
Esta guía no acredita una demostración en vivo ni una prueba física en teléfono.

## 1. Explicación del proyecto en un minuto

> Partimos de los planos y de la geometría digitalizada para definir nodos,
> elementos, secciones, apoyos y superficies de carga. Con esos datos construimos
> en OpenSees un modelo espacial elástico de seis grados de libertad por nodo.
> Calculamos los casos G, Q, EX y EY y sus combinaciones. Exportamos la geometría,
> identificadores, desplazamientos, acciones locales y curvas de capacidad a JSON.
> Unity permite consultar y comparar esos resultados, mostrar diagramas y deformada,
> y editar parámetros para ejecutar un nuevo análisis. En AR seleccionamos el mismo
> elemento por ID y colocamos sus resultados respecto de dos puntos elegidos por el
> usuario. El identificador conserva la trazabilidad entre cálculo y representación.

**Esquema que todos deben poder dibujar:**

```text
Planos / geometría digitalizada
        ↓
Nodos + conectividad + secciones + apoyos + cargas
        ↓
OpenSees: restricciones, equilibrio y respuesta
        ↓
Exportador: JSON de geometría y resultados por caso
        ↓
Unity: inspección, superposición, diagramas y capacidad
        ↓
AR: selección por ID, colocación I–J y consulta del mismo resultado
```

**Tres cálculos distintos:** respuesta global del edificio, capacidad de la sección
y representación gráfica. Una curva de capacidad no convierte el edificio elástico
en un análisis global de daño no lineal.

## 2. Dónde está cada parte

Rutas relativas a la raíz del repositorio `Trabajo-MCOC`:

| Parte | Archivo o carpeta | Qué explicar |
|---|---|---|
| Entrada del exportador general | `P1L2/unity_visualizador/Assets/Resources/estructura_completo_unity.json` | Datos digitalizados; no sustituye los planos originales |
| Constructor y análisis estático | `P1L3/carga_viva_sismo.py` | `build_model`, cargas, solución y extracción |
| Exportación y parámetros editados | `P1L4/exportar_resultados_unity.py`, `P1L4/model_edits.json` | Casos, identificadores, resultados y secciones |
| Datos de escritorio | `P1L4/desktop_model/estructura_p1l4_desktop.json` | Variante actual del modelo de escritorio |
| Recursos de Unity/AR | `P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json` | Datos incluidos en el proyecto/aplicación |
| Selección de fuente | `DesktopModelFile.cs`, `StructureViewer.cs` | Fuente de escritorio, recurso o JSON recargado después de editar |
| Lectura y superposición | `UnityData.cs` | Respuesta por caso y factores de sliders |
| Diagramas | `FrameForces.cs`, `DiagramController.cs` | Equilibrio de sección y dibujo |
| Inspector y trazabilidad | `ElementPicker.cs`, `StructuralAuditReport.cs` | ID, `elementTag`, unidades, fuente y comprobaciones |
| Diafragmas | `exportar_diafragmas.py`, `StructuralDiaphragmViewer.cs` | Membresía real de las restricciones |
| Edición de parámetros | `StructuralModelEditor.cs` | Guardado, estado desactualizado, ejecución y recarga |
| AR | `Assets/Scenes/StructuralARScene.unity`, scripts `StructuralAR…` | Identificación manual, pose, anchor y consulta |

Los archivos C# de la tabla están en `P1L4/unity_visualizador/Assets/Scripts/`.
**No asumir que escritorio y teléfono leen siempre el mismo archivo.** Antes de comparar,
consultar la fuente activa y verificar modelo, edición, elemento, caso y posición.
Después del reanálisis el editor puede usar `RuntimeJsonOverride`; revisar nuevamente
la procedencia y la geometría recargada. No confundir el exportador general con los
scripts de revisión de la variante de escritorio.

## 3. Qué representa el modelo estructural

### Geometría e identificadores

- Un nodo tiene coordenadas estructurales `(X,Y,Z)` en metros; Z es vertical.
- Un elemento conecta I con J y tiene sección, orientación, tipo e identificador.
- `id` es la clave numérica del elemento en los datos; `elementTag` conserva su
  identificación trazable, por ejemplo `E1_72`. El nombre del GameObject es otra cosa.
- La decoración —ventanas, árboles, cafetería, personas, terreno— no participa
  automáticamente en el análisis. Los muros estructurales se distinguen de tabiques visuales.

Ejemplos comprobados en el JSON de escritorio al preparar esta guía:

| Elemento | ID | Sección | Largo entre nodos |
|---|---:|---|---:|
| Viga `E1_72` | 72 | `V60/80` | **7,51 m** |
| Columna `E1_229` | 229 | `COL70/70` | **4,00 m** |

No decir que `E1_72` mide 8 m por recordar una conversación anterior: consultar
las coordenadas y el largo del modelo que se está mostrando.

### Hipótesis del cálculo global

- Modelo 3D de barras `elasticBeamColumn`, con transformación geométrica `Linear`.
- Seis GDL por nodo: Ux, Uy, Uz, Rx, Ry, Rz.
- Cálculo estático: equilibrio lineal `K·U = F` con apoyos y restricciones multipunto.
- El constructor usa `E_CONCRETE = 25.000.000 kN/m²` (25.000 MPa) y G derivado
  de E y del coeficiente de Poisson. No asigna automáticamente a cada barra un E
  diferente al editar f'c en el panel de capacidad.
- No se modelan aquí plastificación global, redistribución por daño, rotura ni colapso.
  La transformación `Linear` tampoco incorpora por sí sola efectos P–Δ.
- Las losas de carga/visuales no son elementos shell. Su reparto tributario y la
  restricción de diafragma son dos decisiones distintas.
- Los muros analíticos se idealizan como columnas anchas equivalentes con brazos
  rígidos; no se afirma que su dibujo reproduzca una solución de placa/shell.

### Apoyos y diafragmas

Un empotramiento restringe los seis GDL. Verificar la restricción del nodo en el
modelo; un símbolo dibujado en Unity no demuestra por sí solo que exista `ops.fix`.
El constructor también puede anclar componentes desconectados. El JSON actual
declara cero anclajes automáticos; revisar otra vez después de cambios de conectividad.

El diafragma actual llama `rigidDiaphragm(3, maestro, nodos…)`:

- Plano XY, normal Z; vincula Ux, Uy y Rz.
- El maestro auxiliar tiene Uz, Rx y Ry restringidos, con Ux, Uy y Rz libres.
- El vínculo no obliga a iguales Uz, Rx ni Ry en los nodos del piso.
- Una rotación del piso produce traslaciones distintas en nodos separados.

Para pequeñas rotaciones, con distancias medidas respecto del maestro:

```text
ux(n) = ux(M) − Rz(M)·[y(n)−y(M)]
uy(n) = uy(M) + Rz(M)·[x(n)−x(M)]
rz(n) = rz(M)
```

**Supuesto que hay que defender:** el constructor agrupa por cota y puede vincular
nodos de ambos edificios. No son necesariamente diafragmas independientes por edificio.
Si físicamente existe una junta de separación, hace falta revisar esta idealización
con los planos y cambiar el análisis antes de afirmar independencia estructural.

El modo de Unity muestra cinco cotas (0, 4, 8, 12 y 16 m) y 450 vínculos.
Usa `p1l4.analysisModel.diafragmas`, no la lista histórica `diaphragmList`.
El movimiento del maestro mostrado se recupera de los nodos vinculados; el inspector
lo identifica así y muestra el residuo de compatibilidad.

## 4. Cargas, unidades y superposición

| Caso | Significado en este proyecto |
|---|---|
| G | Cargas permanentes: aportes de losas y peso propio según la implementación |
| Q | Sobrecarga de uso repartida a los elementos receptores |
| EX | Acción sísmica pseudoestática en X, con la torsión accidental implementada |
| EY | Acción sísmica pseudoestática en Y, con la torsión accidental implementada |

Unidades principales: geometría m, fuerza kN, carga superficial kN/m², carga lineal
kN/m, momento kN·m y rotación rad. El panel puede convertir desplazamientos a mm.
Para convertir kg/m² de carga a kN/m²: multiplicar por `9,80665/1000`.

El sismo estático implementado distribuye `F_i = C·W_i` usando peso sísmico basado
en G y 0,5Q. Es una hipótesis de análisis del proyecto; no presentarlo como verificación
completa de todas las exigencias de NCh433.

Combinaciones exportadas de referencia:

```text
C1 = G + 0,5Q + 0,3EX + 0,2EY
C2 = G + 0,5Q + 0,3EX − 0,2EY
C3 = G + 0,5Q − 0,3EX + 0,2EY
```

Los sliders forman `R = λG·RG + λQ·RQ + λEX·REX + λEY·REY`.
Sirve porque el modelo global es lineal y comparte geometría, rigidez y restricciones
entre esos casos. No aplicar esta superposición sin justificación a un modelo no lineal.
Identificar siempre los factores visibles: una C1 modificada ya no es necesariamente
la C1 original exportada.

### Área tributaria y peso propio: ejemplo de explicación manual

1. Consultar el aporte de **cada** losa a la viga, en kN, y sumarlos.
2. Dividir el aporte total por el largo para obtener su carga uniforme equivalente.
3. Añadir el peso propio de la viga según la convención implementada.
4. Aplicar los factores del caso observado; no mezclar G, Q ni cargas mayoradas.

En el código, el peso propio de una viga usa:

```text
wPP = 25 · b · max(h − 0,15; 0)    [kN/m, dimensiones en m]
```

La deducción de 0,15 m evita contar de nuevo el volumen de losa adoptado en esa
convención. Para una sección 0,60 × 0,80 m, `wPP = 9,75 kN/m`.
No añadir otro peso completo `25·b·h` encima del peso ya exportado.

Las cargas de losas provienen del reparto tributario. La conversión a una carga
uniforme de viga es una idealización del modelo; no equivale a resolver una placa.

## 5. Cómo se obtienen los diagramas y la deformada

OpenSees entrega **acciones locales de extremo** con `eleResponse(id, 'localForce')`:
12 componentes, seis en I y seis en J. No son directamente todos los valores del
diagrama interior. `FrameForces.Evaluate` reconstruye la sección por equilibrio.

En la convención del viewer, N es positivo en tracción. El diagrama P–M emplea P
positivo en compresión, por lo que hay que convertir el signo. En las caras I/J
tampoco se copian sin más ambos vectores de acciones con el mismo signo.

Ejemplo literal del cálculo de My, con `x = t·L` y acciones locales f:

```text
qz = [f[2] + f[8]] / L
My(x) = f[4] + f[2]·x − 0,5·qz·x²
```

El signo de qz corresponde al eje local; no sustituirlo por una carga positiva
sin revisar la convención. En general, N/T se interpolan según extremos, los cortes
son lineales y los momentos contienen términos cuadráticos por la carga uniforme.

**¿Por qué los momentos de los extremos son diferentes?** La viga pertenece a un
pórtico: sus giros y acciones dependen de las columnas, muros, cargas y vanos vecinos.
`qL²/8` corresponde al máximo de una viga simplemente apoyada con carga uniforme;
no determina por sí solo los momentos de esta viga dentro del edificio.

Los gráficos se muestrean; el máximo mostrado debe describirse según ese muestreo.
Al recorrer la curva, consultar esfuerzo, x desde I y porcentaje de longitud.

La deformada dibuja `Xvisual = Xoriginal + escalaVisual·U`. La escala no cambia las
cargas, la rigidez, los esfuerzos ni los milímetros calculados. Unity convierte
posiciones de `(X,Y,Z)` a `(X,Z,Y)`; las rotaciones requieren además tratar correctamente
el cambio de orientación, no solo intercambiar dos números.

## 6. Capacidad P–M, fibras, armadura y tipo de falla

El punto de demanda se obtiene del mismo elemento, caso y posición que se consulta.
La curva de capacidad procede de la sección y su armadura; no de la apariencia del
GameObject. Para muros comprobar la sección P–M asignada y la demanda propia del muro.

**Antes de decir “cumple”, leer el rótulo:** la envolvente nominal existente y la
capacidad reducida φRn del laboratorio LRFD son criterios diferentes. No compararlos
como si ambos fueran la misma curva. Un chequeo uniaxial P–M no acredita interacción
biaxial, corte, torsión, esbeltez, anclajes ni todos los detalles sísmicos.

La exportación contiene comprobaciones de sección independientes: parte de P–M
usa estados característicos y parte usa compatibilidad/integración de fibras.
M–Φ integra esfuerzos de hormigón y acero para una distribución de deformaciones.
La respuesta de sección simplificada no es automáticamente la respuesta de un
elemento no lineal integrado en el edificio. Que la metadata mencione
`Concrete01/Steel01` no basta para afirmar que el análisis global use esas leyes.

Armaduras de referencia (consultar propiedades actuales si el elemento fue editado):

- Vigas: 12 Ø25, 4 superiores + 4 inferiores + 2 por cada lado; estribos editables.
- Columnas: 18 Ø25, 5 superiores + 5 inferiores + 4 por cada lado.
- Muros: consultar sección y nota de armadura; hay distribuciones asumidas en la exportación.

```text
As = n·π·Ø²/4
Cuantía total = As/(b·h)
Cuantía de referencia de flexión = As,tracción/(b·d)
```

No usar todo el acero como si estuviera en una única fila de tracción. En una
sección 0,60 × 0,80 m con 12 Ø25, As total ≈ 5.890,49 mm² y cuantía total ≈ 1,227%.
Estos números no son, por sí solos, la verificación del mínimo/máximo de flexión.

El panel explicativo usa referencias de ACI 318-08, con sus límites de aplicación:
As,min de viga, 1%–8% de Ag para columnas y una referencia simplemente armada de
As,max con `cmax=0,375d`, `amax=β1·cmax`, `As,max=0,85f'c·b·amax/fy`.
Esta última referencia no sustituye el equilibrio completo de una sección con
acero superior, inferior y lateral. Ver el cálculo detallado y la deformación εt.

**Explicación de los tipos de respuesta al límite:**

- Acero traccionado fluye antes del límite de compresión del hormigón: mayor
  capacidad de deformación, dentro de las hipótesis de sección.
- Acero traccionado aún elástico cuando el hormigón alcanza su límite: respuesta
  controlada por compresión, menos dúctil.
- Balance: fluencia del acero y límite del hormigón simultáneos; no garantiza
  que sea la cuantía óptima ni una condición especialmente dúctil.

No decir que en una sección subarmada el hormigón “falla por tracción al llegar
a εcu”: εcu es el límite de compresión. La fisuración por tracción es otro fenómeno.

La animación de sección y las grietas didácticas explican esos estados. No constituyen
una predicción de ubicación/ancho real de fisuras ni de destrucción del edificio.

## 7. Modificar dos parámetros: demostración completa

**Sí está implementado el editor; lo evaluable es demostrar la conexión con el cálculo.**

1. Elegir una viga y registrar ID, `elementTag`, fuente, caso, posición, sección,
   armadura, esfuerzo y capacidad. Exportar una ficha de trazabilidad.
2. Abrir **EDITAR MODELO** y cambiar dos parámetros: por ejemplo ancho y diámetro.
   Usar valores que quepan geométricamente y conservar el tag.
3. **GUARDAR + PREVIEW**: explicar que actualiza la vista, guarda `model_edits.json`
   y marca los resultados como desactualizados.
4. **REANALIZAR OPENSEES**: esperar finalización y recarga; comprobar que los cambios
   se mantengan y que la fuente/modelo sean los que se pretende evaluar.
5. Comparar en el mismo caso y posición la sección, armadura, respuesta y capacidad.
   Volver a exportar la ficha. Mostrar evidencia, no prometer que todo debe aumentar.
6. Restaurar los parámetros originales y reanalizar si se necesita recuperar el modelo
   de entrega. Hacer el ensayo previamente en una copia preparada para la demo.

El ancho/alto modifica A, inercias y peso propio según la ruta de análisis. La armadura
modifica la capacidad y respuesta de sección. En el modelo global elástico actual,
editar barras no crea automáticamente plastificación ni una nueva rigidez de sección
transformada. Tampoco asegurar que cambiar f'c actualice el E global constante.

Guardar no equivale a reanalizar. Si falla Python o la fuente recargada no corresponde
a la variante elegida, declarar la respuesta pendiente y corregir el flujo antes de
mostrar un antes/después como válido.

## 8. AR: qué hacemos y qué debemos demostrar

Escena independiente: `StructuralARScene`. Seleccionar `E1_72` por ID/tag, fijar I y J,
confirmar cara y anclar. Consultar C1/C2/C3 y diagramas del mismo elemento.

- La cámara no identifica automáticamente qué viga física es: el usuario confirma su ID.
- La pose coloca la representación; el anchor conserva su referencia espacial mientras
  el seguimiento lo permite. Caminar alrededor comprueba estabilidad visual, no precisión certificada.
- Si el largo marcado no coincide con el largo analítico, el dibujo se ajusta al tramo
  I–J elegido; los esfuerzos originales no se recalculan por esa colocación.
- Girar la cara modifica la orientación visual; explicar los ejes para evitar interpretar
  My/Mz como una componente distinta.
- Distinguir validación en XR Simulation de prueba física Android/iPhone. Esta guía
  no acredita una prueba física; completar dispositivo, fecha y resultado cuando exista.

Ver [guía AR de uso y alcance](unity_visualizador/Assets/StructuralAR/README_AR.md).

## 9. Recorrido del núcleo obligatorio (antes de Honors)

Ensayar el tiempo disponible y completar la tabla con los tres integrantes.

| Requisito | Acción de demostración | Explicación breve |
|---|---|---|
| 1. Geometría | Vista general; identificar edificios y niveles | Datos estructurales separados del entorno visual |
| 2. Apoyos | Activar Apoyos e inspeccionar un nodo de base | Restricciones reales y seis GDL |
| 3. Ejes | Mostrar globales/locales de E1_72 | X' de I a J; Z global vertical; cambio a Unity |
| 4. Diafragmas | Activar modo; elegir cota e inspeccionar | Maestro, vinculados, plano, supuesto entre edificios |
| 5. Áreas tributarias | Seleccionar losa y receptores | Aporte a viga y conservación de carga |
| 6. G/Q/EX/EY | Aislar casos con sliders (uno en 1, los demás en 0) | Permanente, uso y pseudoestático por dirección |
| 7. Deformada | Activar y variar escala | Desplazamiento real vs amplificación gráfica |
| 8. Diagramas | Seleccionar E1_72; N/V/My/Mz | Valores locales por equilibrio; lectura a lo largo de I–J |
| 9. Superposición | Mostrar C1 y cambiar un factor | Combinación lineal de respuestas; etiqueta actual |
| 10. P–M columna | Seleccionar E1_229, activar interacción | Capacidad de su sección y convención P |
| 11. P–M muro | Elegir previamente un muro con curva y demanda | Columna ancha, armadura asumida y flexión en el plano |
| 12. Demanda | Cambiar combinación y seguir el punto | Mismo elemento/caso; nominal o reducida según rótulo |
| 13. Dos parámetros | Ejecutar el ciclo del apartado 7 | Guardado, análisis y evidencia actualizada |
| 14. AR básica | Escena AR; seleccionar, colocar y consultar | ID conservado, anchor y resultados trazables |

Seleccionar y anotar **antes** del examen el ID/tag exacto del muro de demo, la losa
receptora y el dispositivo AR. No depender de encontrarlos al azar durante la exposición.

## 10. Preguntas probables y respuestas que hay que sostener

| Pregunta | Respuesta breve + evidencia |
|---|---|
| ¿De dónde sale este número? | Fuente activa → caso/factores → ID/tag → acciones/desplazamiento → posición; abrir Trazabilidad |
| ¿Unity calcula la estructura? | El edificio lo resuelve OpenSees; Unity superpone, reconstruye secciones y visualiza; tiene chequeos independientes rotulados |
| ¿Es un análisis no lineal? | El edificio actual es elástico lineal; capacidad de sección y evaluación sísmica no implican daño global no lineal |
| ¿Por qué puedo sumar casos? | Misma K y restricciones en régimen lineal; contraste con corrida explícita |
| ¿Dónde están las losas shell? | No se modelan shells aquí; superficies de carga y restricción de diafragma |
| ¿Todos los nodos del piso se desplazan igual? | No: la rotación agrega desplazamientos que dependen de la posición; mostrar ecuaciones/residuo |
| ¿Los dos edificios son independientes? | El diafragma actual puede vincularlos por cota; contrastar supuesto con juntas reales |
| ¿Ese apoyo existe en el cálculo? | Comprobar nodo/restricciones del JSON y constructor, no solo el símbolo |
| ¿De dónde sale q? | Sumar aportes tributarios, dividir por L, añadir PP y aplicar factores del caso |
| ¿Por qué no da qL²/8? | La viga está en un pórtico, no simplemente apoyada; acciones y giros dependen del conjunto |
| ¿Por qué la deformación se ve tan grande? | Escala gráfica; enseñar milímetros reales y escala utilizada |
| ¿La columna “aguanta todo”? | Solo se afirma el chequeo rotulado, para ese caso y eje; listar verificaciones no incluidas |
| ¿P–M incluye φ? | Leer curva nominal o φRn; no mezclar sus D/C |
| ¿Más acero cambia los esfuerzos? | Cambia capacidad; en barras elásticas no altera automáticamente la rigidez por armado |
| ¿Qué pasa si guardo y no reanalizo? | Vista previa y resultados desactualizados; el editor lo indica |
| ¿Esas fisuras son reales calculadas? | Representación didáctica/estado calculado según modo; no predicción de fisuras individuales |
| ¿La cámara reconoce la viga? | Identificación por usuario; calibración I–J, no reconocimiento semántico automático |
| ¿Mover I/J cambia OpenSees? | Cambia ubicación/escala visual, conserva ID y respuesta del modelo original |
| ¿Cómo prueban que los resultados son correctos? | Equilibrio, superposición explícita, identidad de datos, signos/ejes y pruebas reproducibles |
| ¿Un warning significa cero esfuerzo? | No; revisar respuesta ausente, elemento/caso y procedencia; no inventar un cero |

Si no sabemos: indicar el supuesto conocido, localizar el archivo y describir cómo
lo comprobaríamos. No improvisar una certificación ni una ecuación que no usa el código.

## 11. Honors: explicar solamente después del núcleo

El archivo oficial `03_HONORS_TRACK.md` no se encontró en esta revisión. Confirmar
sus criterios con el material del curso antes de atribuir Honors a una extensión.

- **Carga móvil:** transferencia aproximada por superficies tributarias a nodos
  receptores, respuestas unitarias OpenSees y superposición. No es una placa no lineal
  ni una persona que genera cargas por su apariencia. Ver [carga móvil](CARGA_MOVIL_LOSAS.md).
- **Structural X-Ray:** región receptora y red con mayor incremento de una métrica,
  calculada con la persona. Pulsos y cámara explican conexiones reales, no velocidad
  de propagación ni trayectoria exacta de fuerzas. Se comparan las reacciones con
  la carga nodal aplicada. Ver [X-Ray](STRUCTURAL_XRAY.md).
- **Radar de demanda:** ranking de la métrica seleccionada; nominal P–My y esfuerzos
  no son rankings equivalentes. “Más solicitado” no siempre significa “menos seguro”.
- **Duelo de diseños Antes ↔ Después:** dos respuestas exportadas sobre la misma
  topología y los mismos factores; diferencias reales en desplazamiento, esfuerzos,
  capacidad, acero y peso propio. La barra y el cambio visual de sección interpolan
  gráficamente, no generan un tercer análisis. Ver [comparación de diseños](COMPARACION_DISENOS.md).
- **Laboratorio de sección:** barras configuradas, curva y deformaciones de sección.
  La flexión pura ilustrada no es la deformada global ni un terremoto.
- **Sismo Transient:** `M·ü+C·u̇+K·u = −M·r·ag(t)`, con gravedad previa y respuesta
  relativa al terreno. Masas traslacionales de G+0,5Q y PP; Rayleigh 5% en modos 1 y 3;
  Newmark γ=0,5, β=0,25. Confirmar metadata de la corrida. Factores 0,25×–1,50× escalan
  el acelerograma; no son magnitudes Richter. D/C excedido indica criterio excedido,
  no fractura/colapso de un modelo que sigue siendo elástico. Ver [sismo](seismic/README.md).
- **LRFD:** cargas idealizadas y combinaciones de los apuntes, con respuestas de
  OpenSees y capacidad reducida P–My independiente. Nubes/lluvia/nieve son efectos
  visuales; las presiones y casos de carga son los que entran al cálculo. No afirmar
  cumplimiento normativo integral. Ver [laboratorio LRFD](LABORATORIO_LRFD.md).

## 12. Evidencia y preparación de la entrega

Pruebas relevantes desde la raíz (con Python/OpenSeesPy y Unity compatibles):

```powershell
python P1L4/tests/test_diafragmas.py
& P1L4/tests/verificar_armadura_seccion.ps1
& P1L4/tests/verificar_fuerzas_unity.ps1
python P1L4/seismic/validate_transient.py
```

No todas usan las mismas entradas: la validación sísmica requiere corridas generadas,
y la de LRFD requiere su escenario/resultados de validación. Revisar sus README/scripts.
Compilar y pasar pruebas numéricas no acredita por sí solo controles en vivo o AR física.

Al implementar los diafragmas se contrastaron los grupos con la llamada efectiva de
OpenSees, siete casos exportados y el maestro de corridas EX/EY directas. El residuo
máximo XY de los datos originales fue ≈ 3,47×10⁻¹⁸ m; los demás campos del JSON
permanecieron idénticos. El inspector usa float y puede mostrar un residuo algo mayor.

**Antes del examen:**

- [ ] Todos los integrantes ejecutaron personalmente los 14 pasos.
- [ ] Cada uno puede dibujar el flujo y explicar unidades, signos y ejes.
- [ ] Anotados viga, columna, muro, losa y sus identificadores de demostración.
- [ ] Verificada fuente activa y ausencia de resultados desactualizados.
- [ ] Ensayada edición/reanálisis y recuperación del modelo de entrega.
- [ ] AR ensayada; indicada explícitamente simulación o dispositivo físico.
- [ ] Guardadas fichas antes/después y evidencias de pruebas en rutas accesibles.
- [ ] Código, scripts nuevos, shaders y sus `.meta` incluidos en Git.
- [ ] Informe/guía enlazados desde el repositorio; cambios finales publicados.
- [ ] Canvas: URL del repositorio, hash o tag exacto y enlace al Markdown solicitado.

**Ensayo entre tres:** A presenta y B pregunta, C verifica el archivo/dato; rotar
hasta que los tres expliquen el flujo completo. Practicar especialmente diafragmas,
momentos extremos, demanda/capacidad, edición y límites del AR.

**Datos por completar por el grupo:**

```text
Integrantes:
Commit/tag EXACTO a evaluar:
Modelo/fuente de la demo:
Viga / columna / muro / losa elegidos:
Dos parámetros y valores antes/después:
Equipo AR y tipo de prueba:
Fecha del ensayo completo y observaciones:
Enlace directo al informe exigido por el curso:
```

Documentación complementaria: [armadura](VERIFICACION_ARMADURA.md),
[radar](RADAR_DEMANDA.md), [LRFD](LABORATORIO_LRFD.md),
[AR](unity_visualizador/Assets/StructuralAR/README_AR.md), [sismo](seismic/README.md).
