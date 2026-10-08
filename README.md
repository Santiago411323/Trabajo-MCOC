# Proyecto MCOC — OpenSeesPy, Unity, AR y VR

Modelo estructural de los edificios 1 y 2, análisis en OpenSeesPy y visualización de geometría, esfuerzos, deformada y capacidad de secciones en Unity. Las aplicaciones Android e iOS consultan resultados precalculados en AR y permiten recorrer el edificio en VR con Cardboard. El ambiente decorativo no interviene en el cálculo estructural.

Repositorio: [Santiago411323/Trabajo-MCOC](https://github.com/Santiago411323/Trabajo-MCOC).

## Estructura

| Carpeta | Contenido |
|---|---|
| `P1L1/` | Benchmark y trabajos iniciales; comparación con SAP2000 |
| `P1L2/` | Geometría base y unificación de los edificios |
| `P1L3/` | Constructor OpenSees, cargas y análisis estático |
| `P1L4/desktop_model/` | Modelo y resultados vigentes de escritorio |
| `P1L4/unity_visualizador/` | Proyecto Unity del visor PC y aplicación Android |
| `P1L4/unity_visualizador_ios/` | Proyecto Unity independiente para iPhone |
| `P1L4/seismic/` | Análisis transitorio y empaquetado móvil |
| `P1L4/tests/` | Verificaciones Python y herramientas de validación |
| `reports/` y `revisiones/` | Informes y evidencias |

## Reproducibilidad: dependencias y versiones

Entorno verificado el **8 de octubre de 2026**, en Windows de 64 bits:

| Herramienta | Versión verificada | Uso |
|---|---|---|
| Python | **3.12.7, 64 bits** | Análisis, exportación y tests |
| OpenSeesPy | **3.8.0.0** | Paquete Python; motor OpenSees **3.8.0** |
| NumPy | **2.5.3** | Cálculo y verificaciones |
| Matplotlib | **3.11.1** | Gráficos e informes Python |
| Plotly | **7.0.0** | Visualizaciones Python |
| Unity Editor | **6000.6.0f1** | Ambos proyectos Unity |
| AR Foundation | **6.6.2** | Sesión, cámara, seguimiento y anchors |
| ARCore / ARKit XR Plugin | **6.6.2** | Android / iOS, respectivamente |
| Google Cardboard XR Plugin | **1.35.0** | VR; revisión fijada en los manifiestos |

`requirements.txt` declara las dependencias Python sin fijar versiones. Para repetir el entorno verificado, usar los pins del comando siguiente. Unity conserva sus versiones en `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` y `Packages/packages-lock.json`, y resuelve sus paquetes al abrir el proyecto. La disponibilidad de OpenSeesPy depende de la plataforma y Python; esta guía toma Windows de 64 bits como referencia.

Instalar Python 3.12.7 y Unity Hub con el editor indicado. Ejecutar todos los comandos **desde la raíz del repositorio**, en PowerShell:

```powershell
py -3.12 -m venv .venv
& ./.venv/Scripts/python.exe -m pip install -r requirements.txt "openseespy==3.8.0.0" "numpy==2.5.3" "matplotlib==3.11.1" "plotly==7.0.0"
& ./.venv/Scripts/python.exe -m pip check
& ./.venv/Scripts/python.exe -c "import sys, importlib.metadata as m; import openseespy.opensees as ops; print(sys.version); print('OpenSeesPy:', m.version('openseespy')); print('OpenSees:', ops.version())"
```

`py -3.12` selecciona una instalación 3.12: comprobar que sea 3.12.7 para repetir exactamente el entorno. Crear `.venv` en cada computador; no copiar el entorno de otra máquina.

Para que el reanálisis del visor encuentre Python, configurar **antes de iniciar Unity desde esa sesión**:

```powershell
$env:MCOC_PYTHON = (Resolve-Path ./.venv/Scripts/python.exe).Path
```

Como alternativa persistente, crear `P1L4/seismic/runtime.local.json` con `{"executable":"C:/ruta/al/proyecto/.venv/Scripts/python.exe"}`. Es una configuración local excluida de Git; cada equipo indica su propia ruta.

## Ejecutar análisis y generar resultados estáticos

Para **reproducir el modelo vigente**, con la alineación del edificio 2 y las modificaciones geométricas de muros, ejecutar:

```powershell
& ./.venv/Scripts/python.exe -X utf8 P1L4/mover_muros_positivos_desktop.py
```

El wrapper utiliza la geometría base de `P1L2/unity_visualizador/Assets/Resources/estructura_completo_unity.json`, aplica las correcciones y las ediciones de `P1L4/model_edits.json`, construye el modelo OpenSees y resuelve G, Q, EX, EY y C1/C2/C3. Genera:

- `P1L4/desktop_model/estructura_p1l4_desktop.json`: geometría, IDs, desplazamientos, fuerzas locales, propiedades, curvas de capacidad y metadatos.
- `P1L4/desktop_model/slab_load_surfaces.json`: catálogo de superficies de carga.

El análisis y la exportación forman parte del mismo flujo. Para editar desde Unity: seleccionar un elemento, abrir **EDITAR MODELO**, guardar y ejecutar el análisis. La previsualización con cambios pendientes conserva resultados anteriores hasta completar el reanálisis.

El exportador general `P1L4/exportar_resultados_unity.py` sigue disponible para el modelo base, pero ejecutarlo directamente no reproduce todas las correcciones vigentes y escribe sobre el recurso Android. Para esta versión, utilizar el wrapper anterior.

## Abrir el viewer

1. En Unity Hub, usar **Add / Add project from disk** y seleccionar `P1L4/unity_visualizador`.
2. Abrir con **6000.6.0f1** y esperar la importación y resolución de paquetes.
3. Abrir `Assets/Scenes/StructureViewerScene.unity` y pulsar **Play**. Para reconstruir la escena si hace falta, usar **MCOC → Crear Visualizador**.
4. Seleccionar vigas o columnas y consultar ID, sección, combinación y resultados. Los botones **Axial**, **Corte**, **Momento** y **Deformada** controlan la representación.
5. Usar **EDITAR MODELO** para modificar y reanalizar. Los factores de combinación y la escala visual no requieren otro análisis del modelo lineal.

El escritorio utiliza el archivo de `desktop_model` cuando esa fuente está disponible y activa. Los teléfonos utilizan `Assets/Resources`. Revisar la fuente indicada antes de comparar resultados. OpenSees calcula y Unity representa los datos exportados.

## Generar datos adicionales y actualizar los móviles

Tras reanalizar, salir de Play Mode y copiar el modelo y catálogo vigentes a ambos proyectos antes de compilar:

```powershell
Copy-Item -LiteralPath P1L4/desktop_model/estructura_p1l4_desktop.json -Destination P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json
Copy-Item -LiteralPath P1L4/desktop_model/slab_load_surfaces.json -Destination P1L4/unity_visualizador/Assets/Resources/slab_load_surfaces.json
Copy-Item -LiteralPath P1L4/desktop_model/estructura_p1l4_desktop.json -Destination P1L4/unity_visualizador_ios/Assets/Resources/estructura_p1l4_unity.json
Copy-Item -LiteralPath P1L4/desktop_model/slab_load_surfaces.json -Destination P1L4/unity_visualizador_ios/Assets/Resources/slab_load_surfaces.json
```

### Simulación sísmica

Generar ocho respuestas completas —cuatro intensidades en X y cuatro en Y— y empaquetarlas para ambas aplicaciones:

```powershell
& ./.venv/Scripts/python.exe P1L4/seismic/transient_analysis.py --model P1L4/desktop_model/estructura_p1l4_desktop.json --all-intensities --direction X --output P1L4/seismic/desktop_results
& ./.venv/Scripts/python.exe P1L4/seismic/transient_analysis.py --model P1L4/desktop_model/estructura_p1l4_desktop.json --all-intensities --direction Y --output P1L4/seismic/desktop_results
& ./.venv/Scripts/python.exe P1L4/seismic/pack_mobile.py --source P1L4/seismic/desktop_results
```

El empaquetador comprueba hashes, integridad y corridas completas. No utilizar corridas recortadas con `--max-seconds` para el entregable móvil. Más detalles en [la guía sísmica](P1L4/seismic/README.md).

### Unidades y combinaciones LRFD

`P1L4/lrfd_results/` no se incluye en Git. Para recrear el escenario de validación, los resultados y las unidades móviles:

```powershell
New-Item -ItemType Directory -Path P1L4/lrfd_results -Force | Out-Null
Set-Content -LiteralPath P1L4/lrfd_results/validation_scenario.json -Encoding utf8 -Value '{"d":1,"l":1,"roof":0.5,"snowDepth":0.1,"waterDepth":0.03,"snowDensity":300,"windSpeed":25,"windCoefficient":1,"windAngle":45,"e":1}'
& ./.venv/Scripts/python.exe P1L4/lrfd_analysis.py --model P1L4/desktop_model/estructura_p1l4_desktop.json --scenario P1L4/lrfd_results/validation_scenario.json --output P1L4/lrfd_results/validation_results.json
& ./.venv/Scripts/python.exe P1L4/pack_lrfd_mobile.py
```

No reutilizar respuestas sísmicas o unidades LRFD de una geometría anterior. Los hashes vinculan los resultados con su modelo. Los teléfonos consultan estos resultados precalculados; no ejecutan Python/OpenSeesPy.

## Compilar para Android

1. Instalar en Unity Hub **Android Build Support** para **6000.6.0f1**, incluyendo SDK, NDK y OpenJDK.
2. Abrir `P1L4/unity_visualizador` y ejecutar **MCOC → Android → Configurar AR y VR**.
3. Ejecutar **MCOC → AR → Validar configuracion AR** y **MCOC → AR → Validar sector, ajuste y comparacion**.
4. En **File → Build Profiles**, seleccionar Android y cambiar de plataforma. Comprobar que la escena inicial sea `Assets/Scenes/StructuralARScene.unity`.
5. La configuración usa **IL2CPP, ARM64 y OpenGLES3**, con ARCore al inicio y Cardboard al entrar a VR. Revisar identificador de aplicación y ajustes de Player.
6. Conectar un Android compatible con ARCore, habilitar depuración USB y usar **Build And Run**. Para un APK local, desactivar App Bundle y usar **Build**.

Permitir cámara y comprobar seguimiento, colocación I/J, esfuerzos y transición AR/VR. La validación del editor no sustituye la compilación nativa ni la prueba física.

## Compilar para iPhone y generar ZIP de Xcode

1. Instalar **iOS Build Support** para **6000.6.0f1** y abrir `P1L4/unity_visualizador_ios`.
2. Ejecutar **MCOC → iPhone → Configurar ARKit**, revisar el Bundle Identifier y ejecutar **MCOC → iPhone → Exportar Xcode y ZIP**. Si Unity acaba de cambiar de plataforma, esperar la importación y repetir la exportación.
3. Se genera `MCOC_iOS_Xcode_....zip` en `P1L4/unity_visualizador_ios/Entregables/`; la carpeta Xcode temporal queda en `xc/`.
4. Transferir el ZIP Xcode al Mac, descomprimirlo completo y abrir `Unity-iPhone.xcodeproj` en Xcode.
5. En **Signing & Capabilities**, seleccionar Team y configurar firma. Conectar iPhone, habilitar Developer Mode si se solicita, elegirlo como destino y ejecutar **Product → Run**.
6. Permitir cámara y probar AR/VR. El mínimo configurado es iOS 15.0; el teléfono debe ser compatible con ARKit y con el Xcode utilizado.

Se puede exportar Xcode desde Windows con el módulo iOS: **no hace falta Unity en el Mac si se recibe el ZIP Xcode generado**. Compilar, firmar e instalar requiere Xcode en Mac. El ZIP no es una IPA firmada. Después de verificar una nueva exportación, conservar solo el ZIP más reciente.

Guía detallada: [README iOS](P1L4/unity_visualizador_ios/README_IOS.md). Los ZIP, `xc/` y cachés Unity se excluyen de Git y se regeneran localmente.

## Ejecutar tests

Con Python configurado y los JSON actuales disponibles, ejecutar desde la raíz:

```powershell
# Conservación de cargas, entradas inválidas y contraste con OpenSees.
& ./.venv/Scripts/python.exe -m unittest discover -s P1L4/tests -p test_mobile_slab.py -v

# Membresía y compatibilidad de diafragmas y sus maestros.
& ./.venv/Scripts/python.exe P1L4/tests/test_diafragmas.py

# Generar primero el escenario y resultados LRFD indicados arriba.
& ./.venv/Scripts/python.exe P1L4/tests/test_lrfd_analysis.py

# Comparación antes/después con un modelo de prueba aislado.
& ./.venv/Scripts/python.exe P1L4/tests/test_design_comparison.py
```

Los tests deben terminar con código cero y sus mensajes `OK` o `PASS`. Algunos scripts históricos comparan contra checkpoints locales o variantes anteriores: no ejecutar indiscriminadamente toda la carpeta como una suite autocontenida. Por ejemplo, `test_muros_positivos_desktop.py` necesita el checkpoint previo y verifica que los recursos móviles no hayan cambiado respecto de aquel momento.

En Unity, usar los menús de validación AR indicados arriba. Para comprobar configuración por consola, cerrar primero ese proyecto en el editor y ajustar la ruta de Unity si es distinta:

```powershell
$taskUnity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$taskProject = (Resolve-Path P1L4/unity_visualizador).Path
& $taskUnity -batchmode -nographics -projectPath $taskProject -executeMethod StructuralARValidation.ValidateBatch -logFile ./validacion_ar.log
```

Para iOS, utilizar el proyecto `P1L4/unity_visualizador_ios` y el método `IOSBuild.ValidateBatch`. Revisar código de salida y log; estas validaciones no acreditan permisos, cámara, sensores o estéreo en un teléfono.

## Documentación complementaria

- [Guía de defensa y trazabilidad](P1L4/DEFENSA_SEMANA7.md).
- [Visor Unity](P1L4/unity_visualizador/README_Unity.md).
- [Carga móvil](P1L4/CARGA_MOVIL_LOSAS.md).
- [Comparación de diseños](P1L4/COMPARACION_DISENOS.md).
- [Structural X-Ray](P1L4/STRUCTURAL_XRAY.md).
- [Informe semana 6](reports/semana06.md).

Estos comandos corresponden al modelo vigente P1L4. Las guías de etapas anteriores se conservan como referencia histórica.
