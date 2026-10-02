# Semana 6 — Integración AR y QA estructural final

**Proyecto:** MCOC, modelo estructural P1L4.

**Fecha de revisión:** 2 de octubre de 2026.

**Base revisada:** commit `aed1842` y datos actuales del repositorio.

**Unidades del análisis:** kN, m y kN·m; curvatura en m⁻¹.

El visualizador principal está en `P1L4/unity_visualizador`; la copia independiente
para iPhone está en `P1L4/unity_visualizador_ios`. Ambos consumen actualmente el mismo
JSON estructural. Este informe distingue implementación, comprobación numérica y
prueba física: ninguna de ellas sustituye automáticamente a las otras.

## 1. Flujo AR

El flujo exigido por la entrega es:

```text
marker → pose → anchor → transform → elemento → resultado
```

| Etapa | Función | Estado actual |
| --- | --- | --- |
| `marker` | Usar una referencia espacial para relacionar modelo y entorno | Etapa de referencia del flujo general; el procedimiento documentado utiliza puntos I–J elegidos por el usuario. |
| `pose` | Obtener posición y orientación respecto del espacio AR | Implementado mediante dos puntos I–J elegidos con la mira y una cara de referencia. |
| `anchor` | Mantener la colocación mientras el proveedor XR sigue el entorno | Implementado con `ARAnchorManager.TryAddAnchorAsync`; cada elemento tiene su propio anchor. |
| `transform` | Alinear, orientar y dimensionar la representación del elemento | Implementado mediante conversión de ejes, alineación I–J, ajuste de cara y longitud visual. |
| `elemento` | Mostrar una viga o columna real del modelo | Implementado: conserva el ID interno, `elementTag`, nodos y sección del JSON. |
| `resultado` | Consultar fuerzas del caso elegido | Implementado: diagramas N, Vy, Vz, My y Mz, con C1/C2/C3 y comparación entre combinaciones. |

Por tanto, el flujo operativo actual es:

```text
Elegir ID → marcar I → marcar J → ajustar cara → confirmar pose
→ crear anchor → transformar representación → elemento → resultado OpenSees
```

La app no reconoce automáticamente qué viga del edificio se observa. El usuario
debe identificarla en el modelo o plano y respetar sus nodos I/J. La geometría
anclada muestra la línea del elemento y su diagrama, sin el volumen azul opaco;
un collider invisible permite seleccionar el elemento por toque.

La colocación guiada permite corregir un extremo, desplazar el elemento en pasos
de 1/2/5 cm, girarlo y agregar varios miembros sin borrar los anteriores. Si el
anchor pierde seguimiento, la representación se oculta hasta recuperarlo.
Los anchors duran la sesión: no existe restauración persistente tras cerrar la app.

### Teléfono y cálculo previo

En el teléfono corren Unity, la cámara, AR Foundation, el proveedor ARKit o ARCore,
la colocación, el seguimiento, la selección y el dibujo de diagramas. También se
evalúan las expresiones de esfuerzo de sección a partir de las acciones exportadas.

OpenSees, las combinaciones, los desplazamientos, las fuerzas de extremo y las
curvas de capacidad se calculan previamente en el computador y se exportan al JSON.
La AR no ejecuta Python ni reanaliza el edificio. Cambiar la posición visual,
la longitud dibujada o la combinación mostrada no cambia el modelo analítico.

El usuario confirmó que la cámara y la aplicación funcionan en su iPhone después
de corregir los permisos. Esto es evidencia de funcionamiento comunicada por el
usuario; no constituye una medición de precisión. La prueba en un teléfono Android
también fue realizada, según la confirmación del autor del proyecto.

## 2. Transformación entre sistemas

### OpenSees → Unity

En OpenSees, X/Y son horizontales y Z es vertical. El proyecto representa la
vertical con Y de Unity. Para un punto estructural en metros:

$$
\mathbf p_O=(x_O,y_O,z_O)^T,\qquad
\mathbf p_U=C\mathbf p_O=(x_O,z_O,y_O)^T,
$$

$$
C=\begin{bmatrix}1&0&0\\0&0&1\\0&1&0\end{bmatrix}.
$$

Esta conversión aparece en `StructuralARController.ToUnity` y
`UnityData.AxisToUnity`. C intercambia dos ejes y tiene determinante −1: es un
cambio de convención de coordenadas, no una rotación física. Los ejes locales se
construyen primero con `FrameGeometry`, siguiendo el eje I→J y el vector de
referencia de la transformación del análisis; luego se convierten para dibujarlos.
Las fuerzas exportadas ya son **locales**, según
`p1l4.elementForceCoordinates = "local"`; no deben confundirse con fuerzas globales.

