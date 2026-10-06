# Skate en el modo juego de PC

Primera etapa: funcionamiento sobre el entorno existente, sin agregar rampas. La escena `StructureViewerScene` incorpora el skate automáticamente mediante `DesktopWalkthrough`.

## Uso

1. Abrir la escena y pulsar Play.
2. Pulsar **F** o el botón de modo juego para entrar caminando.
3. Sobre el suelo, pulsar **E** para subir al skate. Aparecen una tabla y un patinador provisionales, con cámara en tercera persona.

| Control | Acción sobre el skate |
| --- | --- |
| W | Impulsarse; al soltar conserva la inercia |
| S | Frenar hasta detenerse |
| A / D | Girar la tabla |
| Ratón | Mirar alrededor sin cambiar la dirección de movimiento |
| Espacio | Ollie conservando el movimiento horizontal |
| Q | Kickflip: salto con una vuelta de la tabla sobre su eje longitudinal |
| T | Shove-it 360: salto con una vuelta horizontal de la tabla |
| E | Bajarse y recuperar la primera persona; requiere estar en el suelo |
| R | Volver al inicio y detener el movimiento |
| Esc | Pausar y mostrar el menú, incluido el cambio de piso |
| F | Salir al visualizador |

Al caminar siguen funcionando WASD, ratón, salto con Espacio y correr con Shift.

## Funcionamiento

- Controlador independiente del movimiento a pie, usando la cápsula y el mundo temporal de colisiones existentes.
- Aceleración, resistencia al rodamiento, freno, límite de velocidad de 11 m/s y gravedad sobre pendientes.
- La dirección se corrige gradualmente al girar. Se conserva el movimiento durante el ollie.
- Q y T lanzan sus propios saltos desde el suelo. Cada truco gira la tabla durante el vuelo y vuelve a su orientación de rodamiento al aterrizar; no se pueden encadenar pulsaciones para repetir saltos en el aire.
- El campus de tierra enlaza con el borde delantero y los laterales de las plataformas mediante una transición de 30 m hacia su pendiente original. Se suaviza también el recorte del terreno bajo el edificio cerca del perímetro. La misma altura se usa para estacionamiento, vereda y árboles de esa zona; el campo deportivo y su paseo conservan su perfil dedicado.
- Choques con paredes eliminan la velocidad bloqueada. La cámara se retrae ante obstáculos.
- La tabla y el personaje son objetos visuales procedurales sin colliders propios. Al salir se liberan y se restauran la cámara y las colisiones del visualizador.
- No modifica geometría del edificio, secciones, cargas ni cálculos OpenSees. Las aplicaciones móviles no se reconstruyeron para esta etapa.

## Validación

Las tres verificaciones siguientes terminaron en **PASS** en Unity 6000.6.0f1 el 6 de octubre de 2026.

- `DesktopSkateValidation.ValidateBatch`: subir/bajar; tabla visible; cámara independiente; impulso; inercia; freno; giro; ollie y aterrizaje; pausa; pared; pendiente; cámara ante obstáculos; regreso al movimiento a pie y salida.
- `DesktopWalkthroughValidation.ValidateBatch`: seis pisos, subterraneo, gravedad, salto, escaleras, puertas, barandas y restauración del visualizador.
- `DesktopWalkthroughPlayValidation.ValidateBatch`: inicialización de la escena real en Play, subir al skate, impulsar, tercera persona, pausa, frenado y regreso a primera persona y visualizador.
- Registros: `skate_pc_unity.log`, `skate_pc_recorrido.log`, `skate_pc_play.log`.
- Vista del prototipo: `skate_pc_vista.png`.
- Actualización de trucos y terreno: **PASS** en `skate_trucos_terreno.log`, `skate_trucos_recorrido.log` y `skate_trucos_play.log`. El primero verifica giros de tabla, aterrizajes y conservación de velocidad, además de cruzar el borde real del pasto en ambos sentidos en las plataformas de tres alturas. El segundo comprueba escaleras, puertas, gravedad y pisos. El tercero comprueba ambos trucos en la escena real en Play. Imagen adicional: `skate_pc_kickflip.png`.

## Alcance de esta etapa

El personaje y la tabla son provisionales. Se implementan ollie, kickflip y shove-it 360; quedan para etapas posteriores las rampas, otros giros de trucos, grinds, puntuación, caídas animadas y controles por joystick. El modelo de movimiento es una aproximación de juego con CharacterController, no una simulación física de cuatro ruedas.
