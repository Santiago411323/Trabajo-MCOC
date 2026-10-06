# AR/VR Android e iOS: herramientas del commit 75e2a47

Ambas aplicaciones usan el modelo y las armaduras del último commit. Se conservaron los controladores de cámara, los anchors, el sentido de los diagramas y los calculadores existentes.

## Uso

- **AR:** coloca y ancla una viga/columna, abre Menú → Ingeniería. El panel sigue limitado a una parte de la pantalla, con desplazamiento interno. Armadura, Radar y LRFD son pestañas independientes.
- **VR:** mira **Estudio / volver**. Las acciones se activan con la misma mirada o disparador del visor. El menú normal y la cruceta se recuperan al volver. **Detalle** alterna las cifras del cálculo.

### Armadura

Área y cuantía, mínimo/máximo de referencia, compatibilidad de deformaciones, eje My/Mz, cara y P de la combinación o flexión pura. Vista 3D aislada con hormigón transparente, barras y estribos exportados; giro, zoom, Play/Pausa, sección móvil desde I, deformada estática OpenSees y curva de sección M–Φ. Curvas P–My nominal y de diseño con φ variable y ramas independientes. Las grietas son didácticas. Los resultados, geometría e IDs no se modifican por consultar el inspector.

La reproducción de sección P≈0 y la interpolación gráfica de la deformada estática están rotuladas por separado. La escala visual no cambia esfuerzos. La verificación es parcial y uniaxial; no representa colapso ni una certificación del edificio.

### Radar

M, V, compresión, tracción y D/C nominal, C1/C2/C3 y envolvente completa. Top 5 ordenado con elementTag, ID, caso y distancia desde I. En AR se consideran los elementos colocados; una fila selecciona el anchor existente y su diagrama. En VR se consideran los elementos visibles del piso actual; se puede seleccionar, recorrer y activar el mapa de colores. No se mueve la cabeza ni se teletransporta al usuario. Las propiedades gráficas se restauran al cerrar o cambiar de herramienta. El radar nominal se pausa durante sismos y casos LRFD.

### LRFD

Intensidades D/L, Lr, profundidad y densidad de nieve, agua retenida, velocidad/Cp/dirección de viento y factor E. El teléfono evalúa 22 variantes U1–U7 por superposición lineal de nueve casos unitarios OpenSees precalculados. Lr/S/R son alternativas; se comparan variantes por respuesta del elemento. Cada fila conserva esfuerzos concomitantes en la estación crítica; el caso elegido se aplica al diagrama existente. Se identifica el gobernante y en VR se pueden recorrer las siete filas.

Un cambio de escenario invalida la evaluación y restablece C1 si había un caso LRFD aplicado. C1/C2/C3 y los JSON base no se escriben. Cubierta y viento mantienen las idealizaciones del laboratorio PC; el teléfono no ejecuta Python/OpenSees. La demanda/capacidad de diseño P–My utiliza φ variable, distinta del radar nominal.

En VR se incluyen acciones visuales, lluvia/nieve excluyentes, viento, nubes y espesores de nieve/agua configurados. Se puede abrir visualmente la fachada/cubierta y restaurarlas. Los efectos se pausan durante sismos y se ocultan al salir del laboratorio. No participan en cargas, masas, selección ni colisiones. La cámara AR conserva su imagen y permisos; no se sustituye por el clima del edificio virtual.

## Datos y validación

- `pack_lrfd_mobile.py` exporta las nueve unidades y valida equilibrio/hash del modelo. `MobileLrfdUnits.json` se incluye en ambos proyectos y es portátil.
- Los ocho casos sísmicos se recalcularon con el modelo vigente y se empaquetaron nuevamente. El build rechaza datos LRFD/sísmicos de otro modelo.
- `test_lrfd_analysis.py`: 22 variantes finitas, equilibrio, U1 exacta y U4 frente a OpenSees directo, error máximo 1,42e−11 kN/kN·m.
- Compilación de runtime C# Android e iOS: PASS.
- Unity en ambos proyectos: comparación de las 22 variantes móviles con fuerzas, nodos y rotaciones OpenSees; armaduras, capacidades, radar, selección, gráficas/texturas, escenario inválido, efectos visuales, ocho casos sísmicos, regreso a resultados estáticos y AR.
- Capturas del inspector, radar y LRFD en `unity_visualizador*_ingenieria_*.png`; logs `lrfd_movil_*`.

Para regenerar los datos, ejecutar `lrfd_analysis.py` con el modelo actual y un escenario, luego `pack_lrfd_mobile.py`. Las unidades se obtienen del caché identificado por hashes del modelo, catálogo y código original del laboratorio. No se modificó ese calculador.

iOS se exporta con build 15 a un ZIP Xcode; se comprueban CRC, proyecto, permiso de cámara, permisos ejecutables y recursos exactos de modelo/LRFD/sismos. Solo se conserva el último ZIP después de validarlo. Android tiene el proyecto actualizado y runtime compilado; este editor carece de Android Build Support/SDK/NDK/OpenJDK, por lo que no genera un APK nuevo. Las comprobaciones en Editor no sustituyen la prueba física en teléfono.

Entregable final validado: `P1L4/unity_visualizador_ios/Entregables/MCOC_iOS_Xcode_20261006_182758_371_7c5a5c.zip` (595,6 MiB). SHA-256: `c30ccb72db0a7112da0d3db9f9b539ca864ac730c5c1940cef7d3b00f24b869c`. Evidencia: `validacion_zip_ios.json` y `lrfd_movil_export_ios.log`. Los ZIP anteriores se retiraron después de esta validación.

El retorno desde VR vuelve a registrar el escenario LRFD propio de AR, aunque se haya evaluado otro escenario dentro del edificio virtual. Esta correspondencia se comprueba contra las doce fuerzas de U1 después de salir de VR. La vista 3D se oculta y libera al suspender AR o cerrar las herramientas. La inspección visual confirmó que el shader RGB del panel conserva los colores de la armadura y de las curvas.
