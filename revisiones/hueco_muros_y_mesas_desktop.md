# Mesas y abertura de muros — Unity de escritorio

## Cambios

- Las tres mesas interiores de la galería de cafetería pasan a X=1,88, cerca de los accesos y lejos del muro de servicio. Conservan su separación de 3,40 m y sus sillas.
- Se modifica la abertura entre los muros 41–45 y 51–55, desde el subterráneo hasta el último piso (bases Unity Y=-4, 0, 4, 8 y 12).
- La abertura pasa de Unity Z=[-1,10; 1,30] a [0,40; 2,80]: desplazamiento +1,50 m y ancho constante de 2,40 m. Unity +Z corresponde a +Y del modelo estructural.
- Los extremos exteriores de los muros se conservan en Z=-4,045 y Z=8,90. El muro del lado negativo se amplía de 2,945 a 4,445 m; el positivo se reduce de 7,60 a 6,10 m. No se trasladan los paneles completos.
- Se conserva la columna real E1_233, así como las vigas y columnas originales. Se actualizan centroides, secciones y brazos rígidos de los muros. Los apoyos de los muros modificados siguen en el subterráneo, a la misma cota y con las mismas restricciones, bajo sus nuevos centroides.

## Modelo y resultados

El generador `P1L4/actualizar_hueco_muros_desktop.py` produce `P1L4/desktop_model/estructura_p1l4_desktop.json`. El visor y el recálculo de escritorio usan ese modelo separado. Los resultados sísmicos se guardan en `P1L4/seismic/desktop_results`.

Se recalcularon G, Q, EX, EY, C1, C2 y C3, incluyendo las curvas P-M correspondientes a las nuevas dimensiones de los muros. También se recalcularon los ocho casos de El Centro: X/Y con intensidades 0,25; 0,50; 1,00 y 1,50.

Los archivos de modelo usados por las aplicaciones móviles y el ZIP de Xcode no se actualizan con esta modificación.

## Verificación

- Unity compiló y comprobó las mesas, los cinco huecos, sus extremos y la columna E1_233; además pasó la comprobación del ambiente y la cafetería.
- `P1L4/tests/test_hueco_muros_desktop.py`: geometría de huecos, conectividad original de vigas/columnas, cotas de apoyos, equilibrio estático y superposición. Error máximo de superposición: 1,403e-8.
- `P1L4/tests/validar_sismo_hueco_desktop.py`: ocho respuestas completas de 1660 estados, hashes del modelo y binarios, masas, equilibrio gravitacional, apoyos, intensidades y desplazamientos. Informe: `revisiones/validacion_sismo_hueco_desktop.json`.
- La comparación de escalado sísmico se hace en float64 y contempla el error de redondeo de los valores almacenados en float32, mediante una cota por valor de media ULP. La tolerancia original permanece como base y el comportamiento por defecto del validador no cambia.

La comprobación automática de geometría no sustituye la revisión visual final del espacio en Unity.
