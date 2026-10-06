# Structural X-Ray — Red de respuesta incremental

## Uso en Unity

Proyecto de escritorio: `P1L4/unity_visualizador`. Detener Play y volver a
iniciarlo después de esta actualización, para reiniciar el worker con la
exportación de huella del modelo y reacciones.

1. En Capas, abrir **STRUCTURAL X-RAY · TRAZA LA CARGA**. Si ya hay una carga
   aplicada, se inicia el trazado automáticamente. Si falta, se abre su configuración
   y las losas se hacen visibles para colocar la persona.
2. Pulsar **PERSONA** para abrir la configuración existente. Aplicar la carga
   en una losa válida y esperar la respuesta de OpenSees.
3. El trazado aparece al recibir la respuesta; **TRAZAR** también permite volver
   a la presentación después de configurar la persona. Si el proceso devuelve una
   huella antigua, se reinicia una vez conservando la carga y su posición. Se ocultan temporalmente las interfaces alternativas de
   carga móvil, manteniendo la persona y el cálculo activos. No equivale a
   **Ocultar carga móvil**, que conserva su comportamiento de retirar la carga.
4. El edificio queda como contexto translúcido. Se destaca la región tributaria
   de los receptores efectivos, vigas, columnas/muros equivalentes y una conexión
   real hacia un apoyo declarado. Un pulso fino gris identifica brazos de vinculación.
5. Pulsar **SEGUIR LA CARGA**: recorrido de 18 s, desde vista general hasta
   región, receptores, conexión vertical, apoyo y resumen. La respuesta se congela
   para la secuencia. Al terminar queda congelada; **LIVE X-RAY** retoma actualizaciones.
6. Click en el ranking selecciona y enfoca la pieza. **X-RAY · ¿POR QUÉ?** en
   Resultados muestra incremento firmado, posición, intensidad relativa, conexiones,
   receptor real cuando corresponda, apoyo de la ruta, huella y procedencia.
7. Desmarcar **Modo presentación** para elegir ΔN, ΔVy, ΔVz, ΔMy, ΔMz, ΔT o ΔU,
   umbrales y conexiones MPC de contexto. Todos los puestos de un ranking usan
   la misma métrica/unidad. ΔU es el máximo de la norma traslacional de sus nodos,
   sin inventar rotaciones ni deformación interior de la barra.
8. **EXPORTAR RED Y TRAZABILIDAD .CSV** guarda filas por elemento y reacciones
   medidas; copia automáticamente la ruta al portapapeles.
9. **SALIR** retira solo esta visualización y restaura renderers, capas, filtros,
   interfaces y preferencias de cámara que tomó temporalmente. No elimina la carga.
   Si se retira la persona mediante su control existente, también desaparece la red.

### Primera persona

Con una red calculada visible, entrar al modo de primera persona existente.
La escena conserva una **foto de la respuesta calculada**: el modo de caminar
deshabilita el worker móvil como ya hacía anteriormente. No se afirma una
actualización LIVE mientras ese worker está deshabilitado.

Mirar una arista destacada y hacer click permite consultar su explicación;
se pausa la navegación para leer y el botón de cerrar permite continuar.
El recorrido cinematográfico no toma la cámara en primera persona. Al volver
al modo de órbita se puede retomar LIVE con las nuevas respuestas del worker.

## Interpretación técnica de la versión implementada

- Los datos son la respuesta incremental **localForce** del worker, calculada
  por superposición de cargas nodales unitarias para la misma estructura lineal.
  Nunca se resta un máximo de un diagrama a otro máximo en distinta posición.
- Cada pieza se evalúa en 61 posiciones I→J. Su puesto usa el mayor valor absoluto
  de la componente incremental elegida; se conserva el valor firmado y x/L.
  La intensidad es ese máximo dividido por el máximo de la misma métrica.
- Brazos de vinculación y MPC no participan como piezas físicas en el ranking.
  Los muros se muestran en su **eje analítico equivalente**, asociado al muro
  visual mediante `analysisElements`; no es un mapa de tensiones del panel.
- La ruta usa aristas I–J existentes y prefiere una conexión legible hacia abajo.
  Incluye brazos reales si hacen falta y termina en un apoyo declarado con Uz
  restringido. Si no existe una conexión de ese tipo, no se inventa una ruta.
- Las MPC XY/Rz se conservan en el grafo y en las explicaciones. No se dibujan
  como barras que transmitan directamente la carga vertical de la persona.
  Su contexto opcional muestra hasta 12 líneas punteadas, no toda la membresía.
- AUTO incluye un grupo compacto de dominantes, receptores, elementos verticales
  y los conectores necesarios. El tope de selección por ranking es 48; la ruta y
  receptores pueden añadir aristas. El contador permite saber cuántas se muestran.
- Un receptor puede tener ΔMy=0 y seguir siendo receptor del reparto nodal:
  pertenencia tributaria e intensidad de una componente no son equivalentes.
- El apoyo de la ruta y el apoyo de mayor |ΔRz| pueden ser distintos. La ruta
  es una conexión explicativa, no una distribución de porcentajes de reacción.
- ΣΔRz se compara con P y el error de reparto con ΣP nodal. Se exportan también
  Fx/Fy/Fz y Mx/My/Mz globales de los nodos realmente fijos, distinguiendo apoyos
  declarados de nodos auxiliares. No se inventan reacciones si falta esa exportación.
