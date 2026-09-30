# Structural AR sin marcador: colocacion libre I-J

El panel comienza contraido en la parte inferior; `Menu / resultados` abre
los controles y `Contraer` vuelve a dejar libre la vista. Respeta el area segura.
Una vez anclado, el volumen virtual es invisible: se muestran la linea I-J y
el diagrama OpenSees sin cubrir la viga fisica. El collider invisible conserva
la seleccion por toque y el mismo `elementTag`.

La escena independiente `Assets/Scenes/StructuralARScene.unity` lee geometria,
`elementTag` y fuerzas reales de `Assets/Resources/estructura_p1l4_unity.json`.
La camara no identifica por si sola una viga o columna fisica: el usuario
confirma su ID en el plano estructural. La calibracion usa las dimensiones
del elemento exportado, sin cambiar el analisis de OpenSees.

## Probar en XR Simulation

1. Abre `P1L4/unity_visualizador` en Unity 6000.6.0f1.
2. Si cambiaste scripts o el entorno, fuera de Play ejecuta
   `MCOC > AR > Crear o reparar escena Structural AR`.
3. Abre `Assets/Scenes/StructuralARScene.unity` y presiona Play.
4. Elige `VIGAS` o `COLUMNAS` y escribe un ID interno o `elementTag`
   existente, por ejemplo `E1_72` o `E1_229`.
5. Pulsa `Usar elemento → Marcar I`. La colocación libre está activa por defecto. Ajusta `Profundidad`
   hasta que la esfera celeste este donde quieres I. Apunta y pulsa
   `FIJAR NODO I`. No necesitas un punto AR ni color verde.
6. Apunta al lugar donde quieres J y pulsa `FIJAR NODO J`. No se calcula
   una posicion esperada: I y J son los dos puntos elegidos por ti. Puedes
   cambiar de nuevo la profundidad antes de fijar J. La linea celeste solo
   une los puntos marcados; su color no bloquea el boton.
7. Comprueba el contorno entre I y J y la flecha magenta de la cara. Puedes apuntar
   a la cara que estás mirando con `Capturar cara con la mira`, o girarla en pasos de 90°.
   Si la dirección no calza, pulsa `Corregir J (conservar I)`.
   Pulsa `Cara lista → Revisar anclaje` y luego `ANCLAR ELEMENTO` para crear el anchor XR.
8. Toca el elemento virtual para consultar C1/C2/C3 y diagramas N, Vy, Vz,
   My, Mz. El `elementTag` y el ID siguen siendo los del JSON.

La viga y columna de practica tienen la longitud y seccion reales de los
elementos de ejemplo, pero no representan una identificacion automatica del
objeto fisico. La geometria virtual y sus diagramas ocupan exactamente el
tramo elegido entre I y J. El panel distingue el **largo visual marcado**
del **largo original del modelo OpenSees**. Si no son iguales, la longitud
del dibujo se ajusta para encajar entre los puntos; los esfuerzos y el
elementTag siguen siendo los datos originales, sin reanalisis estructural.
Si desactivas `freePlacement` en el Inspector, la mira intentará primero detectar una
superficie; si no la encuentra, volvera a usar la profundidad libre.
Al desplazarse la camara simulada, el anchor conserva la superposicion en el
espacio XR mientras el seguimiento permanezca activo. Si se pierde tracking,
el elemento se oculta hasta recuperarlo.

Los diagramas usan 41 muestras de `UnityData.TryGetSectionForces`. Los valores
son los exportados por OpenSees; solo se normaliza la altura grafica del
diagrama. La calibracion no modifica cargas, resultados ni deformaciones.

En `Resultados` puedes superponer C1/C2/C3 con una escala común y consultar sus
extremos. `Ajuste` permite mover todo el elemento o corregir I/J en pasos de 1/2/5 cm;
los giros actúan sobre el elemento completo. `+ Agregar otro elemento` conserva los
anteriores. Cada colocación se selecciona por su número en `Sector` o tocándola en AR.
El volumen sólido es invisible; se muestra la línea con su diagrama. El sector dura
esta sesión y no se guarda al cerrar. Consulta README_IOS.md para los límites y pruebas.

## Telefono iPhone (copia independiente)

Esta copia declara Apple ARKit XR Plugin 6.6.2. Se requiere iOS Build
Support y un iPhone compatible con ARKit. Sigue README_IOS.md en la raiz
de esta copia para obtener el ZIP exportado y abrirlo en Xcode. En el lugar real hay que
identificar correctamente el elemento y sus nodos I/J, ajustar manualmente
la profundidad cuando no hay superficie detectada, y validar la precision del
anchor caminando alrededor. La colocacion manual no es una certificacion de
precisión metrológica. La cámara de la versión anterior fue confirmada por el usuario
en iPhone. Los ajustes y el sector de esta versión deben comprobarse en el teléfono
después de compilar el ZIP en Xcode.
