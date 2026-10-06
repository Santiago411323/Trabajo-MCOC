# Traslación de las familias de los muros 33 y 38 hacia Z positivo de Unity

Traslación autorizada y aplicada el 6 de octubre de 2026, con punto de recuperación previo. Corrección posterior: la familia del muro 33 se lleva al extremo Z positivo de la familia del muro 38 para orientar el hueco al pasillo. Se reutiliza el mismo respaldo; no se crea otro. El cierre vertical de piedra de la plataforma Y=4 es únicamente visual y se mantiene separado de este cambio estructural.

## Identificación

Coordenadas expresadas como (X, Y, Z) de Unity; corresponden a (x, z, y) en los archivos OpenSees.

| Referencia | Familia | Geometría previa al primer cambio en Unity | Longitud |
| --- | --- | --- | --- |
| Muro 33 | 31–35, `W_DPRIME_ELEVATOR_TOP_TO_CPRIME` | X=-10 a -12,645, Z=-4,045 | 2,645 m |
| Muro 38 | 36–40, `W_CPRIME_ELEVATOR_SIDE` | X=-12,645, Z=-4,045 a -1,100 | 2,945 m |

Ambas familias tienen cinco niveles: -4–0, 0–4, 4–8, 8–12 y 12–16 de Unity. Los muros 33 y 38 corresponden al tramo 4–8.

## Posición aplicada

La familia 31–35 (muro 33) queda en Z=+6,990 de Unity: se desplaza +2,945 m adicionales respecto a la primera traslación, o +11,035 m respecto al respaldo original. La familia 36–40 (muro 38) permanece entre Z=+4,045 y +6,990, sin otro desplazamiento. Así se cierra el lado de la ventana y queda abierto el lado del pasillo. Se conservan longitudes, orientación, espesores y alturas en los cinco niveles. No se invierte el edificio 2.

Se aplica la traslación con la corrección de posición solicitada. La alternativa de reflejarlos respecto a Z=0 no se utiliza.

## Ejecución propuesta

1. Guardar un punto de recuperación de la geometría y resultados actuales.
2. Revisar las uniones de ambos grupos con columnas, vigas, losas y los muros vecinos. En particular, el extremo X=-10 actualmente coincide en planta con la familia 41–45; mover solo los dos dibujos no preservaría esa unión. Definir las conexiones de la posición elegida sin modificar automáticamente otros muros.
3. Aplicar el cambio mediante la entrada geométrica de escritorio, manteniendo los IDs de los paños. Actualizar conjuntamente sus nodos, ejes analíticos equivalentes, brazos rígidos y apoyos de base afectados. Revisar orientación local y conectividad en los cinco niveles, incluido el subterráneo.
4. Revisar el hueco de ascensor y las losas alrededor del núcleo. Ajustar únicamente la geometría que realmente necesite la nueva posición; no ocupar el hueco anterior ni abrir otro sin acordar su correspondencia con el núcleo.
5. Regenerar las entradas y ejecutar los mismos programas OpenSees, sin cambiar sus fórmulas, combinaciones ni convenciones de diagramas. El cambio de posición estructural exige recalcular; los resultados anteriores no representarían la nueva geometría.
6. Verificar continuidad vertical, nodos conectados, apoyos, brazos rígidos, áreas de losas, reparto tributario y equilibrio de G/Q/EX/EY. Contrastar IDs y resultados exportados con lo que se muestra en Unity.

## Implementación y recuperación

- Entrada geométrica: `P1L4/mover_muros_positivos_desktop.py`, que reutiliza íntegros los programas existentes. El editor de secciones del visualizador de PC usa esta entrada para conservar la traslación en futuros recálculos.
- Las dos familias comparten esquina en X=-12,645, Z=6,990; su extremo en X=-10 conecta mediante brazos rígidos existentes/regenerados con la familia 51–55, que permanece en su posición. Se verifica la conexión analítica por nodo en cada piso.
- Se conservan exactamente las vigas, columnas, losas y huecos previos. Se trasladan dos familias de muros, no el ascensor completo; no se crea ni desplaza un hueco de losa sin mover todo el núcleo.
- Punto de recuperación: `revisiones/punto_inicio_muros_positivos_20261006/estado_previo.zip`, 209 archivos con CRC y SHA256 comprobados. Incluye el modelo previo, scripts existentes, configuración de secciones y respuestas sísmicas/LRFD previas.
- Para revertir, salir de Play en Unity y ejecutar `revisiones/restaurar_punto_muros_positivos.py --restaurar` con el Python configurado. Restaura los archivos del punto de recuperación; conserva los nuevos archivos de auditoría como registro. No se ejecuta automáticamente.
- Evidencias: `validacion_muros_positivos.json`, `validacion_sismo_muros_positivos.json`, `muros_positivos_analisis.log`, `muros_positivos_sismo_x.log`, `muros_positivos_sismo_y.log`, `muros_positivos_lrfd.log` y `muros_positivos_unity.log`.

Alcance: modelo de PC. Los programas de cálculo, convenciones de diagramas y recursos de las aplicaciones Android/iOS se conservan sin cambios; solo se regeneran los resultados de la geometría de escritorio.
