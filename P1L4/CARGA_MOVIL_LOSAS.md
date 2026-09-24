# Carga móvil sobre superficies de losa

Proyecto: `P1L4/unity_visualizador`. Implementación para Unity Editor en Windows,
con Python/OpenSeesPy como proceso persistente sin ventana.

## Uso

1. Salir de Play, refrescar Assets y volver a Play.
2. Mostrar losas y seleccionar una. La persona aparece inmediatamente en el punto
   elegido, con cuerpo articulado, base amarilla y flecha roja vertical. El panel
   MOBILE LOAD — SLAB conserva esa losa aunque después se seleccione una viga o
   columna para observar sus resultados.
3. Definir P en kN. La persona se puede arrastrar directamente; PLACE permite
   colocarla otra vez y también existen sliders y coordenadas locales en metros.
4. MOVE TO POINT permite hacer click en un destino; PLAY avanza a Speed, PAUSE detiene y RESET
   vuelve a la posición inicial. ESC cancela colocación/movimiento. Las direcciones
   también se pueden elegir con las cuatro flechas dibujadas alrededor de la
   persona. La flecha activa se vuelve verde y produce marcha continua en +X,
   -X, +Y o -Y. Al alcanzar un borde compartido, la persona entra en la losa
   adyacente del mismo nivel, cambia el resaltado y recalcula reparto y resultados.
   Se detiene si encuentra un vacío, una separación, un borde sin continuidad o
   el final del edificio. No cambia automáticamente de piso.
5. Seleccionar una viga o columna: su panel permite My, Mz, Vy, Vz, N y torsión T.
   El tablero móvil se organiza en MOVIMIENTO, REPARTO y RESPUESTA. Esta última
   permite elegir el componente observado y Uz (mm, interpolado entre los
   desplazamientos de sus nodos), y muestra valor actual animado, rango global,
   elemento crítico, gráfico temporal y máximo absoluto encontrado. La deformada
   usa los desplazamientos nodales totales mediante el modo existente del viewer.
6. El coloreado global utiliza el valor en el centro de cada barra y la vista
   RESPUESTA presenta sus extremos como azul/rojo. No es un mapa de esfuerzos de losa.
7. Exportar historial CSV guarda tiempo, losa, posición local, P, elemento,
   componente, valor, unidades y combinación en Application.persistentDataPath.
   Sin elemento observado se registra la posición con valor vacío, nunca cero
   inventado. El máximo se separa por losa, elemento, componente y combinación.

## Modelo y transferencia

Las 226 losas exportadas son rectángulos de carga; no existen shells.
Los campos `openings` opcionales definen vacíos rectangulares en coordenadas
globales estructurales. Se rechazan puntos y segmentos que cruzan estos vacíos,
incluso cuando ambos extremos del recorrido son válidos. El JSON actual no trae
vacíos interiores explícitos: solo se conoce la superficie de cada panel.

Se reproduce la partición tributaria existente: cuando b/a > 2 se usan los dos
bordes largos, en otro caso regiones a 45 grados (borde más cercano). En las
fronteras se comparte la carga entre regiones empatadas. La carga se proyecta
sobre el borde y luego sobre la viga coincidente, transfiriéndose a sus nodos
I/J con pesos (1-t,t). Esto es una extensión puntual explícita de la regla
tributaria, aproximada mediante cargas nodales, no un análisis de placa ni una
carga interior de barra. No produce la curvatura local de una carga puntual
aplicada directamente dentro de una viga. No presupone empotramiento en sus extremos.

Se comprueban suma de cargas nodales descendentes = P y equilibrio de las
reacciones de cada respuesta unitaria. Tolerancia de transferencia:
max(1e-6 kN, P*1e-6). No se redistribuyen cargas a vigas cercanas cuando falta un
receptor geométricamente válido. El panel muestra ERROR y retira el incremento.

El proceso importa `P1L3/carga_viva_sismo.py` y utiliza su build_model, incluidos
sus apoyos y restricciones automáticas. Obtiene acciones locales de extremo y
desplazamientos nodales. Guarda respuestas unitarias por nodo receptor y las
superpone linealmente. La respuesta móvil se suma una sola vez a la combinación
G/Q/EX/EY seleccionada, con factor 1 para P. No cambia cargas permanentes ni los
JSON de resultados preexistentes. Active desactivado restaura la combinación base.

Las solicitudes tienen un intervalo mínimo de 0,15 s y distancia mínima de 5 mm,
con una sola solicitud en vuelo. Se muestran las coordenadas de la última
respuesta confirmada; si la persona ya avanzó aparece PENDIENTE. Se descartan
respuestas de otra selección/activación. Hay timeout de 30 s y botón Reintentar.

## Límites del modelo actual

- No hay Nxx/Nyy/Nxy, Mxx/Myy/Mxy, Qx/Qy, Uz de losa ni shell crítico.
- Los muros no tienen respuesta FE de carga móvil en este modelo. Sus demandas
  estimadas preexistentes no se actualizan como si fueran un análisis shell.
- Auditoría de los centros: 101 losas permiten transferencia; 125 no tienen
  todos los receptores necesarios en ese punto. No implica que todos los puntos
  de las primeras sean válidos ni que ningún punto de las restantes lo sea.
- Hay 233 centros de bordes tributarios sin viga coincidente. El catálogo
  `Assets/Resources/slab_load_surfaces.json` enumera cada borde, área y receptores
  en su centro. La asociación se vuelve a comprobar en la posición de la persona.
- Antes de habilitar todas las posiciones deben corregirse las correspondencias
  reales de losas y vigas del modelo. La implementación no modifica geometría,
  conectividad o apoyos para ocultar estas faltas.

## Propiedades gravitacionales

`exportar_superficies_carga.py` genera el catálogo por losa a partir de los perfiles
de origen: h=0,15 m, densidad 2500 kg/m3 convertida a peso unitario, adicionales
FLOOR=260, ROOF=200, ELEVATOR_ROOF=1500 kg/m2. Se conservan las diferencias entre
pisos/cubiertas/ascensores; no se redefine G. El panel muestra h, peso unitario,
adicional y q_G=h*gamma+adicional. Los receptores listados corresponden al centro
del borde, no a una garantía de cobertura completa. El espesor dibujado continúa
siendo un espesor de visualización.

## Python y validación

El panel permite indicar un ejecutable Python. En este equipo utiliza el runtime
disponible de Codex y los paquetes de `.venv/Lib/site-packages` del proyecto; en
otro equipo configurar Python 3.12 compatible con OpenSeesPy instalado. No se ha
empaquetado un ejecutable standalone del viewer ni Python para distribución.

Pruebas: `P1L4/tests/test_mobile_slab.py` (transferencia, vacíos, receptores
ausentes, conservación, contraste con análisis directo de los 462 elementos,
protocolo del worker). `verificar_fuerzas_unity.ps1` comprueba además límites de
superficie, cruce de vacíos estrechos, suma única del incremento y restauración.
Compilación con el compilador y referencias del proyecto Unity. La inspección
visual interactiva en Play queda pendiente; no se presenta como realizada.

La conectividad geométrica actual contiene 338 uniones entre las 226 superficies;
todas tienen al menos una vecina. Una unión exige mismo nivel, borde coincidente
dentro de 3 cm y solape transversal mayor que 3 cm; el contacto por una sola
esquina no permite el paso.
