# Simulación sísmica — fase 1 (2026-10-05)

Rama: `codex/simulacion-sismica-transient`. Módulo independiente: no altera
los scripts ni los archivos de resultados estáticos existentes.

## Análisis

- Snapshot de P1L4: 490 nodos analíticos exportados, 808 elementos analíticos;
  se visualizan 444 vigas, 124 columnas y 75 barras equivalentes de muro.
- Registro El Centro 1940 NS, versión Peknold oficial de OpenSees, 1559 muestras
  de 0.02 s. Se antepone una muestra nula, declarado en metadata; cola de 2 s.
- Modelo elástico lineal, apoyos empotrados y diafragmas del modelo existente.
- Masas nodales: peso tributario G+0.5Q más peso propio estructural.
  Peso total 70106.538551 kN; masa equivalente total 7148.877400 kN·s²/m.
  De ese peso, 1232.1625 kN corresponde a nodos restringidos.
- Periodos iniciales: 0.612671, 0.584805 y 0.408678 s.
- Rayleigh: 5% en modos 1 y 3; los otros modos no tienen todos 5%.
- Gravedad G+0.5Q constante, excitación uniforme en X, Newmark 0.5/0.25.
- Cuatro corridas completas, 1660 estados cada una, dt=0.02 s y 33.18 s.

Máximos **totales relativos al terreno, incluyendo gravedad**; no son daño:

| Intensidad | PGA aplicada [g] | Máximo real [mm] | Nodo | Tiempo [s] |
|---|---:|---:|---:|---:|
| 0.25x | 0.07970 | 25.850 | 148 | 2.18 |
| 0.50x | 0.15941 | 49.433 | 148 | 2.18 |
| 1.00x | 0.31882 | 96.752 | 148 | 2.18 |
| 1.50x | 0.47823 | 144.108 | 148 | 2.18 |

## Pruebas

- PASS: conservación de pesos/masas, equilibrio gravitacional y apoyos fijos.
- PASS: registros completos, tags únicos, hashes, tiempos, valores finitos y máximos.
- PASS: escalamiento de U y fuerzas con intensidad, descontando la gravedad.
- PASS: entrada de aceleración nula conserva la respuesta gravitacional.
- PASS inicial: dt=0.01 frente a 0.02 en primeros 4 s; diferencia máxima
  de desplazamiento 0.002678 m (2.923% respecto al máximo refinado).
  No sustituye la revisión del paso temporal del registro completo y de fuerzas.
- PASS: regresión existente de corrección de las 444 vigas y conservación de cargas.
- Unity: compilación y reproducción en Play comprobadas; selección de viga
  y columna, cambio de intensidad, timeline y escala gráfica.
- Columna C5006 / ID 347, 0.25x, muestra t=3.04 s: N=-548.684 kN;
  extremo I Vy=0.632932, Vz=56.226139 kN, My=-94.896652, Mz=-2.763057 kN·m.
  Los valores del panel coinciden con el binario. Al cambiar escala 50x a 153x,
  los valores estructurales permanecieron iguales.
- Al cerrar el panel se recupera el viewer estático y su geometría.
- PASS: botón EJECUTAR OPENSEES probado desde Unity, corrida completa Y/1.00x
  y carga automática de su respuesta. Máximo total 93.974 mm, nodo 720, t=2.48 s.
- Console al terminar la prueba: 0 errores y 0 advertencias visibles.
  Vistas ISO/TOP/FRONT/RIGHT siguen disponibles durante la reproducción.

## Pendiente

Validar capacidades, interacción biaxial y armaduras de vigas antes del
DamageEvaluator. No se generan fracturas, grietas ni colapso. Muros:
representación analítica equivalente, sin esfuerzos shell ficticios.
No hay todavía diagramas dinámicos interiores ni DCR, ranking de daño,
gráficos de historia o cámara de primera persona. AR/VR/móviles no validados.
Las respuestas binarias y la ruta local de Python no se versionan.

Instrucciones completas: `P1L4/seismic/README.md`.
