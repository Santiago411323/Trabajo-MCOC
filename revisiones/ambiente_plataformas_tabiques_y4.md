# Ambiente del visualizador de PC: plataformas, vereda, escalera y tabiques

Cambios del 6 de octubre de 2026. Se mantienen separados de la geometría analítica: no se editan nodos, elementos, secciones, cargas, resultados ni programas de cálculo OpenSees.

## Estacionamiento y plataformas

- Vereda única, con malla continua y una pendiente que enlaza los tres niveles próximos a las plataformas.
- La altura combina suavemente el perfil superior de las plataformas a lo largo de X. Los bordes compartidos de las franjas de estacionamiento usan la misma función de altura.
- El terreno general queda por debajo del pavimento para evitar que su malla lo atraviese. El estacionamiento usa una malla más fina junto a la nueva vereda.
- Las plataformas anteriormente de pasto, incluida Y=4 y su ampliación, tienen terminación de cemento. La cancha conserva su pasto.

## Escalera cerca de E1_288

- Lado Z positivo de Unity, opuesto a los voladizos.
- Parte de la plataforma Y=4 y desciende hacia X negativo hasta quedar alineada con E1_288 (X=30), sobre la plataforma exterior inferior.
- Ancho de 3 m; borde próximo separado 2 m de la fachada posterior. El eje de recorrido queda en Z=12,4.
- Los peldaños y descansos tienen cuerpo macizo hasta la base del terreno; no queda un vacío debajo.
- Tiene barandas salmones a ambos lados, orientadas con el recorrido de los escalones.
- Se cierra la abertura que se había añadido en el vidrio entre E1_289 y E1_301; se conservan las demás puertas.
- Se integra al control existente de escaleras y barandas.
- Un cierre vertical con material de piedra cubre la cara expuesta de la plataforma Y=4 detrás de la escalera, desde la cota del terreno inferior hasta Y=4. Tiene un rebaje bajo el acceso, cubierto por el cuerpo macizo de los peldaños, para mantener libre la subida. Se oculta junto con la terraza y no tiene ID estructural ni collider permanente. Verificación de subida y bajada: `muro_piedra_escalera.log` (PASS).

## Tabiques visuales, exclusivamente en Y=4

- Entre E1_281 y E1_285, hasta las caras de las columnas.
- Desde la cara del muro estructural 53, sobre Z=2,8, hasta la ventana de la fachada X=35 que contiene la puerta junto a E1_297. Es un tabique arquitectónico independiente y no una prolongación analítica del muro.
- Se deja libre el sector de acceso de los muros 28, 23 y 18.
- Sobre el eje central del edificio 1, entre las columnas del tramo Y=4–8, excepto el paño E1_285–E1_297 que se retira.
- Los paños y el dintel existentes de la sala de mesas se excluyen para no duplicarlos ni cerrar su puerta.
- Se pueden ocultar con **Ventanas / muros**. Siguen el filtro del piso correspondiente.

La retirada del paño E1_285–E1_297 y la extensión del tabique paralelo hasta la ventana se verifican en `ambiente_y4_tabique_ventana.log`; la puerta del voladizo se comprueba con `DesktopFacadeRevisionValidation` en `ambiente_y4_tabique_puerta.log`.

Los objetos visuales no tienen IDs estructurales ni colliders permanentes. El modo juego crea sus propias colisiones temporales para que las paredes, los escalones y el pavimento sean transitables o actúen como obstáculos físicos.

## Habitaciones y mobiliario en Y=4

- Tabiques sobre E1_41, E1_33, E1_31 y E1_28, desde el vidrio hasta el tabique perpendicular del pasillo. Se mantiene el mismo color cemento.
- Seis habitaciones nuevas: cuatro del lado Z positivo y dos del lado Z negativo junto a la sala existente de laptops.
- Cinco habitaciones tienen dos mesas y cuatro sillas cada una, con puertas abiertas hacia el pasillo. En el sector de los muros 18, 23 y 28 se retiran las dos mesas, las cuatro sillas y la puerta añadida; el acceso al núcleo sigue abierto.
- Las cuatro mesas y laptops existentes se conservan; se añaden dos sillas por mesa. Total añadido tras despejar el núcleo: 10 mesas y 28 sillas. Su acceso existente sobre E1_8 incorpora marco, manilla y puerta visible abierta hacia el pasillo.
- Se retira del tabique central únicamente el tramo de acceso frente al núcleo de los muros 8, 3 y 13, con margen lateral de 0,45 m. Los tres muros estructurales se conservan íntegros.
- Los nuevos objetos se ocultan con **Ventanas / muros** y siguen el filtro Y=4. No se modifican geometría analítica, cargas ni resultados.
- Verificación: `ambiente_y4_habitaciones.log`; imagen `ambiente_y4_habitaciones.png`.
- Ajuste de puerta de computadores y escalera ancha/maciza: `ambiente_escalera_ancha_puerta.log`; imagen `ambiente_escalera_ancha.png`.

## Evidencia

- `DesktopEnvironmentRevisionValidation`: continuidad del estacionamiento, pendiente de la vereda, ausencia de tierra atravesando el pavimento, cemento en todas las plataformas, tabiques solo en Y=4, unión al muro 53, pasos del núcleo, sala preservada, subida y bajada entre cafetería y plataforma Y=4, y vidrio cerrado en la antigua abertura de la escalera.
- `DesktopCafeValidation`: cafetería, mobiliario, muros estructurales originales, puertas, cerros, caminos y escaleras existentes.
- `DesktopSkateValidation`: movimiento, trucos y cruce entre las plataformas y el pavimento.
- `DesktopWalkthroughValidation`: seis pisos, subterraneo, gravedad, salto y accesos existentes.
- Las verificaciones terminaron en **PASS** en Unity 6000.6.0f1. Pendiente máxima muestreada de la vereda: 0,463 m/m, aproximadamente 25°.
- Registros: `ambiente_y4_unity.log`, `ambiente_y4_cafeteria.log`, `ambiente_y4_skate.log`, `ambiente_y4_recorrido.log`. La corrección del sentido de la escalera y el vidrio se verifica en `ambiente_y4_escalera_corregida.log` y `ambiente_y4_vidrio_corregido.log`.
- Imágenes: `ambiente_y4_vereda.png`, `ambiente_y4_escalera.png`.

Las aplicaciones móviles y el comprimido de Xcode no se reconstruyen en esta revisión del visualizador de PC.
