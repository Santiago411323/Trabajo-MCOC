# AR/VR Android e iPhone: ventanas, diafragmas y modelo vigente

Se sincroniza el modelo actual de escritorio con ambos recursos móviles. SHA-256 del modelo: `3d38f18e9a246578f21cc9df30793a5cf34cbe42af19acd9a2c44d385d21b444`. Se conservan los cambios de sección y armadura actuales, incluidos los de E1_241; no se sustituyen por el modelo del build anterior.

## Aplicado

- En VR: se elimina solamente el paño E1_271–E1_259 y se añaden E1_259–E1_310, E1_271–E1_311 y E1_311–E1_310, en Unity Y=12–16. Los paños vecinos se conservan. El estilo, shader estereoscópico y botón del ambiente siguen siendo los existentes.
- AR y VR: geometría, secciones, acero, curvas y resultados del JSON actual. Se recalculan y empaquetan ocho casos sísmicos completos X/Y (25/50/100/150 %, 1660 cuadros cada uno), y nueve unidades de cálculo LRFD para evaluar 22 variantes por superposición en el teléfono.
- En Herramientas AR y en Estudio VR se añade **Diafragmas**. Al seleccionar una viga o columna muestra a qué grupos reales pertenecen sus nodos I/J, nivel, maestro, nodos vinculados, Ux/Uy, Rz y residuo de compatibilidad. Usa la membresía exportada de `rigidDiaphragm(3)`, no las losas decorativas. No añade restricciones, cambia el modelo ni amplifica los números. La página estática se pausa durante sismos.
- Ambos proyectos conservan el buscador por elementTag/ID de AR y la selección por mirada de VR. Los diagramas globales del componente común quedan habilitados por defecto; los controles y diagramas nativos de cada elemento/sector AR y de VR conservan su funcionamiento.

## Alcance

La tecla 1 para inspección, WASD, gravedad, salto y skate pertenecen al modo juego de PC. No se portan controles de teclado a los teléfonos. La comparación de diseños que requiere editar y reanalizar, y el trazado incremental de cargas Structural X-Ray que requiere el worker Python/OpenSees, permanecen en PC. No se presentan en el teléfono resultados de reanálisis que este no puede producir.

No se cambian permisos ni cámara. El teléfono sigue usando resultados OpenSees precalculados.

## Validación y entrega

- LRFD: 22 variantes finitas, equilibrio, U1 exacta, U4 contrastada con OpenSees directo. Error máximo: 1,7508e-11 kN/kN·m. Log `movil_nuevo_lrfd_validacion.log`.
- Empaquetadores: hashes, integridad de binarios y correspondencia de ambos modelos comprobados; ocho registros completos, 350,5 MiB comprimidos por aplicación.
- Pruebas Unity: `MobileApplicationValidation`, `MobileEnvironmentRevisionValidation`, `MobileEngineeringValidation`. Comprueban AR, signos de momento, sectores, fuerzas, curvas, LRFD, radar, sismos y retorno VR→AR, ventanas nuevas/vecinas, membresía de los cinco diafragmas y la nueva página VR con restauración de sus botones.
- Logs Android/iPhone: `movil_nuevo_android.log`, `movil_nuevo_ios.log`. Exportación iPhone: `movil_nuevo_export_ios.log`.
- iPhone: build 17, ZIP Xcode nuevo en Entregables; `validacion_zip_ios.json` registra su nombre, hash y comprobación completa de CRC/recursos/permisos. Solo se conserva el último ZIP después de validarlo.
- ZIP validado: `MCOC_iOS_Xcode_20261007_020306_951_50306c.zip`, 597,9 MiB; SHA-256 `878cf9ec12e3d66068cc33d30adc4e5a2837182eadfd01060c2fa9e17a343d94`. Exportación y comprobación completa: PASS. Se retiró el build 16 después de verificar este archivo.
- Android: proyecto y recursos actualizados. No se genera APK mientras falte Android Build Support/SDK/NDK/OpenJDK en esta instalación de Unity.

Estas pruebas se ejecutan en Unity; el nuevo build requiere comprobación física de seguimiento y presentación en los teléfonos.