### Unity → espacio AR

Sean I/J los extremos convertidos a Unity, y a/b los extremos marcados en AR:

$$
\mathbf c_U=\frac{\mathbf I_U+\mathbf J_U}{2},\quad
\mathbf c_R=\frac{\mathbf a+\mathbf b}{2},\quad
L=\|\mathbf J_U-\mathbf I_U\|,\quad L_R=\|\mathbf b-\mathbf a\|.
$$

La rotación R alinea I→J con a→b y añade el giro alrededor del eje longitudinal
necesario para que la cara elegida coincida. Se implementa con
`Quaternion.FromToRotation` y `Quaternion.AngleAxis` en `UpdateFacePreview`.
El anchor se solicita con centro c_R y esa rotación en `ConfirmCalibration`.

En la colocación calibrada, el código fija `uniformScale = 1`: mantiene el ancho
y alto originales de la sección, pero dibuja el largo marcado por el usuario.
Por tanto, el ajuste longitudinal no es una escala uniforme de todo el edificio.
Con u como eje unitario del elemento y λ = L_R/L, puede expresarse como:

$$
A=I_3+(\lambda-1)\mathbf u\mathbf u^T,\qquad
\mathbf p_R=\mathbf c_R+R\,A\,(C\mathbf p_O-\mathbf c_U).
$$

A cambia la longitud en la dirección u y conserva las dimensiones transversales.
La representación equivalente mediante transforms homogéneos es:

$$
\tilde{\mathbf p}_R=
T_{R\leftarrow anchor}\,T_{ajuste}\,
\begin{bmatrix}A&-A\mathbf c_U\\0&1\end{bmatrix}
\begin{bmatrix}C&0\\0&1\end{bmatrix}\tilde{\mathbf p}_O.
$$

`T_ajuste` representa las correcciones visuales posteriores de traslación y giro.
La jerarquía utilizada es `anchor → raíz visual → elemento/diagramas`.
El anchor mantiene la referencia espacial mientras el seguimiento XR sea válido;
no garantiza por sí solo que el usuario haya marcado correctamente los nodos.

Para un punto del diagrama, t va de 0 en I a 1 en J. Su posición dibujada usa L_R,
pero su valor se evalúa con la longitud analítica L y las fuerzas del JSON.
La altura del diagrama se normaliza para hacerlo legible; no es una deformación
física ni una escala de esfuerzos en metros. En vigas, My/Mz positivos se dibujan
hacia abajo y negativos hacia arriba, sin cambiar los signos numéricos.

En un flujo basado en marker, su pose y la calibración marker–modelo definen la
referencia espacial para componer las transformaciones anteriores.

## 3. Precisión y error de alineamiento

**No hay una campaña de medición física registrada.** La siguiente estimación es
ilustrativa y no debe presentarse como precisión medida del iPhone.

Para la viga E1_94, L = 4,12 m. Suponiendo un error máximo e = 0,02 m al marcar
cada extremo, una estimación conservadora del error angular es:

$$
\delta\theta\lesssim\frac{2e}{L}=
\frac{0,04}{4,12}=0,00971\;\text{rad}\approx0,56^\circ.
$$

El error de longitud puede alcanzar 2e = 0,04 m, aproximadamente el 0,97 %.
Una estimación simple para un punto a distancia d de la referencia es:

$$
e_{alineamiento}(d)\lesssim e_{traslación}
+d\,\delta\theta+d\,|\delta s|+e_{deriva}.
$$

Por ejemplo, a d = 2 m, con traslación de 2 cm, giro de 0,56° y error de escala
del 0,97 %, el límite aproximado es 5,9 cm **antes de sumar deriva del tracking**.
Es una suma conservadora de contribuciones; no es un intervalo estadístico.

Para obtener una cifra defendible se propone medir cinco colocaciones de la misma
viga: distancia entre el extremo virtual y el físico en I/J y un punto intermedio,
con regla o cinta. Registrar además el error después de caminar alrededor y a los
30/60 segundos. Informar promedio, máximo, longitud marcada, iluminación y pérdidas
de tracking. No usar los mismos dos puntos de calibración como única comprobación:
un tercer punto independiente permite detectar error de giro o de cara.

## 4. Resultados y evidencia de correspondencia

### Elemento real del modelo: E1_94

