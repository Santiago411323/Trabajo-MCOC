# MCOC — copia independiente para iPhone

Esta carpeta es un proyecto Unity separado de `P1L4/unity_visualizador` (Android). Sus Assets, Packages y ProjectSettings son copias independientes. Los scripts de exportación iOS sólo existen aquí. Conserva la escena AR de colocación libre I–J, el JSON estructural y los mismos `elementTag`.

Unity: **6000.6.0f1**. AR Foundation y Apple ARKit XR Plugin: **6.6.2**. iOS mínimo configurado: **15.0**, sujeto a compatibilidad del teléfono con ARKit y a requisitos de la versión de Xcode utilizada. Los scripts se compilaron y la exportación iOS terminó correctamente en Windows. La validación de configuración y datos pasó. Quedan pendientes la compilación y firma en Xcode y la prueba física en iPhone.

## Los dos ZIP

| Archivo | Qué contiene | Dónde abrirlo |
|---|---|---|
| `MCOC_iOS_Unity_....zip` | Proyecto fuente de Unity: Assets, Packages, ProjectSettings y guías | Descomprimir y abrir la carpeta `unity_visualizador_ios` en Unity Hub |
| `MCOC_iOS_Xcode_....zip` | Proyecto Xcode generado por una exportación iOS correcta | Descomprimir TODO en el Mac y abrir `Unity-iPhone.xcodeproj` |

El ZIP de Unity **no se abre directamente en Xcode**. El ZIP de Xcode se genera aquí en Windows con Unity e iOS Build Support: al recibir ese ZIP, **no necesitas Unity en el Mac**, sólo Xcode para compilar, firmar y ejecutar en el iPhone. Ninguno es una aplicación `.ipa` firmada ni se instala directamente en el iPhone.

## Generar el ZIP para Xcode

1. En este Windows ya se instaló el módulo oficial **iOS Build Support** para Unity **6000.6.0f1**. También puedes exportar en otro Windows o Mac con esa instalación; no se necesitan herramientas externas del repositorio GameCI.
2. Abre `unity_visualizador_ios` en Unity Hub. Espera la resolución de paquetes y compilación de scripts.
3. La copia intenta configurar ARKit automáticamente al abrirse. Comprueba **Edit > Project Settings > XR Plug-in Management > iOS > ARKit**. Si faltaba el módulo iOS, instálalo y ejecuta **MCOC > iPhone > Configurar ARKit**.
4. En **Player Settings > iOS**, ajusta tu identificador de aplicación para que sea único. El valor inicial es `com.mcoc.structuralar.ios`. No contiene tu cuenta ni certificados de Apple.
5. Ejecuta **MCOC > iPhone > Exportar Xcode y ZIP**. Si Unity acaba de cambiar a la plataforma iOS, espera la importación y vuelve a ejecutar ese menú.
6. Cada exportación correcta crea un ZIP nuevo, con fecha UTC y sufijo único, dentro de `unity_visualizador_ios/Entregables/`. El proyecto Xcode sin comprimir queda en `Builds/iOS/`.

El empaquetado también se ejecuta después de una compilación iOS normal desde la ventana Build de Unity. Una exportación fallida no debe considerarse entregada. Si falla el empaquetado, Unity informa el error; no entrega un ZIP parcial como válido.

En Mac se utiliza la herramienta del sistema `ditto` para conservar permisos de ejecución y enlaces. En Windows se utiliza `tar.exe` incluido en el sistema, que admite las rutas largas de IL2CPP. Después se conservan los permisos de ejecución de scripts y herramientas habituales en el ZIP y se identifica su formato de permisos como Unix. Su compatibilidad final debe comprobarse en el Mac.

Si una exportación termina correctamente pero el empaquetado falla, el código de exportación informa el fallo del ZIP aunque Unity anuncie éxito de la generación Xcode. Puede recuperarse la exportación sin recompilar, indicando su carpeta en la variable `MCOC_XCODE_PATH` y ejecutando Unity con `-executeMethod IOSBuild.RepackageBatch`.

## Abrir en Xcode e instalar en el iPhone

1. Lleva al Mac el ZIP **Xcode**, descomprímelo completo y abre `Unity-iPhone.xcodeproj`. Si posteriormente agregas dependencias que generan un `.xcworkspace`, abre ese workspace.
2. En el target **Unity-iPhone**, entra a **Signing & Capabilities**, selecciona tu **Team**, habilita firma automática y usa un Bundle Identifier único.
3. Conecta un iPhone compatible, acepta la confianza del computador y habilita Developer Mode si iOS lo solicita. Selecciona el iPhone como destino en Xcode.
4. Ejecuta **Product > Run** y permite el acceso a la cámara.
5. Elige un elemento, fija sus nodos I/J, ajusta la cara y ancla. Comprueba el mismo `elementTag` y los resultados C1/C2/C3 al caminar alrededor.

Para pruebas directas en tu propio dispositivo puede utilizarse una cuenta personal de Apple en Xcode, con las limitaciones que Apple aplica a esa modalidad. TestFlight y distribución requieren la membresía correspondiente. Esta copia no configura publicación automática, firmas ni contraseñas.

## Mantener el ZIP de Unity actualizado

Desde PowerShell en Windows:

```powershell
& .\P1L4\unity_visualizador_ios\empaquetar_unity_ios.ps1
```

