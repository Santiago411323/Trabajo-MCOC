# Corrección de vigas — 5 de octubre de 2026

Revisión de ambos edificios. Se eliminaron tres barras duplicadas y se unieron seis pares continuos, pasando de 453 a **444 vigas** (808 elementos analíticos en total). Los casos corregidos corresponden al edificio 1; el edificio 2 no presentó estos errores.

| Barras anteriores | Viga conservada | Acción |
| --- | --- | --- |
| E1_64.1 / E1_209 | E1_64 | Eliminar E1_209 superpuesta y unir E1_64.1 + E1_64.2 |
| E1_93.1 / E1_210 | E1_93 | Eliminar E1_210 superpuesta y unir E1_93.1 + E1_93.2 |
| E1_112.1 / E1_205 | E1_112.1 | Eliminar E1_205 superpuesta |
| E1_105 + E1_106 | E1_105 | Unir sin apoyo ni viga transversal intermedia |
| E1_113 + E1_114 | E1_113 | Unir sin apoyo ni viga transversal intermedia |
| E1_149 + E1_150 | E1_149 | Unir sin apoyo ni viga transversal intermedia |
| E1_157 + E1_158 | E1_157 | Unir sin apoyo ni viga transversal intermedia |

E1_112.2 permanece separada de E1_113 por E1_132. E1_112.1 y E1_112.2 también permanecen separadas por sus conexiones transversales. Los IDs de las barras no afectadas se conservan; las uniones guardan los tags e IDs anteriores en `mergedElementTags` / `mergedElementIds`.

La corrección se aplica en `P1L3/carga_viva_sismo.py`, después de las particiones existentes, para que futuras exportaciones vuelvan a generar el modelo corregido. Cada caso valida coincidencia geométrica, sección, edificio y cargas por metro. Las uniones exigen un nodo intermedio sin apoyo y con solamente las dos barras incidentes. Si esas condiciones cambian, el proceso se detiene.

Se conservan las cargas D/L y áreas tributarias existentes. Al quitar barras duplicadas también se retira su peso propio contado dos veces. No se alteraron columnas, muros, losas, ambiente, cámaras, controles ni código de representación de los diagramas. Las demandas y desplazamientos sí cambian como resultado del nuevo análisis estructural. Las curvas de capacidad P-M conservan su definición porque las secciones y materiales no cambiaron.

## Recálculo y comprobaciones

OpenSees 3.8.0 recalculó **G, Q, EX, EY, C1, C2 y C3**, con 5.656 registros de fuerzas locales. Android e iPhone contienen el mismo JSON actualizado.

| Comprobación | Resultado |
| --- | --- |
| Auditoría geométrica de ambos edificios | 444 vigas; 0 solapes; 0 uniones injustificadas; 0 conexiones ambiguas |
| Conservación de D/L y áreas tributarias | PASS |
| Conectividad, equilibrio, superposición y correspondencia con OpenSees | PASS: 36.566 comprobaciones |
| Código real UnityData/FrameForces contra OpenSees independiente | PASS: 356.409 comprobaciones; diferencia máxima 0,002226 (tolerancia 0,01 kN o kN·m) |
| Equilibrio G/Q/EX/EY/C1/C2/C3 | Desbalance máximo menor que 8,1 × 10⁻⁹ kN |
| Regresión E1_113 y conservación de separación E1_112.2 | PASS |
| Unity iOS: flujo AR, sector, signos de momentos y entrada/salida VR | PASS en editor; incluye ambiente y selección E1_94 |
| ZIP Xcode, build 11 | CRC completo válido; incluye exactamente el JSON recalculado en sharedassets0.assets |

Reacción vertical total G: 60.285,482 kN. La viga E1_94 conserva ID 94 y nodos 61→60; su momento My al centro en C1 es ahora −23,377838 kN·m. Se actualizaron únicamente las referencias numéricas de las pruebas, sin modificar las fórmulas ni la orientación de los diagramas.

La evidencia geométrica anterior se conserva en `auditoria_vigas_antes.json` / `.md`; el resultado final está en `auditoria_vigas.json` / `.md`.

Entregable iPhone: `P1L4/unity_visualizador_ios/Entregables/MCOC_iOS_Xcode_20261005_171624_301_3c2364.zip`. Conserva la configuración de cámara y los proveedores ARKit/Cardboard. Estas pruebas de editor/exportación no sustituyen la instalación física en el iPhone.
