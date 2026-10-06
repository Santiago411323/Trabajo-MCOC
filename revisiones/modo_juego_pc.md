# Recorrido en primera persona — PC

La escalera de E1_220 ahora llega a X=30, alineada con E1_281. La baranda exterior cerrada acompaña la nueva longitud; no se mueve ninguna viga o columna estructural.

## Uso

En Unity, abrir el visualizador y pulsar **Play**. Usar el botón **Modo juego · primera persona (F)** de la esquina inferior derecha, o pulsar **F**. En el ejecutable de PC se usa directamente el mismo botón.

| Control | Acción |
|---|---|
| WASD | Caminar |
| Ratón | Mirar |
| Espacio | Saltar cuando está apoyado |
| Shift | Correr |
| Esc | Pausar el recorrido, liberar cursor y abrir menú; volver a continuar |
| R | Volver al inicio exterior |
| F | Volver al visualizador y a la cámara orbital |

El menú de pausa permite ir al inicio y cambiar entre los seis niveles disponibles, incluido el subterráneo. Esto permite acceder a pisos sin escaleras. El jugador empieza en el exterior junto a la cafetería. Si cae fuera del entorno, vuelve al inicio.

## Colisiones y aislamiento

`DesktopWalkthrough` usa un `CharacterController` de 1,75 m, radio 0,25 m, gravedad -22 m/s², velocidad 3,5 m/s al caminar y 6,5 m/s al correr, salto de 1 m y peldaño máximo de 0,42 m.

Se crea un grupo temporal de colisiones para vigas, columnas, muros, losas, terreno, caminos, plataformas, ventanas, muebles y escaleras. Las barandas cerradas bloquean el paso. Las hojas, señalética y figuras de público no son obstáculos. Las superficies interiores de recorrido coinciden con la terminación utilizada por los descansos; la cafetería y su galería mantienen su propia cota para permitir atravesar sus puertas.

Durante el juego se muestra el modelo completo y se desactiva la selección de resultados. Al salir se restauran la vista anterior, los filtros de visibilidad, la cámara, el cursor y el estado original de los colliders. Los objetos de recorrido no se exportan ni añaden masas, cargas, IDs o elementos a OpenSees. La implementación se activa solo para el visualizador de escritorio; no se actualizan las aplicaciones móviles ni el ZIP de Xcode.

## Verificación

- `DesktopWalkthroughValidation.ValidateBatch`: llegada a E1_281 y baranda asociada; colisiones temporales; acceso seguro a seis niveles; gravedad; salto y aterrizaje; subida por la escalera normal de Y=4 y ambas escaleras de los voladizos; espacio libre en ambas puertas de cafetería; bloqueo de barandas; pausa; restauración de cámara y colliders.
- `DesktopWalkthroughPlayValidation.ValidateBatch`: prueba dentro de Play de la escena real, inicialización automática, entrada en primera persona, exclusión de la cámara orbital, pausa y salida al visualizador.
- Capturas de revisión: `P1L4/unity_visualizador/Logs/desktop-walkthrough-interior.png` y `P1L4/unity_visualizador/Logs/desktop-terrace-stairs-preview.png`.

La verificación automática prueba el controlador y sus colisiones. El recorrido manual con teclado y ratón permite revisar la sensación de movimiento y detectar pasos estrechos adicionales del modelo.
