# Barandas y acceso de terraza Y=4 — escritorio

- Barandas cerradas de color naranjo salmón sobre E1_208, E1_206, E1_221.1 y E1_221.2. Altura visual 1,05 m, paneles opacos de 0,10 m de espesor con postes y pasamanos.
- Barandas únicamente en el lado exterior de las escaleras E1_207–E1_218 y E1_220–terraza Y=4, siguiendo las vigas y con conexiones en los descansos. El lado junto al edificio queda libre.
- Ampliación de pasto desde el borde anterior de la terraza hasta X=30, columna E1_280, limitada al lado de los voladizos (Z menor que -7,25). La otra cara de la terraza conserva su contorno.
- Acceso desde la ampliación hacia la cafetería, descendiendo hacia -X desde Y=4,03 hasta Y=0,03 en 12 m. Ocupa 9,30 m del ancho exterior: 1,80 m con 24 peldaños normales y 7,50 m con ocho gradas rectas más grandes. Son franjas contiguas, sin curvatura, con apoyo visual hasta el suelo.
- Los postes de las barandas del nuevo acceso apoyan sobre los peldaños, manteniendo el pasamanos inclinado.
- En el acceso inferior se conservan las barandas de los dos bordes exteriores y se elimina la división entre peldaños normales y gradas.
- La llegada de la escalera E1_220 se acorta hasta X=30, alineada con E1_281. Su panel exterior se regenera siguiendo la nueva longitud.
- El control **Escaleras / barandas** oculta escaleras y barandas. **Terraza Y=4** oculta el pasto original y su ampliación.

Todos los objetos son decorativos, sin `elementTag`, `ElementSelectable`, colliders, masas ni cargas. No se modifica el JSON del modelo ni se recalcula la estructura para añadirlos. La implementación se activa solo en el diseño de escritorio; no se actualizan iPhone, Android ni el ZIP de Xcode.

Validación: compilación y creación de escena con `DesktopCafeValidation.ValidateBatch`; comprobación de las cuatro vigas, cuatro franjas de escalera, límites de ampliación, dimensiones del acceso, ausencia de componentes estructurales y controles de ocultación. Vista generada en `P1L4/unity_visualizador/Logs/desktop-terrace-stairs-preview.png`.
