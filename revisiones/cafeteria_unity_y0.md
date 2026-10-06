# Corrección exclusiva del modelo de Unity

La cafetería, su galería, mobiliario y terrazas quedan en Y=0. Los techos siguen esa cota y el filtro de piso usa `CIELO_1S`, correspondiente a Z=0 en el modelo estructural.

La plataforma inferior se prolonga hasta cubrir toda la cafetería y sus terrazas. La plataforma de Y=4 recupera su inicio original junto al edificio; se retiran su talud hacia −X, los bordes del talud y sus rocas. La conexión elevada de la cafetería se retira del modelo de escritorio; se conserva la escalera independiente E1_220 → terraza.

El proyecto de escritorio comparte fuentes con Android. `VisualCafe.UseDesktopLayout` activa esta revisión en el editor y en escritorio, y conserva la disposición móvil anterior al compilar Android/iOS. El proyecto iOS y el ZIP build 13 no se modificaron ni se generaron nuevos entregables para teléfonos.

PASS en Unity: altura Y=0 del pavimento, altura del techo, cobertura completa de la cafetería por la plataforma inferior, tamaño original de la plataforma Y=4 y ausencia de su talud/conector elevado. Evidencia: `P1L4/unity_visualizador/Logs/desktop-cafe-y0-validation.log`. Verificador: `DesktopCafeValidation.ValidateBatch`.

Cambios decorativos únicamente; no se modificaron nodos, elementos, cargas, datos OpenSees ni resultados.

## Ajuste posterior: árboles y camino

Se retiró por completo la plataforma elevada de pasto de Y=4 en escritorio, que ocultaba los árboles. La plataforma de Y=0 bajo la cafetería permanece. Se conserva la referencia de posición de la escalera existente.

Se agregaron tres tramos de camino decorativo de 2,4 m de ancho: paralelo a la fila de árboles junto a la cancha (separación de 4 m entre ejes), llegada bajo el voladizo de Y=4 y conexión por el exterior con la vereda del estacionamiento. El pavimento sigue las cotas del terreno/plataforma, con relleno lateral donde cambia la altura.

PASS: ausencia de la plataforma elevada, cafetería Y=0, existencia de los tres tramos, continuidad de las uniones y llegada bajo Y=4. Registro: `Logs/desktop-campus-path-validation.log`. Vista generada y revisada: `Logs/desktop-campus-path-preview.png`. Esta revisión sigue siendo exclusiva del modelo de escritorio; las aplicaciones y el ZIP no se actualizaron.

## Disposición corregida: cerro, paseo y plataforma Y=4

La plataforma de pasto Y=4 vuelve a estar presente con su tamaño original. El origen del saliente verde era la malla del cerro: ahora su borde tiene huella circular y se desvanece contra el terreno existente. Se excluyen sus triángulos de la zona del edificio, estacionamiento, cancha, graderías, árboles y paseo, conservando los catorce cerros de fondo.

El camino paralelo pasa detrás de los árboles, al lado opuesto de la cancha: su eje se desplaza 4 m hacia +X respecto de los troncos. La pendiente asciende linealmente desde Y=0 hasta Y=4; el pavimento se eleva 6 cm sobre ella. Los doce árboles se apoyan sobre esa misma pendiente. El camino llega a la plataforma Y=4 y conecta con la vereda del estacionamiento. Se rebaja la geometría del terreno donde podría cubrir los conectores.

PASS final en Unity: cafetería Y=0, plataforma Y=4 recuperada, continuidad de caminos, extremos del paseo Y≈0/Y≈4, árboles ajustados a la pendiente y catorce cerros sin triángulos dentro de la zona protegida. Evidencia: `Logs/desktop-ramp-hills-validation.log`. La vista `Logs/desktop-campus-path-preview.png` se regeneró y revisó visualmente. No se actualizaron aplicaciones de teléfono, ZIP ni datos estructurales.
