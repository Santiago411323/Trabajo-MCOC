# Simulación sísmica — Transient y chequeos del elemento seleccionado

Módulo independiente del análisis estático y de la carga móvil. OpenSees calcula
la respuesta; Unity reproduce sus estados, con interpolación exclusivamente visual.
Implementado para el proyecto **P1L4/unity_visualizador**, Editor Windows.

## Uso en Unity

1. Abrir `P1L4/unity_visualizador`, esperar compilación y pulsar Play.
2. En Capas, pulsar **SIMULACIÓN SÍSMICA — OPENSEES**.
3. Seleccionar registro, intensidad (0.25x/0.50x/1.00x/1.50x) y dirección X/Y.
4. Pulsar **CARGAR CALCULADO** para una respuesta existente o **EJECUTAR OPENSEES**.
5. Usar Play, Pause, Restart, timeline, velocidad, escala visual y Original tenue.
6. Seleccionar una barra deformada: se muestran ID, elementTag y N/Vy/Vz/My/Mz
   en sus extremos para la muestra temporal calculada. Amarillo indica selección.
7. Cerrar devuelve la geometría y los resultados estáticos del viewer.

Cambiar intensidad carga otra corrida: **no multiplica los desplazamientos almacenados**.
La escala visual sí multiplica las posiciones dibujadas, y los números permanecen reales.
La velocidad de reproducción no cambia el paso del análisis ni la aceleración aplicada.
Mientras está activa se suspende la consulta estática C1/C2/C3, para evitar presentar
sus resultados como si fueran sísmicos. Los resultados existentes no se sobrescriben.

## Python y ejecución

Usar Python 3.12 de 64 bits con OpenSeesPy compatible. El `.venv` actual contiene
los paquetes, pero su ejecutable original ya no existe en este computador. Se utiliza
un Python 3.12 compatible configurado en `runtime.local.json` (archivo local, ignorado
por Git). El módulo puede reutilizar `.venv/Lib/site-packages` si faltan dependencias.
Cada compañero puede configurar su propio ejecutable:

```json
{ "executable": "C:/ruta/a/python.exe" }
```

Desde la raíz, con ese Python:

```powershell
python P1L4/seismic/transient_analysis.py --all-intensities
python P1L4/seismic/transient_analysis.py --intensity 1.00 --direction Y
python P1L4/seismic/validate_transient.py
```

Las corridas se guardan en `P1L4/seismic/results` (ignorado por Git; regenerable).
Para cambiar dt, `--substeps 2` divide por dos el dt del acelerograma sin alterar
sus muestras. `--max-seconds` sirve para pruebas parciales: se declara explícitamente
`truncated=true`; no usar esas pruebas como evaluación del registro completo.

## Hipótesis estructurales

- Modelo elástico lineal 3D: geometría corregida del JSON del viewer, seis GDL por nodo.
- Masas nodales traslacionales: G de losas + 0.5Q + PP de columnas, muros y alma de vigas.
  No se agregan masas de elemento ni masas ficticias de brazos rígidos.
- Peso en nodos empotrados documentado aparte; no participa como masa libre.
- Diafragmas y apoyos del constructor existente; se rechazan anclajes automáticos.
- Gravedad G+0.5Q se equilibra y mantiene constante antes de aplicar el acelerograma.
- Seis modos; amortiguamiento Rayleigh por masa y rigidez inicial, 5% en modos 1 y 3.
  El amortiguamiento efectivo de los demás modos se exporta. Esta hipótesis requiere
  revisión para un uso normativo; no es una medición experimental.
- Excitación uniforme horizontal X o Y; no incluye componente vertical, suelo flexible,
  movimiento diferencial de apoyos ni torsión accidental pseudoestática agregada.
- Newmark (gamma=0.5, beta=0.25), solver UmfPack y algoritmo Linear.
- Registro El Centro 1940 N/S, versión Peknold del repositorio oficial OpenSees:
  https://github.com/OpenSees/OpenSees/blob/master/EXAMPLES/verification/elCentro.at2
- Entrada AT2: NPTS, DT y unidades declaradas (`g`, `m/s2` o `cm/s2`). No se adivinan
  unidades. Se antepone una muestra nula, desplazando el registro DT; se declara
  `recordShift_s`. No se filtra ni corrige la línea base adicionalmente. Cola libre: 2 s.
- Se usa la misma componente para ensayos X o Y. No se afirma que formen un par
  bidireccional medido. Para otro registro, agregar su archivo y procedencia al catálogo.

## Contrato de resultados

JSON con identificadores, coordenadas originales, metadatos, hash del modelo/registro,
masas, periodos, equilibrio de gravedad, PGA y máximo real. Binario asociado:

| Parte | Contenido |
|---|---|
| Cabecera, 20 bytes | ASCII `MCOCSIS1`, int32 cantidad de nodos, elementos y pasos |
| Por paso, float32 little endian | time, Ux/Uy/Uz por nodo, 12 acciones locales por elemento |

`nodeTags` y `members` del JSON definen el orden. Se exporta cada paso convergido,
incluido t=0, sin reducir la frecuencia de muestreo para el viewer.
Los desplazamientos son **totales, relativos al terreno e incluyen gravedad**.
Las acciones son **totales resistentes** (`localForce`), con gravedad; no son
fuerzas nodales con inercia o amortiguamiento añadidos.

N: tracción positiva (`-FxI`, `FxJ`); Vy/Vz/My/Mz: cara I positiva y acciones J
con signo invertido. No se reconstruye todavía un diagrama dinámico interior del vano.
Unity valida hash, tamaño, contrato, pasos, nodos, secciones e identificadores antes
de reproducir. Nunca sustituye una respuesta faltante por cero.