| Dato | Valor verificado |
| --- | --- |
| Identificador visible | `elementTag = E1_94` |
| ID interno del elemento | `94` |
| Tipo / nivel | Viga / `CIELO_2` |
| Sección | `V60/80`: ancho 0,60 m y alto 0,80 m |
| Nodo I | `61`, OpenSees (7,51; −7,25; 8,00) m |
| Nodo J | `60`, OpenSees (7,51; −11,37; 8,00) m |
| I en Unity | (7,51; 8,00; −7,25) m |
| J en Unity | (7,51; 8,00; −11,37) m |
| Longitud analítica | 4,12 m |
| Caso mostrado | `C1 = G + 0,5Q + 0,3EX + 0,2EY` |

Los nodos I/J provienen de `nodeI`/`nodeJ`, no del menor número de nodo ni del
extremo más cercano a la cámara. En esta viga I es N61 y J es N60.

El registro `p1l4.elementForces` con `combo = C1` e `id = 94` contiene las acciones
locales de extremo. Aplicando la misma expresión de `FrameForces.Evaluate` que usa
Unity se obtienen los siguientes **esfuerzos de sección**:

| Posición I→J | N [kN] | Vz [kN] | My [kN·m] |
| --- | ---: | ---: | ---: |
| I, t = 0 | 0,000 | 210,714 | −416,928 |
| Centro, t = 0,5 | 0,000 | 172,215 | −22,511 |
| J, t = 1 | 0,000 | 133,715 | +292,596 |

El componente My de la **acción nodal J** exportada es −292,596 kN·m; el esfuerzo
de sección en la cara J es +292,596 kN·m. Esta diferencia de convención no es un
error de datos ni una inversión de la combinación.

Para reproducir el valor del centro, a partir del registro actual:

$$
q_z=\frac{f_2+f_8}{L}=18,6893\;\text{kN/m},\quad x=L/2=2,06\;\text{m},
$$

$$
M_y(x)=f_4+f_2x-\tfrac12q_zx^2=-22,5113\;\text{kN·m}.
$$

### Evidencia y trazabilidad

1. [JSON consumido por Unity](../P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json): relaciona `E1_94 → id 94 → N61/N60` y el registro de fuerzas C1.
2. [Evaluador de secciones](../P1L4/unity_visualizador/Assets/Scripts/FrameForces.cs): convierte acciones locales a esfuerzos de sección.
3. [Controlador AR](../P1L4/unity_visualizador/Assets/Scripts/StructuralARController.cs): consulta `UnityData.TryGetSectionForces(selectedElement.id, activeCombo, t, ...)`; no crea otro ID ni otro resultado.
4. [Validación del sector AR](../P1L4/unity_visualizador/Assets/Scripts/Editor/StructuralARSectorValidation.cs): comprueba ajustes, independencia de elementos, comparación y orientación de momentos, incluyendo E1_94.

El JSON Android e iOS se comparó como objeto completo y coincide. La revisión
encontró 817 IDs internos y 817 `elementTag`, todos únicos, y 5.719 registros de
fuerzas: 817 elementos × 7 casos. El SHA-256 del JSON principal revisado es:

```text
1af9fb52af971147a00ba83d1eb01033cd7c9d1dcb9480aa35fd84a676606cd4
```

Esto demuestra correspondencia **digital** entre modelo, datos y consumidor AR.
Para cerrar la evidencia física falta adjuntar una captura o video del teléfono
que muestre E1_94, C1, nodos I/J y el resultado sobre el elemento identificado.
No se adjunta una imagen simulada como si fuera una medición en el edificio.

## 5. QA final estructural

Los estados se refieren a las pruebas indicadas, no a una certificación global del
edificio. Se revisó el JSON actual y se ejecutó
`P1L4/tests/verificar_modelo.py` con OpenSeesPy: **PASS, 36.971 comprobaciones**.
También se ejecutó el modo `--numpy`, que reproduce el modelo lineal con
`mini_opensees`, con el mismo conteo de comprobaciones aprobadas. El primer intento
de arrancar `.venv` dentro del entorno restringido falló; la ejecución autorizada
fuera de esa restricción funcionó. No se considera una falla del entorno Python.

