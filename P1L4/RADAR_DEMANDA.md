# Radar de demanda

En Play: panel de capas → Activar radar de demanda. Está integrado en el
scroll del panel; la rueda dentro del panel no mueve el zoom del edificio.

## Lecturas

- D/C: P–My **uniaxial nominal**, con P=-N. No incorpora φ, interacción
  biaxial, corte, torsión ni detalles sísmicos. No es un indicador global
  de seguridad. Se excluye una sección asimétrica sin curvas por signo.
- M: √(My²+Mz²), kN·m. V: √(Vy²+Vz²), kN.
- N−: compresión máx(0,-N). N+: tracción máx(0,N), kN.
- Cada caso se evalúa en 41 posiciones del elemento, con el mismo
  FrameForces y la misma curva nominal que utiliza el visualizador.
  Los máximos reportados son de esas muestras, no optimización continua.
- Casos: activa, C1, C2, C3, envolvente. La envolvente exige datos en los
  tres casos; un caso ausente no se presenta como cero. Conserva dentro
  del radar los factores de casos editados mientras se visitan otros casos.
  Restablecer explícitamente un preset desde la barra superior elimina
  el ajuste capturado de ese caso. No escribe ni modifica resultados JSON.

El ranking considera únicamente vigas/columnas visibles y respeta el
filtro de piso y visibilidad existente. Se actualiza cada 0,6 s y al cambiar
los controles. Gris: sin evaluación aplicable. Seleccionado: amarillo.
En D/C los colores se basan en el valor absoluto del cociente; para M/V/N
son relativos al máximo visible y no significan cumplimiento de capacidad.

## Navegación

Top 5: elementTag, ID, valor, caso gobernante y posición desde I. Al pulsar
una fila, activa el caso correspondiente, selecciona el elemento, aproxima
la cámara suavemente y abre su diagrama existente. En M y V muestra el
componente predominante; el valor del ranking sigue siendo la resultante.

«Recorrer Top 5» visita cada fila durante cuatro segundos. El ranking se
mantiene estable durante el recorrido. Se puede detener con el botón o
al comenzar a navegar con el botón derecho/central del mouse.
Selecciona el primer elemento inmediatamente, conserva una copia del Top 5
y muestra «Visitando n/5» hasta completar el recorrido. Las referencias a
cámara y selector se resuelven al visitar, no solo al construir la estructura.
La interpolación de cámara la controla OrbitCamera en LateUpdate para evitar
que la navegación normal sobrescriba la transición del radar. Si faltan
cámara o selector, se informa el motivo y se detiene, sin avanzar en silencio.

El botón de simulación sísmica está fijo arriba del panel de capas, fuera
del scroll del radar. En el Editor permanece disponible aunque el destino
de compilación seleccionado sea Android; el controlador se crea también
en ese caso. Esto no habilita ejecución local de Python en un teléfono.

El mapa utiliza MaterialPropertyBlock y restaura los bloques anteriores al
ocultarlo; no cambia materiales estructurales, datos, cargas ni geometría.
Se suspende durante la reproducción sísmica para no confundir el ranking
estático con los estados dinámicos.

## Validación

`P1L4/tests/verificar_armadura_seccion.ps1` verifica además máximos
interiores, resultantes, signos, D/C con P y M simultáneos, capacidad ausente,
demanda axial fuera de curva y fuerzas no finitas. Compila el runtime
completo de escritorio, incluido el radar y su integración en capas.
