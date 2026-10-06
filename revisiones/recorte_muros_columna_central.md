# Muros 41–45: término en la cara de la columna central

Los muros del mismo lado que el muro 42 se recortaron hasta la cara negativa de la columna central, incluida E1_233. Se aplicó a los muros 41, 42, 43, 44 y 45: subterráneo y cuatro tramos superiores.

La columna tiene sección de 0,70 × 0,70 m, centrada en Z=0 de Unity. El extremo del muro estaba en Z=+0,40 y atravesaba la columna; ahora termina en Z=−0,35. La longitud pasa de 4,445 a 3,695 m. Se conserva el extremo exterior Z=−4,045 y la posición de los muros 51–55, cuyo inicio sigue en Z=2,80. El paso libre entre la cara positiva de la columna y el muro opuesto queda en 2,45 m.

Se actualizaron la longitud de la sección de columna ancha, el centroide, el empotramiento del subterráneo y los brazos rígidos. La conexión analítica alcanza el eje de la columna; el volumen visible termina en su cara. Las columnas, vigas, losas y alineación del edificio 2 no cambiaron.

Los códigos de cálculo, las combinaciones y las propiedades de los materiales se conservaron. Los esfuerzos, desplazamientos, demandas de muros y curvas asociadas a la nueva longitud se obtienen nuevamente con las funciones existentes; no se reutilizan los resultados anteriores del muro más largo.

Alcance: modelo de PC. Recursos móviles y proyecto iOS conservados. Copia previa del modelo alineado en `punto_inicio_recorte_muro_20261006/modelo_alineado_previo.zip`; las respuestas sísmicas del modelo alineado anterior permanecen en `alineacion_e2_trabajo/desktop_results`.

Verificaciones: `validacion_recorte_muro.json`, `test_hueco_muros_desktop.py`, `test_alineacion_e2_desktop.py` y `recorte_muro_recalculo.log`. Los siete casos estáticos convergieron; máximo desbalance 1,54×10⁻⁸ kN y máxima diferencia de superposición 6,59×10⁻⁹ kN o kN·m.

Unity compiló y verificó el límite del volumen renderizado de los cinco muros en Z=−0,35, además de las regresiones de cafetería, aberturas, ambiente, escaleras y barandas (`recorte_muro_unity.log`). Pasaron los ocho casos sísmicos recalculados para X/Y e intensidades 0,25/0,50/1,00/1,50, con hashes del modelo y archivos binarios, masas, gravedad, apoyos, períodos y proporcionalidad verificados (`validacion_sismo_recorte_muro.json`).
