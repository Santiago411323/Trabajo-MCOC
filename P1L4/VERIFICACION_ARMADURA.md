# Verificación explicativa de armadura en Unity

Seleccionar una viga o columna → Activar resultados → Sección de fibras.
Debajo del dibujo aparecen cuantía, comprobaciones y perfil de deformaciones.
Se puede cambiar My/Mz, cara positiva/negativa y, en columnas, P de la
combinación activa o P=0. «Ver cálculo paso a paso» despliega los números.
«Editar modelo» conserva el editor paramétrico existente.

## Laboratorio 3D del elemento

Dentro de la misma vista, el laboratorio dibuja la pieza completa con su
longitud real, sección y barras individuales. El hormigón es transparente;
la cámara del RenderTexture está aislada del edificio. Se puede girar con
arrastre, cambiar zoom, Play/Pause, reiniciar y recorrer la respuesta.

- **Curva de sección:** reproduce puntos de la curva exportada P≈0, con
  un punto sincronizado sobre M–Φ. Recupera ε0 desde la máxima deformación
  de hormigón y la discretización real, usando ε(y)=ε0−Φy. Comprueba las
  deformaciones individuales de acero contra el máximo exportado. Verde:
  tracción; naranja: compresión; rojo: fluencia. La curvatura constante
  dibuja un arco de flexión pura ilustrativo; no predice la deformada
  del elemento dentro del edificio. El recorrido se detiene en la primera
  muestra que alcanza εcu, sin simular destrucción.
- **Deformada estática OpenSees:** interpola traslaciones/rotaciones
  nodales mediante Hermite en direcciones transversales y mantiene la
  interpolación axial lineal. El intercambio de ejes X,Z,Y se aplica al
  producto vectorial para respetar el signo de las rotaciones. Para
  incrementos de carga móvil sin rotaciones se usan traslaciones lineales.
  La reproducción interpola gráficamente una respuesta estática: no es
  un análisis dinámico. Los valores mostrados siempre son reales y
  únicamente la geometría aplica la escala visual.
- **Corte móvil:** informa x desde I, porcentaje de longitud y esfuerzos
  reales de la combinación activa, independientemente de la curva P≈0.
- **Grietas opcionales:** solo didácticas, después de Mcr de referencia
  (0,63√f'c·Ig/yt). No son fisuras individuales ni aberturas calculadas.
  La curva exportada no modela la resistencia a tracción del hormigón;
  no se presenta el umbral Mcr como un punto simulado de agrietamiento.
- **Estribos:** count/diámetro/espaciado/ramas configurados. La ubicación
  centrada del tramo es ilustrativa porque no se exportó su posición.
  Si faltan datos de estribos en columnas, no se inventan.

Al ocultar/cambiar resultados se desactiva la cámara. Al deshabilitar el
panel se liberan cámara, textura, geometría y materiales. No modifica
geometría estructural, IDs, cargas ni archivos de análisis.

## Criterios y alcance

Referencia implementada: ACI 318-08 general, capítulos 9 y 10, coherente con
la edición mencionada en los apuntes. No constituye verificación completa
del capítulo 21 ni de requisitos chilenos específicos. Los estados «cumple»
se refieren únicamente al criterio rotulado en cada tarjeta.

- Área longitudinal recalculada desde cantidad y diámetro: As=n·π·Ø²/4.
- Cuantía total: As/(b·h), diferente de As,fila/(b·d) en vigas.
- Columnas: mínimo 0,01Ag y máximo 0,08Ag (10.9.1), sin excepciones.
- Vigas: As,min=máx(0,25√f'c/fy;1,4/fy)·b·d en MPa/mm (10.5.1),
  comparado conservadoramente con la fila extrema traccionada. No incluye
  excepciones de mínimo. El acero restante participa en el equilibrio.
- Vigas en flexión pura: límite εt≥0,004 (10.3.5). No se aplica
  automáticamente 0,75ρb ni se inventa un As,max para una sección con
  acero superior y lateral. La banda de cuantía muestra una referencia
  simplemente armada rotulada expresamente como tal: cmax=0,375d;
  amax=β1·cmax; As,max=0,85·f'c·b·amax/fy. Este cmax corresponde a
  εt=0,005 para εcu=0,003 y es distinto del límite general εt≥0,004
  indicado arriba. La tarjeta muestra la comparación con la fila extrema;
  la ductilidad de la sección completa se comprueba por separado.
- β1=0,85 hasta f'c=28 MPa; disminuye 0,05 por cada 7 MPa hasta 0,65.
- Equilibrio independiente con bloque de Whitney, εcu=0,003 y acero
  elastoplástico perfecto. Todas las barras participan; se descuenta
  el hormigón que ocupan dentro del bloque mediante intersección circular
  continua para evitar saltos artificiales al cruzar una fila de barras.
- εy=fy/Es; compresión si εt≤εy, transición entre εy y 0,005,
  tracción si εt≥0,005. ϕ para estribos varía entre 0,65 y 0,90.
  Es un valor explicativo que **no modifica** la capacidad exportada.
- En columnas se usa P de la misma convención del inspector/P-M existente,
  compresión positiva. El cálculo es uniaxial por el eje elegido; no verifica
  interacción biaxial, esbeltez ni efectos de segundo orden.
- Si falta la distribución o P queda fuera del dominio de equilibrio,
  no se declara un tipo de falla.

La curva P-M exportada y su demanda/capacidad se muestran por separado,
usando el criterio existente del visualizador. Mn Whitney es un cálculo
independiente y puede diferir de la curva de fibras Concrete01/Steel01.
El perfil representa la sección **al límite**, no el daño actual ni un
colapso producido por el análisis elástico del edificio.

La condición balanceada no garantiza ductilidad. En una sección subarmada,
el acero fluye antes del aplastamiento del hormigón en compresión.

No se alteran JSON, cargas, fuerzas, desplazamientos ni análisis OpenSees
al consultar esta vista. La lectura se actualiza con las propiedades
editadas que entregue el editor existente. Se usa la distancia al centro
de barras exportada como cover_mm, no se suma otra vez el estribo.

## Fuentes

- [Programa oficial ACI basado en ACI 318-08](https://www.concrete.org/Portals/0/Files/PDF/ReinforcedConcreteDesign.pdf).
- [Manual oficial ACI 318-11, explicación de límites de deformación](https://www.concrete.org/portals/0/files/pdf/previews/sp1711v1.pdf).
  Este manual complementa la explicación; no es un cambio de edición.
- [Manual del fabricante Dlubal: límites de armadura de columnas, 10.9.1](https://www.dlubal.com/es/webfile/003219/3726871/concrete-columns-aci-manual-en_de.pdf?hash=5b9128ba67f80ff51c5a468e5884768a1b3231fa).

## Validación reproducible

Ejecutar `P1L4/tests/verificar_armadura_seccion.ps1` en PowerShell.
Comprueba áreas, mínimo, máximo, asimetría de caras, cambio de ejes,
dependencia de P, equilibrio, rechazo de datos ausentes y secciones reales
de ambos JSON. También compila el runtime completo de escritorio con las
referencias del Unity instalado. No equivale a una prueba física móvil.