| Prueba | Estado |
| --- | --- |
| Equilibrio G | **PASS numérico.** Carga vertical −60.353,829658 kN y reacción +60.353,829658 kN; desbalance vertical 7,77×10⁻⁹ kN en el export. Reproducción lineal aprobada. |
| Equilibrio Q | **PASS numérico.** Carga −19.642,112786 kN y reacción +19.642,112786 kN; desbalance 1,80×10⁻⁹ kN. |
| Corte basal EX | **PASS numérico.** Carga horizontal +13.847,344710 kN y reacción X −13.847,344710 kN; residuo X 3,42×10⁻¹⁰ kN. |
| Corte basal EY | **PASS numérico.** Carga horizontal +13.847,344710 kN y reacción Y −13.847,344710 kN; residuo Y 7,69×10⁻¹⁰ kN. |
| Superposición | **PASS.** Error máximo de fuerzas C1/C2/C3 = 9,962×10⁻⁹; error máximo de desplazamientos = 5,334×10⁻¹⁷. Tolerancia de comparación 10⁻⁶ por componente y su unidad. |
| `M-phi` | **Disponible; QA parcial.** La curva de columna exportada tiene 800 puntos; primera fluencia registrada: φ = 0,0046 m⁻¹, M = 849,527 kN·m. Hay estudio de sensibilidad de malla y curva de muro de 122 puntos. No se recalculó el análisis de sección no lineal en esta revisión. |
| `P-M` columna | **PASS de datos y comparación del visor.** Curva `COL70/70_FIBER` de 5 puntos; evaluadas 124 columnas en cada C1/C2/C3, sin registros faltantes ni razón > 1 en el método actual. Esto no equivale a verificación biaxial ni a validar todas las hipótesis de armadura. |
| `P-M` muro | **Datos completos; NO CUMPLE en algunos paneles.** 75/75 registros tienen curva asociada. C1: índices 11 y 26 fuera de curva, índice 31 con razón 1,027; C2: 11 y 36 fuera; C3: 21 fuera. |
| IDs Unity | **PASS de integridad digital.** 817 IDs y tags únicos; nodos y fuerzas de E1_94 trazables; JSON estructural idéntico en Android/iOS. |
| AR | **Implementado y probado en iPhone y Android**, según las pruebas comunicadas por el autor. Colocación I–J, anchors, ajustes, sector y resultados con validaciones previas de editor. La medición cuantitativa de precisión sigue pendiente. |

La superposición comprobada corresponde a:

```text
C1 = G + 0.5Q + 0.3EX + 0.2EY
C2 = G + 0.5Q + 0.3EX - 0.2EY
C3 = G + 0.5Q - 0.3EX + 0.2EY
```

El QA P–M reutiliza `UnityResultsValidator.capacity_ratio`, por lo que valida la
correspondencia con el visor, no constituye una verificación independiente de su
método de capacidad. En particular, `99,99` es un indicador de demanda fuera del
rango axial de la curva, no una razón demanda/capacidad física calculada.

## 6. Errores conocidos y limitaciones

- **Alineamiento sin metrología:** no hay error medido ni ensayo de deriva. Profundidad libre, selección equivocada de I/J o cara incorrecta pueden producir una superposición visual convincente pero mal ubicada.
- **Longitud visual independiente del análisis:** una viga puede colocarse en un tramo de longitud diferente; sus esfuerzos siguen siendo los del elemento original. Deben mostrarse ambas longitudes y no inferir una respuesta recalculada.
- **Persistencia limitada:** el sector no se guarda entre sesiones; pérdida de tracking oculta los elementos. Falta probar recuperación y estabilidad en condiciones reales.
- **Muros con demandas fuera de capacidad:** los incumplimientos de la tabla requieren revisar demanda, sección y armadura; no deben ocultarse mediante ajustes del dibujo.
- **Capacidad simplificada:** el panel de columnas compara un momento resultante con una curva P–M uniaxial. No dispone de una superficie P–Mx–My. Las hipótesis de armadura y aplicabilidad de la curva de referencia deben revisarse.
- **Modelo global lineal:** equilibrio y superposición no demuestran comportamiento no lineal, desempeño sísmico ni cumplimiento integral del diseño. Los muros se idealizan con miembros equivalentes y brazos rígidos.
- **Entornos independientes:** las copias Android/iPhone no se sincronizan automáticamente. El nuevo recorrido de cargas y las mejoras visuales del edificio pertenecen al proyecto principal; no se afirma que estén incorporados en el último ZIP iOS.
- **Advertencias del QA OpenSees:** la corrida aprobada emitió mensajes repetidos `WARNING no response is found` y un aviso de integrador estático predeterminado. Deben revisarse las consultas de respuesta y la configuración del análisis; el PASS de las comprobaciones no elimina esas advertencias.
- **Documentación histórica desactualizada:** semana05 y algunas guías conservan cifras y estados anteriores, incluido el estado de cámara iOS. Para esta entrega prevalecen el JSON actual y este informe; no se reutilizan antiguos cortes basales ni conclusiones P–M sin comprobarlos.
- **Elementos decorativos:** terreno, pendiente, muros salmón, techo y escaleras no tienen cargas ni apoyos y no participan en el cálculo. La pendiente hacia −X puede tapar parte del edificio por decisión visual; se oculta con `Terraza Y=4` y las escaleras con `Escaleras`.

## 7. Plan final

