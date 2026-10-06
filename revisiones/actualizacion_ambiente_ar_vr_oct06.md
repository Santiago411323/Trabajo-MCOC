# Actualización de AR y VR — 6 de octubre de 2026

Se actualizaron ambos proyectos Unity: `P1L4/unity_visualizador` (Android/PC) y `P1L4/unity_visualizador_ios` (iPhone). Las aplicaciones comparten el modelo geométrico actual, su catálogo de losas, las nueve unidades LRFD y los ocho registros sísmicos completos X/Y con 1660 cuadros por registro.

El modelo incorporado tiene SHA-256 `73889f1c38277c9f7943e11323ffecc0885aa1e20ede737886e602e4a66f60f8`. Incluye la corrección de las familias de los muros 33 y 38 en todos sus niveles, incluido el subterráneo. Los resultados proceden de los análisis ya realizados sobre esta geometría; no se modificaron los algoritmos de cálculo.

En VR se incorporaron las plataformas de cemento, vereda y estacionamiento con pendiente continua, tabiques visuales del piso Y=4, cinco habitaciones con puertas y mobiliario (10 mesas y 28 sillas), la puerta de la sala de computadores, la escalera lateral de 3 m de ancho orientada hacia la cafetería y terminada en X de E1_288, sus barandas y el cierre vertical de piedra de la plataforma. Se conservan las cuatro laptops con su imagen. Los elementos del ambiente no tienen IDs estructurales ni alteran cargas o resultados.

Los tabiques y su mobiliario utilizan el shader estereoscópico del ambiente, participan en el botón de ocultar/mostrar ambiente y quedan excluidos del coloreado estructural. AR utiliza los mismos IDs y resultados actualizados para los elementos y sectores colocados.

No se portaron WASD, salto o skate de escritorio. No se cambiaron la cámara, permisos ni orientación del teléfono.

## Verificación

- Empaquetador LRFD: nueve unidades, equilibrio y hash del modelo iguales en ambos proyectos.
- Empaquetador sísmico: ocho casos completos, metadatos/binarios y hash verificados; 350,4 MiB comprimidos por aplicación.
- `MobileApplicationValidation.ValidateBatch`: comprueba AR, signos de momento, sectores, 22 escenarios LRFD contra OpenSees, ocho registros sísmicos, selección/deformación/resultados/daño en VR, retorno a AR y restauración de resultados estáticos.
- `MobileEnvironmentRevisionValidation`: comprueba posición de los muros, plataformas, escaleras, mobiliario, puerta de computadores, shader/layer VR y elementos únicamente visuales. También se comprueba ocultar/restaurar los tabiques.

Los logs son `revisiones/ambiente_movil_android.log` y `revisiones/ambiente_movil_ios.log`. Estas pruebas corren en Unity; el movimiento y la imagen del dispositivo requieren una prueba física del nuevo build.

## Entrega

iPhone: build 16; proyecto Xcode exportado desde Windows y entregado como `P1L4/unity_visualizador_ios/Entregables/MCOC_iOS_Xcode_20261006_210635_093_78fb4a.zip` (597,8 MiB). SHA-256: `b5c19988f7f2c7d7a79d04a7f39f87b4e48e13fd513b13bc4a4e5857997d4917`. El verificador `revisiones/verificar_zip_ios.py` pasó CRC, proyecto Xcode, permiso de cámara, permisos de scripts y coincidencia exacta de los recursos incluidos. Solo se conserva este último ZIP validado.

Android: proyecto y recursos actualizados y validados en el editor. No se generó un APK porque este Unity no tiene instalado Android Build Support (SDK/NDK/OpenJDK). Para producirlo hace falta instalar ese módulo y compilar la escena AR usando la configuración existente de ARCore/Cardboard.

El punto de recuperación anterior a mover los muros se conserva intacto; no se creó otro respaldo.
