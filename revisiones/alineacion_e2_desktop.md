# Alineación del edificio 2 con el edificio 1 — 6 de octubre de 2026

El eje interior del edificio 2 pasó de Z=1,65 a Z=0 en Unity, sin invertirlo. En el generador original corresponde a cambiar únicamente `grid_y['2']` de 8,90 a 7,25 antes de aplicar el desplazamiento de montaje Y=−7,25. Unity usa `(x,z,y)` respecto a OpenSees.

Se ajustaron las tres pilas de columnas C1004..C5004, C1005..C5005 y C1006..C5006, las vigas centrales y las longitudes de las transversales que llegan a ellas. Las coordenadas X, alturas y bordes exteriores Z=−7,25 y Z=8,90 se conservaron. Las divisiones de las vigas longitudinales coinciden con columnas o vigas transversales.

En la unión con E1 se reunieron E1_47, E1_95, E1_139, E1_183 y E1_225: ya no necesitan dos partes porque la viga central llega directamente al nodo del eje original. También desaparecieron cinco brazos auxiliares del encuentro anterior con el muro; las conexiones se reconstruyen con las funciones originales.

Se reconstruyeron los paños de losas: el catálogo conserva 169 losas y 68 cambiaron sus límites o sus identificadores al reconstruir los nuevos paños. El modelo pasa de 813 a 803 elementos analíticos. Se conservaron las aberturas previamente modificadas en los muros y las dos secciones editadas desde Unity, identificadas por sus `elementTag`.

## Cálculos y alcance

`alinear_eje_edificio2.py` prepara exclusivamente la geometría de PC. Reutiliza el generador original, sus especificaciones de vacíos y su función de áreas tributarias. `actualizar_hueco_muros_desktop.py` entrega esa geometría al exportador existente. Las nuevas longitudes, áreas y cargas tributarias son entradas actualizadas; los resultados se recalculan.

No se modificaron `P1L3/carga_viva_sismo.py`, `P1L4/exportar_resultados_unity.py`, `P1L4/slab_panels.py`, `edificio_2/modelo_python/geometry_data.py` ni `P1L4/seismic/transient_analysis.py`. Se conservaron materiales, combinaciones, fórmulas y configuración de análisis.

Unity carga el modelo y el catálogo de losas de `P1L4/desktop_model/`; los consumidores del catálogo solo cambian la fuente del archivo en PC. Los recursos de Android, el proyecto iOS y su comprimido Xcode no se actualizaron.

## Verificación

- Auditoría independiente: 15 tramos de columnas centrales alineados, dimensiones exteriores preservadas, ningún miembro físico duplicado, cortes centrales con unión real, geometría de muros y empotramientos conservada.
- Se compararon por SHA-256 211 archivos protegidos con la copia inicial, incluidos código de cálculo, recursos móviles y fuentes de iOS.
- G, Q, EX, EY y C1/C2/C3 convergieron. Máximo desbalance: 1,30×10⁻⁹ kN.
- Superposición de esfuerzos: diferencia máxima 9,74×10⁻⁹ kN o kN·m.
- Ocho respuestas sísmicas completas: X/Y, intensidades 0,25/0,50/1,00/1,50; verificados hashes, masas, gravedad, apoyos, períodos, máximos y proporcionalidad de la respuesta considerando la precisión de almacenamiento float32.
- Unity compiló y comprobó las posiciones renderizadas de los 15 tramos de columnas, la correspondencia del catálogo de losas y la carga de los 5.621 registros de esfuerzos actuales.
- Pasaron las regresiones del ambiente y del recorrido: cafetería, aberturas, caminos, árboles, cerros, escaleras, barandas, seis niveles incluido subterráneo, gravedad, salto, accesos y restauración de cámara. También pasó la prueba de entrada, pausa y salida del modo juego en Play de la escena real.

Evidencia numérica: `validacion_alineacion_e2.json`, `validacion_sismo_alineacion_e2.json`, `alineacion_e2_recalculo.log` y los registros sísmicos X/Y en esta carpeta. Evidencia de Unity: `alineacion_e2_unity_cafe.log`, `alineacion_e2_unity_recorrido.log` y `alineacion_e2_unity_play.log`.

## Punto de regreso

Antes de ejecutar el plan se guardó y verificó `punto_inicio_alineacion_e2_20261006/estado_previo.zip`: contiene 808 archivos del estado de trabajo, incluidos cambios anteriores sin commit y las respuestas sísmicas de escritorio previas. `manifest.json` guarda sus hashes y `git_status.txt` el estado de Git.

Para regresar a ese punto se deben restaurar los archivos afectados desde esta copia, conservar los trabajos previos sin commit y retirar únicamente los archivos incorporados por esta alineación. No se debe usar `git reset --hard`, porque el punto inicial incluye cambios que todavía no están en HEAD. La copia y la carpeta temporal de cálculo están excluidas de Git.