- Los segundos del recorrido y los pulsos son narrativos, no velocidad de
  propagación física. No se presenta daño, rotura, D/C ni cumplimiento global.
- LIVE actualiza por respuesta recibida, con un máximo de una reconstrucción
  cada 0,35 s. Siempre se muestra seq, losa y posición **calculadas**. La persona
  puede haber avanzado mientras llega el siguiente cálculo.
- La huella se contrasta con el archivo de modelo cargado. Resultados de otro
  modelo, estados desactualizados, sismo y comparación simultánea se rechazan/pausan.
- Se utilizan dos mallas de contexto por lotes y líneas reutilizables con un
  shader de pulso; no hay un sistema de partículas por elemento. No se altera
  ningún material original, collider, nodo, esfuerzo ni archivo de modelo.

## Pruebas ejecutables

```powershell
python -m unittest discover -s P1L4/tests -p test_mobile_slab.py
python P1L4/tests/test_xray_network.py
& P1L4/tests/verificar_armadura_seccion.ps1
```

El segundo test compara el worker contra `R(G+persona)−R(G)` de corridas directas,
incluidas reacciones, y conserva el hash del modelo. También comprueba el equilibrio
de momentos de la **aproximación nodal aplicada**; no atribuye conservación de
momentos al punto original de la persona cuando fue proyectado a un borde.

La tercera prueba incluye 4.026 comprobaciones de red sobre ese conjunto real:
rankings, posiciones, conectividad, destinos, MPC diferenciadas, nulos/ausentes,
presupuesto AUTO, regiones y vacíos, y compila el runtime completo. Los conjuntos
de entrada de validación se regeneran en `xray_validation/`, excluido de Git.

Pendiente de ensayo visual en Editor: legibilidad de materiales/pulsos, recorrido
de cámara, cambio de losa LIVE, restauración al salir y selección en primera persona.
No se declara una prueba móvil/AR de este módulo; su núcleo de datos queda separado
de la cámara para una integración futura.

## Propuesta técnica previa a la implementación

1. Reutilizar `MobileLoadController`/`mobile_slab_worker.py`, `UnityData`,
   `FrameForces`, `ElementPicker`, `OrbitCamera`, el catálogo tributario y los
   grupos de diafragma reales. No reemplazar carga móvil ni solver.
2. Archivos desacoplados: `IncrementalResponseNetwork`, `XrayTributaryRegion`,
   `StructuralXRayController`, `StructuralXRayOverlay`, vista de explicación del
   panel y shaders de contexto/pulsos.
3. Grafo: nodos estructurales y maestros auxiliares; aristas I–J nativas, brazos
   de vinculación y restricciones de diafragma con tipos distintos.
4. DeltaR: acciones locales incrementales y desplazamientos del worker de carga
   móvil. Es `R(base+persona)−R(base)` para la misma K, calculado por respuestas
   unitarias OpenSees; no se restan máximos de diagramas.
5. Base: caso estático activo sin persona. El incremento no depende de sus
   factores mientras la estructura sea la misma y lineal. Se excluyen sismo
   dinámico, resultados desactualizados y comparación simultánea de diseños.
6. Métricas: N, Vy, Vz, T, My, Mz por posiciones coincidentes y desplazamiento
   traslacional de nodos. Ranking de una métrica y unidad a la vez; My inicial.
7. Destinos: apoyos declarados del modelo con restricción vertical. Los maestros
   auxiliares restringidos no se presentan como fundaciones.
8. Losa–viga: receptores y pesos exportados de la persona en esa posición, no
   proximidad espacial. Región usando la misma regla de borde más cercano y
   vacíos del worker. Las paredes decorativas no se inventan como receptores.
9. Ranking: máximo absoluto del incremento de la componente sobre muestras de
   cada barra; dato firmado y posición conservados. Brazos/MPC fuera del ranking.
10. AUTO: un conjunto compacto de piezas dominantes más receptores y conexiones
    necesarias. Umbrales/top porcentual con límite visual explícito.
11. Sin cambiar análisis: observación del evento de respuesta, caches, grafo,
    ranking y animación. Para reacciones solo se amplía la exportación de datos
    que el mismo análisis ya calcula, sin modificar cargas o solución.
12. Límites: carga puntual aproximada nodalmente; no shells, tensiones de losa,
    daño, porcentaje de flujo ni propagación temporal real. Un recorrido hacia
    un apoyo no mide qué porcentaje de carga pasa por cada arista.
13. Riesgos: respuesta atrasada respecto de la persona, modelo cambiado, grupos
    que vinculan edificios, competencia de cámaras/colores y transparencia.
    Mostrar posición calculada/seq, comprobar huella y restaurar estados.
14. Fases: núcleo numérico → región/red → pulsos → cámara → LIVE/presentación.
    Primera persona observa una respuesta congelada; AR queda fuera de esta fase.
15. Pruebas: transferencia y equilibrio, fuerza incremental contra corrida
    directa, ranking/posición manual, conectividad, apoyos, casos nulos/ausentes,
    conservación de JSON y compilación. La validación visual y de navegación
    dentro del Editor se reporta por separado.
