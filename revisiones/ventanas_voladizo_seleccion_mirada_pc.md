# Ventanas del voladizo y selección por mirada en modo juego

Se elimina únicamente el paño entre E1_271 y E1_259, en el tramo de Unity Y=12–16. Se agregan tres paños del mismo vidrio, montantes y marcos de la fachada: E1_259–E1_310, E1_271–E1_311 y E1_311–E1_310. Las columnas proyectadas se consideran solo para esos paños, sin reclasificar las ventanas vecinas como interiores. Las ventanas son visuales y no modifican geometría estructural, cargas, IDs ni resultados.

En el modo juego de PC, **1** (fila superior o teclado numérico) activa o desactiva la selección por mirada. Cada entrada al modo juego comienza con esta función desactivada. La mira central se vuelve amarilla al apuntar a una viga o columna; se selecciona el objeto original, conservando su ID, resaltado e inspector de resultados. La selección anterior permanece al mirar a un espacio vacío para poder consultar su información.

El rayo alcanza hasta 50 m y usa las colisiones temporales del recorrido: el primer muro, vidrio, losa u otro obstáculo bloquea la selección de lo que quede detrás. No se habilitan los colliders originales de selección mientras se camina. Las colisiones físicas existentes se mantienen.

**Esc** pausa la selección y libera el cursor para usar los resultados. Al desactivar la función se ocultan sus paneles y se recupera la selección previa. Al salir con **F** se restauran las cámaras, colliders y estados anteriores de los paneles. La tecla 1 conserva el atajo de axial fuera del modo juego, y se reserva para la mirada dentro de este.

Validación en Play Mode: `DesktopGazeFacadeValidation.ValidateBatch`, log `revisiones/ventanas_mirada_pc.log`, PASS. Comprueba la ventana retirada, las tres ventanas nuevas, los paños vecinos y de otros pisos preservados, selección de viga y columna, obstrucción por muro, pausa, paneles de resultados, aislamiento del atajo axial y restauración al salir/reingresar. No se recompilaron aplicaciones móviles ni se modificaron algoritmos o resultados OpenSees.
