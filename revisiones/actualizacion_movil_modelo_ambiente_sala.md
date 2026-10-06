# Actualización de Android e iOS — 6 de octubre de 2026

Ambos proyectos móviles reciben el mismo modelo actualizado de 803 elementos: eje central de E2 alineado con E1, vigas y losas ajustadas, abertura de muros corregida y muros 41–45 terminando en la cara de la columna. Se incorporan los resultados estáticos recalculados y las ocho respuestas sísmicas completas del modelo vigente; modelo, catálogo y respuestas coinciden por hash.

En AR se mantienen los controles móviles de colocación, ajuste fino, varios elementos y diagramas, ahora con la geometría y resultados actualizados. No se añaden muros, muebles ni terreno a la colocación de elementos individuales.

En VR se incorporan:

- Cafetería en Y=0, servicio junto a las columnas correspondientes y distribución de sus 29 mesas con sillas y circulación separadas.
- Plataformas, caminos con pendiente, árboles ajustados, cerros con espacio libre, escaleras y barandas actualizados.
- Ventanas y puertas revisadas, techo exterior de cafetería ajustado al voladizo.
- Sala de cemento visual, puerta sobre E1_8, cuatro mesas y laptops, imagen enviada por el usuario en las cuatro pantallas, y unión continua entre muros sobre E1_38/E1_9.

La sala recibe la capa, shader estereoscópico y visibilidad del ambiente VR. Las imágenes de las laptops se conservan al convertir sus materiales. No se portan controles WASD, salto ni gravedad de PC; el recorrido sigue utilizando la mirada y el selector de pisos del teléfono.

Se conservaron exactamente los controladores AR/cámara propios de Android e iOS. Los códigos que calculan con OpenSees, los materiales y las combinaciones no se modificaron para esta adaptación. Los elementos decorativos no añaden cargas, masas ni IDs al modelo.

## Comprobaciones

- Unity Android e iOS: PASS de colocación y anchors AR, signos de momentos, superposición visual de combinaciones, varios elementos, selección y ajustes.
- Ocho casos sísmicos completos por aplicación: hashes, IDs, nodos, fuerzas y separación de escala visual.
- VR: ambiente actualizado, sala, cuatro pantallas con textura y shader estereoscópico; selección, reproducción sísmica, deformación, demandas, capacidades y fisuras; recuperación del estado estático y vuelta a AR.
- Compilación de runtime C# con los símbolos nativos `UNITY_ANDROID` y `UNITY_IOS`: PASS.

Evidencia: `actualizacion_movil_android_unity.log`, `actualizacion_movil_ios_unity.log` y `actualizacion_runtime_movil.log`. Estas verificaciones en el computador no son una prueba física de los nuevos builds en los teléfonos.

Android queda actualizado en `P1L4/unity_visualizador`. Este editor no tiene Android Build Support/SDK/NDK/OpenJDK, por lo que no genera un nuevo APK.

La copia iOS está en `P1L4/unity_visualizador_ios`; se exportó a Xcode con versión de compilación 14. Único entregable: `Entregables/MCOC_iOS_Xcode_20261006_063239_329_0458f7.zip`, 594,3 MiB. Se verificaron CRC de todas las entradas, proyecto Xcode, versión, descripción de cámara, permisos ejecutables, modelo exacto, ocho metadatos y ocho binarios exactos, y presencia de la textura de las laptops. Evidencia: `validacion_zip_ios.json`. El ZIP anterior se retiró después de validar el nuevo.

Copia de las fuentes y recursos móviles previos: `punto_inicio_actualizacion_movil_20261006/fuentes_y_datos_previos.zip`.
