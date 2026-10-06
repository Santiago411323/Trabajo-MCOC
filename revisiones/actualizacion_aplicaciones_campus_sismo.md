# Aplicaciones iPhone y Android â€” campus y sismo precalculado

Se adaptÃ³ el commit fa11e46 a ambas aplicaciones mÃ³viles. Se conserva la configuraciÃ³n de cÃ¡mara de cada plataforma: ARKit/iOS y ARCore/Android. No se alterÃ³ el cÃ¡lculo estÃ¡tico ni se ejecuta OpenSees en el telÃ©fono.

## Funciones mÃ³viles

- Ambiente del visualizador actualizado en VR: ventanas, estacionamientos, Ã¡rboles, cancha, jugadores/graderÃ­as, cafeterÃ­a y terrazas. Se aplican los materiales estÃ©reo, capas de cÃ¡mara y controles de ocultaciÃ³n al campus completo.
- Consulta de la armadura exportada de vigas/columnas mediante el botÃ³n **Acero** de VR. Las curvas y materiales nuevos estÃ¡n en el JSON comÃºn. La ediciÃ³n y el recÃ¡lculo permanecen en el computador.
- Ocho corridas completas El Centro: X/Y con intensidades 0,25 / 0,50 / 1,00 / 1,50. Cada una fue calculada separadamente por OpenSees, sin multiplicar resultados que incluyan gravedad.
- **Sismo** en VR: cargar/cerrar, Play/Pausa, Inicio, X/Y, intensidad, escala visual, avance de 5 segundos y fisuras ilustrativas. La geometrÃ­a se deforma usando posiciones nodales comunes, y los diagramas/valores del elemento seleccionado provienen de la muestra sÃ­smica actual.
- Capacidades nominales compatibles y criterio de fisuraciÃ³n mediante el evaluador existente. Las grietas son ilustrativas; no representan fractura ni pÃ©rdida de rigidez. No se infiere colapso de un modelo elÃ¡stico.
- **Sismo calculado** en AR: reproducciÃ³n y esfuerzos sobre el elemento anclado. No se mueve la colocaciÃ³n medida sobre el elemento fÃ­sico. Se oculta la comparaciÃ³n estÃ¡tica mientras se consulta la respuesta dinÃ¡mica; cerrar recupera C1/C2/C3. Los otros elementos colocados conservan su eje sin presentar sus diagramas estÃ¡ticos como dinÃ¡micos.
- Durante el sismo en VR, las flechas no desplazan al usuario por una estructura que se estÃ¡ deformando. Se mantienen consulta y cambio de piso. Al cerrar se recuperan geometrÃ­a y recorrido estÃ¡ticos.

## Datos y empaquetado

`P1L4/seismic/pack_mobile.py` verifica hashes, cabeceras, dimensiones y registros completos. Empaqueta los binarios con gzip para `Assets/Resources/MobileSeismic` de ambas aplicaciones. El paquete pesa aproximadamente **350,5 MiB por aplicaciÃ³n**. Se carga solamente un caso a la vez, con validaciÃ³n de hash del modelo/binario, IDs y nodos.

Los resultados grandes son regenerables y estÃ¡n ignorados por Git. Para preparar otra copia del repositorio:

```powershell
python P1L4/seismic/transient_analysis.py --all-intensities --direction X
python P1L4/seismic/transient_analysis.py --all-intensities --direction Y
python P1L4/seismic/pack_mobile.py
```

`MobileSeismicBuildCheck` rechaza una compilaciÃ³n mÃ³vil si faltan casos o si el modelo y los resultados no coinciden. No inicia Python desde la aplicaciÃ³n ni desde el procesador de compilaciÃ³n.

## Estado de comprobaciones

- OpenSees: ocho corridas completas, 1.660 estados por caso, 33,18 s, sin truncamiento.
- PASS: contrato, masas, equilibrio gravitacional, apoyos, escalamiento de intensidad descontando gravedad, excitaciÃ³n nula y sensibilidad temporal inicial. Evidencia: `validacion_sismo_movil.log`.
- PASS final en Unity para los proyectos iOS y Android: ocho casos completos, IDs/nodos/fuerzas y separaciÃ³n de escala visual; campus/cafÃ©/ventanas, deformaciÃ³n y selecciÃ³n en VR, diagramas, capacidades y fisuras; recuperaciÃ³n de geometrÃ­a estÃ¡tica y retorno a AR. Evidencia: `Logs/mobile-final-validation.log` en cada proyecto. Se filtran objetos decorativos sin datos estructurales antes de animar/seleccionar.
- PASS posterior a esa correcciÃ³n: compilaciÃ³n de todos los scripts de runtime con sÃ­mbolos `UNITY_ANDROID` y `UNITY_IOS`, usando compilador C# por consola (`tests/verificar_runtime_movil.ps1`). Esto no sustituye el empaquetado nativo.
- PASS: exportaciÃ³n IL2CPP para Xcode, build 12. Entregable: `P1L4/unity_visualizador_ios/Entregables/MCOC_iOS_Xcode_20261006_030643_368_546ce0.zip` (595,4 MiB). Se comprobÃ³ el CRC de todas las entradas, proyecto Xcode, versiÃ³n, descripciÃ³n del permiso de cÃ¡mara, permisos de ejecuciÃ³n y presencia exacta del modelo, ocho metadatos y ocho binarios dentro de los datos exportados. Evidencia: `validacion_zip_ios.json`; comprobador: `verificar_zip_ios.py`. Se dejÃ³ solamente este ZIP actualizado.
- Android: sigue faltando Android Build Support/SDK/NDK/OpenJDK en este editor. No se generÃ³ un APK nuevo ni se probÃ³ fÃ­sicamente ninguna de las dos aplicaciones en esta actualizaciÃ³n.

No se creÃ³ commit ni se publicÃ³ esta adaptaciÃ³n en GitHub.
