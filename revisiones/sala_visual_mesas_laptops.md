# Sala visual y distribución de mesas — 6 de octubre de 2026

Solo PC. Se mantienen intactos el modelo estructural, el catálogo de losas, los recursos móviles y el proyecto iOS.

- Ventanas del estilo existente entre E1_230/E1_243 y E1_255/E1_257. El segundo par incorpora la corrección de IDs del usuario.
- Sala en Y=4: lado X=0 entre E1_243 y E1_247; cierre sobre Z=0 desde E1_247 al encuentro con E1_38; lado X=7,51 siguiendo E1_38 hasta E1_255. Muros de cemento visual, entre caras de columnas y bajo las vigas superiores.
- Puerta de 1,30 × 2,20 m sobre E1_8, centrada en X=2,50, con altura libre desde el acabado del piso. Los muros y piso de la sala no son elementos OpenSees.
- Cuatro mesas en cuadrícula 2 × 2, cada una con una laptop modelada con base, teclado, touchpad, pantalla y superficie de imagen. Las cuatro pantallas usan la captura del visualizador de Unity enviada por el usuario, copiada sin modificaciones a `Assets/Resources/StudyRoomLaptopScreen.png`. Comparten el mismo material y una emisión de pantalla para que la imagen se vea incluso con poca iluminación en la habitación.
- Las 29 mesas de la cafetería se conservan. Sus ubicaciones consideran la extensión de las sillas, la separación entre mesas, columnas, muros, ventanas, escaleras y barandas; dejan libre el corredor que conecta las puertas. El cambio reemplaza la distribución anterior de mesas alineadas con las puertas.

La sala acompaña la visibilidad de las ventanas y el filtro del tramo estructural correspondiente. No incorpora IDs estructurales ni colliders permanentes; el modo juego crea temporalmente sus obstáculos de navegación, como para las otras decoraciones del proyecto.

Verificación con `DesktopStudyRoomValidation.ValidateBatch`: ventanas presentes, cuatro mesas equidistantes, cuatro laptops y pantallas, materiales, carácter decorativo, 29 mesas de cafetería con sillas separadas y circulación libre; puerta de la sala transitable y muros con obstáculos temporales en modo juego. Evidencia: `sala_laptops_unity.log`.