Se crea una nueva copia ZIP en `P1L4/entregables_ios/`. El archivo omite Library, Logs, Temp, UserSettings, Builds, Entregables y otras cachés. Ejecútalo después de editar esta copia si vas a trasladarla al Mac. Los ZIP generados se conservan localmente y se excluyen de Git.

## Exportación por línea de comandos

Con iOS Build Support instalado, Unity puede invocarse con:

```text
Unity -batchmode -nographics -projectPath "RUTA/unity_visualizador_ios" -buildTarget iOS -executeMethod IOSBuild.ExportBatch -logFile "RUTA/exportacion_ios.log"
```

Para validar datos y configuración sin exportar, usa `-executeMethod IOSBuild.ValidateBatch`. Una validación de configuración no sustituye la compilación Xcode ni la prueba en teléfono.

## Límites de la copia

El panel AR v3 comienza con la selección del elemento en la parte inferior, respeta el
área segura del iPhone y se contrae al comenzar la colocación. Se abre con `Menu`.
Al tocar un elemento ya colocado se abre para consultar sus datos.
Los controles completos tienen scroll y ocupan como maximo el 46% del alto del area segura.

El build iOS `4` conserva la solicitud de permiso mediante AVFoundation,
la API nativa de Apple, antes de habilitar captura y sesion AR. La app diferencia permiso
SIN DECIDIR, DENEGADO, RESTRINGIDO y un Info.plist sin descripcion de uso.
No interpreta una solicitud pendiente como un permiso denegado. El boton `Solicitar permiso
de camara` permite iniciar la solicitud nuevamente si sigue sin decidirse.
Si iOS ya tiene una decision guardada no vuelve a mostrar su aviso: si fue denegado, la app
muestra `Abrir Ajustes: permitir camara`. Despues de permitirla y volver, intenta iniciar AR.
El estado `Camara: imagen activa` requiere texturas de camara recientes y fondo AR habilitado;
no basta con tener componentes en la escena. Si no llega imagen aparece `Reintentar camara`.
La simulacion de Unity no solicita permiso de la camara del telefono.
En la consola Xcode, busca `[MCOC Camera]` y `[MCOC Camera v2]` para comprobar el estado
antes y despues de la solicitud. La exportacion incluye `MCOCCameraPermission.mm`;
su compilacion nativa y el aviso del sistema deben validarse en Xcode/iPhone.

La escena AR muestra resultados precalculados del JSON; no ejecuta Python/OpenSees ni recalcula el edificio en el iPhone. Las opciones de escritorio para ejecutar Python pertenecen al visualizador original y no forman parte del flujo AR móvil.

La versión copiada usa **colocación libre sin imagen de referencia**. Si la evaluación sigue exigiendo detectar una imagen, ese requisito aún debe implementarse y validarse: la adaptación a iOS no lo añade.

Los cambios futuros en Android no se sincronizan automáticamente con esta copia. Deben trasladarse deliberadamente para conservar la independencia.

## Colocación guiada y sector (iPhone, build 3)

El flujo muestra cinco pasos: elegir viga o columna, marcar I, marcar J, ajustar la cara
y confirmar el anclaje. Es posible corregir J conservando I y cancelar la nueva colocación
sin borrar los elementos anteriores. `+ Agregar otro elemento` inicia una colocación adicional.

En `Ajuste`, elige Todo, Nodo I o Nodo J y un paso de 1, 2 o 5 cm. Los botones mueven
el objetivo según la cámara; los giros de 5° y los giros de cara actúan sobre el elemento
completo. Corregir un nodo conserva fijo el extremo opuesto. Estos ajustes cambian solo
la representación relativa al anchor; conservan el elementTag y no recalculan OpenSees.

Los botones C1/C2/C3 cambian el caso del elemento seleccionado. En `Resultados` puedes
superponer sus diagramas para N, Vy, Vz, My o Mz: amarillo C1, verde C2, magenta C3.
Las tres curvas usan la misma escala, normalizada por el mayor valor absoluto de las
muestras, y muestran sus mínimos, máximos y el caso de mayor esfuerzo absoluto.
Esta comparación utiliza los resultados precalculados; no es una verificación de capacidad
ni una envolvente analítica continua.

Desde el build 4, en las vigas los momentos My/Mz positivos se dibujan debajo del eje
y los negativos arriba, tanto individualmente como al superponer C1/C2/C3. El lado
se fija usando la vertical AR, sin depender de la posición de la cámara ni del sentido
I→J. En vigas inclinadas se proyecta la vertical sobre el plano perpendicular al tramo;
en elementos verticales se usa una dirección fija de su cara local. Esta convención
solo modifica el dibujo: los valores y signos numéricos de OpenSees se conservan.

Cada colocación tiene su propio anchor, número, combinación y componente. Puedes elegirla
tocando su línea en AR o usando la lista `Sector`; quitar una conserva las demás. El
volumen sólido queda invisible y su collider permite seleccionarlo. El sector se conserva
durante la sesión y no se restaura después de cerrar la app.

`MCOC > iPhone > Validar sector, ajuste y comparacion` comprueba los ajustes I/J, los
estados independientes y la escala común contra los datos reales. La exportación ejecuta
estas comprobaciones. El seguimiento físico y la precisión deben probarse en el iPhone.

Documentación oficial:

- https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.6/manual/project-configuration-arkit.html
- https://docs.unity.com/en-us/engine/6000.5/manual/platform-specific/iphone/ios-building-and-delivering/build-process
- https://developer.apple.com/help/account/basics/about-your-developer-account
