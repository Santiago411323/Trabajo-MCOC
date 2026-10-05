# Actualización Android AR/VR — 5 de octubre de 2026

Se incorporó al proyecto `P1L4/unity_visualizador` el modo VR desarrollado para iPhone, adaptando la activación nativa de Cardboard a Android. Se conserva ARCore para el inicio de AR y se inicia Cardboard solamente al entrar a VR. No se importó el código de permisos de cámara de iOS ni se modificó el controlador de cámara/AR de Android.

## Funciones incorporadas

- Botón «Modo VR · edificio» en la aplicación AR y retorno a AR.
- Cámara en primera persona que recibe la rotación del teléfono mediante `centerEyeRotation`.
- Desplazamiento a 1,8 m/s con mirada sostenida en controles de dirección; mirar fuera detiene el movimiento. No requiere control Bluetooth y no incluye «Probar control».
- Subir/bajar pisos con límites y posición inicial dentro de una losa transitable.
- Menú que sigue al usuario, selección por mirada de vigas/columnas, identificación I/J y diagramas de resultados OpenSees con C1/C2/C3.
- Ambiente completo: las tres plataformas, pendiente de roca, muros salmón, techos y las dos escaleras, con controles de visibilidad.
- Materiales de ambiente y menú compatibles con representación estéreo.
- Escaneo de QR del visor y recentrado mediante Cardboard.

Android mantiene las mejoras AR existentes: menú inferior contraíble, colocación guiada, ajuste fino, varios elementos, comparación de combinaciones y signos de momento corregidos. También conserva su guía de transmisión de carga de losas, que no existía en la copia iPhone. Los JSON estructurales y de cargas coinciden entre ambos proyectos e incluyen las 444 vigas corregidas y todos los casos recalculados.

## Configuración y uso

Se reutiliza exactamente el plugin oficial Google Cardboard ya usado en iPhone: commit `36ac9815b8f191fe11e149b7f323368fa86655a6` (1.35.0). La configuración Android utiliza ARM64, IL2CPP, OpenGLES3, entrada Activity y frame pacing desactivado. ARCore permanece primero en la lista de proveedores. La orientación AR se conserva; al entrar a VR se permiten las dos orientaciones horizontales y al salir se restaura la orientación anterior.

El menú **MCOC → Android → Configurar AR y VR** prepara los proveedores y la escena para la aplicación. También se ejecuta al preparar una compilación Android. Un procesador añade las dependencias Cardboard y AndroidX al Gradle generado, sin reemplazar plantillas de ARCore. Referencia de configuración: [guía oficial Google Cardboard para Unity](https://developers.google.com/cardboard/develop/unity/quickstart).

Para producir el APK se requiere instalar **Android Build Support, SDK/NDK y OpenJDK** para Unity 6000.6.0f1; esos módulos no están instalados en este equipo. Después se selecciona Android en Build Profiles y la escena `Assets/Scenes/StructuralARScene.unity`.

## Verificación

La importación de paquetes y compilación de scripts en el editor terminó correctamente. `StructuralVRValidation.ValidateBatch` pasó:

- Flujo AR, selección y sector con dos elementos.
- Signos de diagramas My/Mz y comparación de combinaciones.
- Rotación de cabeza/cámara conservando posición.
- Recorrido con mirada, velocidad 1,8 m/s, obstáculos y límites de losa.
- Menú que sigue posición y giro, cambios de piso y límites.
- Ambiente completo y ocultación/restauración.
- ID 94, nodos 61→60 y resultado C1 de E1_94 del modelo recalculado.
- Dos ciclos AR → VR → AR.

Log de editor: `P1L4/unity_visualizador/Logs/android-vr-validation.log` (generado, no versionado). No se generó APK ni se probó el seguimiento/estéreo nativo en un teléfono Android durante esta actualización.
