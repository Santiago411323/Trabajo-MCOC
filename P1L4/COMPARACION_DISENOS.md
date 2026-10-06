# Duelo de diseños — Antes ↔ Después

En el panel de resultados de una viga/columna/muro, abrir **ANTES ↔ DESPUÉS**.
Es una vista integrada en el mismo panel desplazable, sin una ventana adicional.

## Demostración

1. Partir de resultados vigentes, sin reproducción sísmica ni incremento móvil.
   Guardar **ANTES**. También se captura automáticamente antes de la primera
   vista previa del editor, si no existe una referencia y el modelo está vigente.
2. **EDITAR ESTE ELEMENTO**: modificar sección y/o armadura. Guardar la vista
   previa no genera un nuevo estado calculado.
3. **REANALIZAR OPENSEES**. Si termina correctamente y los estados son compatibles,
   el modelo se recarga, se vuelve a seleccionar la pieza editada y se abre
   automáticamente la comparación. La referencia sobrevive a esa recarga de escena.
4. Recorrer la barra **Antes ↔ Después** o pulsar **REPRODUCIR**. Cian es Antes,
   magenta Después y dorado la transición gráfica. En las piezas cuya sección
   cambió también se interpola únicamente su escala visual.
5. Consultar N, Vy, Vz, My o Mz en las dos curvas con la misma escala. Recorrer
   el gráfico con el mouse para leer la misma posición I→J en ambos estados.
6. Activar **MOSTRAR EFECTO EN EL EDIFICIO / VECINOS**: colorear cambios de D/C
   nominal P–My o de momento resultante máximo y visitar las cinco piezas con
   mayores cambios absolutos. El ranking es del edificio, no solo piezas conectadas.
7. **EXPORTAR COMPARACIÓN .CSV** y **COPIAR RUTA** para conservar evidencia.

Las tarjetas comparan desplazamiento máximo muestreado, D/C nominal, capacidad
de momento a P=0, momento resultante máximo, área de acero longitudinal y peso
propio de referencia de la pieza. Para muros se muestran sección y demanda P–M
del caso C1/C2/C3 exacto; no se inventan fuerzas de barra ni una deformación shell.

**NUEVO ANTES (REEMPLAZAR)** inicia otra comparación usando el diseño calculado
actual. No revierte archivos ni modifica el modelo. **DESPUÉS ACTUAL CALCULADO**
permite comparar una respuesta recargada por otra ruta; requiere resultados
vigentes. Si ambos estados son idénticos, las diferencias son cero.

## Garantías y alcance

- Se congelan copias profundas de los datos reconocidos por el contrato de Unity,
  con fuente, fecha y huella SHA-256. Las vistas previas no modifican esas copias.
- La misma combinación de factores G/Q/EX/EY se aplica a ambas respuestas base.
  G puede cambiar al modificar el peso propio. Los botones C1/C2/C3 usan las
  tres fórmulas de referencia del proyecto; no son una envolvente normativa nueva.
- Se rechazan cambios de geometría nodal, identidad/conectividad, apoyos,
  diafragmas, unidades, Q superficial, G superficial o coeficiente sísmico base.
  Esta versión compara cambios de sección/armadura sobre la misma topología.
- La barra y las secciones intermedias son una interpolación gráfica. **No es
  un análisis del edificio con una sección intermedia**, una historia dinámica
  ni una predicción de daño. Los números se muestran por separado para los dos
  estados calculados. Tampoco se usan esfuerzos interpolados para verificar capacidad.
- La deformada se reconstruye con traslaciones y rotaciones nodales mediante
  Hermite; la amplificación es gráfica. No es la solución interior exacta bajo
  carga distribuida. Ambos estados usan la misma amplificación y escala de diagrama.
- El criterio de capacidad es nominal P–My uniaxial, consistente con el radar.
  No se sustituye por la capacidad reducida LRFD, ni se certifica diseño integral.
  Datos ausentes o demanda fuera de envolvente no generan porcentajes ficticios.
- Si Antes=0, se informa diferencia absoluta y porcentaje N/D.
- El mapa de cambio se calcula con los dos extremos, independientemente de dónde
  esté la barra. Rojo significa aumento de la métrica; azul, disminución; gris,
  falta de datos o cambio despreciable. No son etiquetas automáticas de colapso.
- La comparación no altera cargas, resultados, identificadores ni archivos del
  modelo. Al salir restaura los colores y escalas temporales de los sólidos.
  Se pausa el radar mientras utiliza el mapa propio, evitando coloreados superpuestos.
- La referencia dura la sesión Play y sobrevive a la recarga del editor. Se pierde
  al reiniciar la sesión/aplicación o recargar el dominio por compilación. El CSV
  conserva las cifras exportadas, no reconstruye por sí solo las dos soluciones.

## Implementación

`DesignComparisonData.cs`: índices propios y métricas de los estados congelados.
`DesignComparisonSession.cs`: captura, validación y conservación durante la recarga.
`DesignComparisonOverlay.cs`: contornos por lotes, diagramas y mapa de cambios.
`ElementResultsDesignComparison.cs`: barra, controles, tarjetas y exportación.
El editor captura antes de mutar las propiedades, acepta Después tras leer la
exportación y exige que el archivo de resultados haya sido actualizado.

## Validación reproducible

```powershell
python P1L4/tests/test_design_comparison.py
& P1L4/tests/verificar_armadura_seccion.ps1
```

La primera prueba calcula G/Q/EX/EY de una copia aislada del modelo de escritorio,
modificando E1_72 de ancho 0,60 a 0,70 m y Ø25 a Ø28. Contrasta la superposición
C1 con una corrida OpenSees directa y comprueba que el JSON de trabajo no cambió.
Los datos se guardan en `design_comparison_validation/`, regenerables y excluidos
de Git. La segunda prueba verifica la lectura C#, cambios reales, rechazos de
incompatibilidad, respuesta ausente, porcentajes y compilación del runtime.
Si no existe el conjunto de validación, declara ese chequeo como omitido.

La compilación y las pruebas numéricas no sustituyen el ensayo visual del control,
la recarga, el coloreado y la navegación dentro del Editor.