### Núcleo

1. Consolidar la trazabilidad entre referencia espacial, pose, anchor, elemento y resultado; verificar que se conserve `elementTag` durante todo el flujo.
2. Registrar prueba en iPhone: permiso, cámara, I/J, anchor, ID, combinación y resultado; medir alineamiento y deriva con el protocolo de la sección 3.
3. Revisar las advertencias del QA, guardar evidencia reproducible y repetirlo al cambiar el modelo; revisar los paneles de muro no conformes y las hipótesis de capacidad.
4. Preparar una demostración de E1_94 y un sector pequeño, con correspondencia física documentada, y reunir la evidencia de las pruebas ya realizadas en iPhone y Android.
5. Mantener sincronizados deliberadamente los cambios necesarios entre copias y exportar un ZIP Xcode actualizado cuando cambie la versión iPhone; conservar únicamente el último entregable vigente.

### Polish

- Afinar legibilidad, tamaño del panel, contraste de diagramas y etiquetas I/J sin cambiar valores numéricos.
- Mostrar advertencias claras sobre largo visual versus analítico y seguimiento limitado.
- Mejorar navegación, selección de varios elementos y presentación de C1/C2/C3.
- Optimizar materiales, sombras y número de objetos decorativos para el teléfono. Conservar los controles para ocultar terreno, terraza, fachadas, techos y escaleras.
- Preparar capturas finales y una explicación breve de escala, rotación, traslación, anchor y cálculo previo para la defensa.

### Honors

Los Honors son extensiones opcionales del proyecto. Dentro de AR avanzada ya se
cuenta con varios anchors, selección independiente de elementos, ajuste fino,
colocación guiada y comparación C1/C2/C3. Estas funciones amplían la experiencia
de consulta estructural sin modificar el modelo analítico.

Como siguiente extensión opcional, se ha definido un plan de acción para Google
Cardboard VR.

### Plan de acción para Google Cardboard VR

Se propone implementar este Honor después de cerrar el núcleo, reutilizando la
geometría y resultados sin cambiar el cálculo estructural. El SDK oficial permite
renderizado estereoscópico, seguimiento de orientación de cabeza, selección con
botón del visor y configuración para Android/iOS. La referencia de implementación
es la [guía oficial de Google Cardboard para Unity](https://developers.google.com/cardboard/develop/unity/quickstart).

1. **Prototipo aislado:** crear una copia o proyecto VR separado para conservar el flujo AR existente. Integrar una versión identificada del plugin oficial y comprobar primero `HelloCardboard` en el iPhone disponible; validar compatibilidad con la versión de Unity del proyecto.
2. **Escena estructural VR:** cargar el mismo JSON y reutilizar `elementTag`. Crear cámara estereoscópica, orientación de cabeza y escala en metros, con puntos de observación predefinidos del edificio.
3. **Interacción:** seleccionar con retícula y botón del visor, mostrar ID y C1/C2/C3 en un panel espacial legible, y permitir activar un diagrama. Añadir recentrado y salida del modo VR.
4. **Navegación y rendimiento:** usar cambios entre puntos de observación y giros controlados; reducir sombras y decoraciones cuando sea necesario. Medir fluidez y estabilidad en el teléfono con el visor real.
5. **Entrega iPhone:** exportar un ZIP Xcode independiente y vigente; compilar y firmar en Mac. Verificar también la lectura del QR del visor y sus permisos si se utiliza esa función.
6. **Aceptación y evidencia:** grabar la experiencia estereoscópica y documentar selección de E1_94, su ID y resultado C1 idéntico al JSON. Registrar funcionamiento del recentrado, controles y ausencia de cambios en fuerzas/cargas.

## Referencias de verificación

- [Pruebas del modelo](../P1L4/tests/verificar_modelo.py) y [auditor numérico del contrato Unity](../P1L3/unity_results_validation.py).
- [Guía de colocación AR](../P1L4/unity_visualizador/Assets/StructuralAR/README_AR.md) y [guía iPhone/Xcode](../P1L4/unity_visualizador_ios/README_IOS.md).
- [Sensibilidad M–φ](../P1L3/resultados/part_d_sensibilidad.json), [gráfico M–φ](../P1L3/resultados/M_phi_COL70_70.png) y [resultados de sección del muro](../P1L3/resultados/part_e_wall.json).

Las validaciones anteriores de editor están registradas localmente en los logs AR
de ambos proyectos; esas carpetas no se versionan. Las pruebas nuevas con OpenSeesPy
y `--numpy`, y las comparaciones de datos, se ejecutaron durante la preparación de este informe, sin
regenerar ni modificar resultados de producción.
