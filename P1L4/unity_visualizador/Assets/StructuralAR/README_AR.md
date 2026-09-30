# Structural AR sin marcador: colocacion libre I-J

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
5. `COLOCACION LIBRE` esta activa por defecto. Ajusta `Profundidad de la mira`
   hasta que la esfera celeste este donde quieres I. Apunta y pulsa
   `1 FIJAR NODO I EN LA MIRA`. No necesitas un punto AR ni color verde.
6. Apunta al lugar donde quieres J y pulsa `2 FIJAR NODO J`. No se calcula
   una posicion esperada: I y J son los dos puntos elegidos por ti. Puedes
   cambiar de nuevo la profundidad antes de fijar J. La linea celeste solo
   une los puntos marcados; su color no bloquea el boton.
7. Comprueba el contorno entre I y J y la flecha magenta de la cara. Puedes apuntar
   a la cara que estas mirando con `3 TOCAR CARA`, o girarla en pasos de 90°.
   Si la direccion no calza, pulsa `AJUSTAR J OTRA VEZ` sin perder I.
   Pulsa `ANCLAR ELEMENTO ENTRE I Y J` para crear el anchor XR.
8. Toca el elemento virtual para consultar C1/C2/C3 y diagramas N, Vy, Vz,
   My, Mz. El `elementTag` y el ID siguen siendo los del JSON.

La viga y columna de practica tienen la longitud y seccion reales de los
elementos de ejemplo, pero no representan una identificacion automatica del
objeto fisico. La geometria virtual y sus diagramas ocupan exactamente el
tramo elegido entre I y J. El panel distingue el **largo visual marcado**
del **largo original del modelo OpenSees**. Si no son iguales, la longitud
del dibujo se ajusta para encajar entre los puntos; los esfuerzos y el
elementTag siguen siendo los datos originales, sin reanalisis estructural.
Si desactivas `COLOCACION LIBRE`, la mira intentara primero detectar una
superficie; si no la encuentra, volvera a usar la profundidad libre.
Al desplazarse la camara simulada, el anchor conserva la superposicion en el
espacio XR mientras el seguimiento permanezca activo. Si se pierde tracking,
el elemento se oculta hasta recuperarlo.

Los diagramas usan 41 muestras de `UnityData.TryGetSectionForces`. Los valores
son los exportados por OpenSees; solo se normaliza la altura grafica del
diagrama. La calibracion no modifica cargas, resultados ni deformaciones.

## Telefono

El proyecto declara ARCore 6.6.2 para Android. Se requiere Android Build
Support y un telefono compatible con ARCore. En el lugar real hay que
identificar correctamente el elemento y sus nodos I/J, ajustar manualmente
la profundidad cuando no hay superficie detectada, y validar la precision del
anchor caminando alrededor. La colocacion manual no es una certificacion de
precision metrologica. No hay prueba
fisica en telefono todavia. iPhone requeriria configurar ARKit en macOS y
validarlo por separado.