Se dibujan todas las barras físicas analíticas (vigas, columnas y muros equivalentes).
Los brazos rígidos permanecen en el cálculo, sin dibujarse como piezas físicas.
Los muros se representan mediante su sección equivalente; no se inventa una
deformación shell de los paneles decorativos. La posición de cada nodo se comparte
exactamente entre los elementos conectados.

## Validación y límites

`validate_transient.py` verifica conservación de pesos, equilibrio de gravedad,
restricción de apoyos, hashes, muestras finitas, identificación de máximos,
escalamiento de las cuatro intensidades descontando gravedad, entrada nula
y comparación de dt=0.02 contra dt=0.01 en los primeros 4 s del registro.
Eso valida esta implementación, no certifica el modelo ni un diseño sísmico.

Los chequeos siguientes usan las capacidades exportadas que sí existen y una
estimación elástica de fisuración. No constituyen un modelo de daño no lineal.
El edificio permanece elástico aunque una demanda exceda una capacidad nominal.

## Fisuración y capacidad del elemento seleccionado

En el panel sísmico seleccionar por clic, ID numérico o elementTag. Se añade
el bloque de estado en el mismo panel, con DCR actual, máximo hasta el instante,
demanda/capacidad y primeros instantes de fisuración y excedencia.

- **VER FISURACIÓN 3D** activa trazos sobre las caras del elemento seleccionado.
- **ANIMAR FISURACIÓN** hace crecer los trazos suavemente y enfoca la cámara.
  Si aún no se alcanzó el primer umbral, va a la primera muestra correspondiente.
- **IR A PRIMERA FISURA** y **IR A EXCEDENCIA** pausan en la muestra real del registro.
- **ENFOCAR** acerca la cámara al elemento, después de actualizar su posición.
- Retroceder la timeline usa solo los máximos anteriores a ese tiempo. Replay
  no conserva grietas de eventos futuros. Cambiar elemento/caso limpia la ilustración.

### Criterios y alcance

`SeismicDamageEvaluator.cs` es independiente de la animación:

1. Recupera esfuerzos N/My/Mz a partir de las acciones locales reales. El modelo
   tiene masas nodales, masa de elemento cero y cargas distribuidas uniformes de
   gravedad. Verifica equilibrio local antes de reconstruir el vano.
2. Evalúa 41 secciones, incluidos I/J. Estima tensión máxima en sección bruta:
   `sigma_t = max(0, N/A + |My|/Sy + |Mz|/Sz)`.
3. Estima inicio de fisuración con el criterio del proyecto para hormigón normal:
   `fr = 0.63 sqrt(fc)` en MPa, lambda=1. Es una estimación de sección no agrietada,
   no una salida de deformaciones/fisuras de materiales no lineales de OpenSees.
4. Donde existe curva P-M compatible con dimensiones, material y armadura exportada,
   interpola Mcap(P), conservando polos axiales y rechazando P fuera de la envolvente.
   Evalúa compresión/tracción axial y la flexión disponible. No sustituye capacidad
   desconocida por cero, As mínima o una sección diferente.
5. Solo se reutiliza una curva en ambos ejes si la sección y las posiciones de
   barras son invariantes al girar 90 grados. **La columna actual de 18 barras
   (5 arriba, 5 abajo, 4 interiores por lado) NO cumple esa condición**; se chequea
   únicamente P–My con su curva exportada. No es una superficie de interacción
   biaxial. Muros: solo su eje fuerte en el plano.
   Armaduras superior/inferior asimétricas sin curvas por signo se marcan no
   evaluadas, en vez de reflejar automáticamente una curva positiva.
6. Vigas sin armadura/capacidad completa: **CAPACIDAD ÚLTIMA NO DISPONIBLE**. Sí se
   puede estimar fisuración usando dimensiones y fc conocidas. Nunca se afirma falla última.
7. Estados de inspección: NORMAL (<0.60), HIGH_DEMAND (>=0.60), NEAR_CAPACITY (>=0.85),
   CAPACITY_EXCEEDED (>1, con tolerancia 1e-6). Estos niveles son umbrales de presentación,
   no grados de daño calibrados experimentalmente. NORMAL no significa que el edificio
   cumpla todos los criterios. Corte, torsión, biaxialidad, fatiga y estabilidad no evaluados.
8. Curvas de muros generadas con armadura supuesta conservan esa advertencia.

La ilustración dibuja nueve estaciones en cuatro caras, con activación por demanda
de tracción y crecimiento dentro de la región de tracción elástica estimada. Los
trazos conservan máximos hasta el instante mostrado. Su zigzag, grosor y velocidad
son gráficos: **no predicen ancho, espaciamiento, trayectoria real o daño residual**.
Una banda roja marca excedencia del criterio P-M; no representa una sección rota.
No se genera daño aleatorio, caída de piezas, fragmentación ni pérdida de rigidez.
Si se detecta fisuración en t=0, se declara **gravedad G+0.5Q** como estado inicial.

### Pruebas del evaluador

```powershell
powershell -File P1L4/seismic/validate_damage.ps1
```

Requiere el SDK .NET 10 para el ejecutable de pruebas, no para Unity. Compila el
código numérico real del viewer junto a `P1L4/tests/SeismicDamageChecks.cs`.
Comprueba soluciones manuales, unidades, signos de caras, polos axiales, capacidad
ausente, eventos temporales, búsqueda hacia atrás y equilibrio de las respuestas
Transient reales. Se conservan aparte las pruebas del análisis dinámico de fase 1.

No se validó reproducción en teléfonos ni VR/AR. Los resultados se leen del
disco del computador; integrar distribución móvil de respuestas requiere otra etapa.
