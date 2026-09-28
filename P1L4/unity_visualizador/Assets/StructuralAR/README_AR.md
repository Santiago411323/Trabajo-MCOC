# Structural AR — simulacion en Editor

Esta escena usa la estructura y los resultados ya exportados en
`Assets/Resources/estructura_p1l4_unity.json`. No ejecuta OpenSees ni genera
fuerzas nuevas.

## Preparacion automatica

1. Abre el proyecto `P1L4/unity_visualizador` con Unity 6000.6.0f1.
2. Espera a que Package Manager resuelva AR Foundation 6.6.2 y XR Plugin
   Management 4.5.3.
3. Ejecuta `MCOC > AR > Crear o reparar escena Structural AR`.
4. Abre `Assets/Scenes/StructuralARScene.unity`.
5. Presiona Play.

La preparacion crea y selecciona un entorno XR Simulation con el marcador
`MCOC_STRUCTURAL_MARKER`. La camara simulada parte mirando el marcador.

## Prueba

Al reconocer la imagen, el panel debe completar en orden:

`IMAGEN -> POSE -> ANCHOR -> COLUMNA -> ELEMENT TAG -> OPENSEES`.

La columna configurada es `E1_229`. Haz clic sobre ella y cambia entre C1,
C2 y C3. Los valores N, Vy, Vz, T, My y Mz corresponden a la seccion media
y se leen mediante `UnityData.TryGetSectionForces`.

Usa boton derecho + mouse para mirar y WASD/Q/E para mover la camara de XR
Simulation. La columna debe mantenerse fija respecto del marcador.

## Transformacion configurable

En `Structural AR Controller` puedes editar:

- `Uniform Scale`;
- `Position Offset Meters`;
- `Rotation Offset Euler`;
- `Preferred Element Tag` (debe existir exactamente en el JSON y ser columna).

## Validacion estatica

Ejecuta `MCOC > AR > Validar configuracion AR`. La validacion comprueba la
biblioteca, el entorno simulado, los managers de imagen/anchor, el tag, los
nodos y la existencia de resultados C1/C2/C3.

La validacion actual es solo de XR Simulation en Windows. Android requerira
ARCore y un dispositivo compatible; iPhone requerira ARKit, un Mac con Xcode
y un dispositivo iOS.
