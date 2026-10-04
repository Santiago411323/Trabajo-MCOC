# Perfil provisional para VR Box

Este QR reutiliza el perfil estándar Google Cardboard I/O 2015, incluido como ejemplo en el SDK oficial. No contiene especificaciones medidas de este VR Box ni constituye una calibración para el iPhone 15 Pro.

## Uso

1. Abrir `VR_Box_perfil_provisional.png` en un computador u otra pantalla, o imprimirlo sin recortar su margen blanco.
2. En la aplicación del iPhone, entrar a VR y abrir el escáner QR.
3. Escanear el código antes de introducir el teléfono en el visor.
4. Centrar el teléfono, ajustar la separación de lentes y después la profundidad hasta enfocar.
5. Comprobar que se ve una sola imagen y que el edificio no presenta una deformación excesiva. Si no se logra, revisar el perfil con medidas y calibración; no forzar la vista.

El perfil se guarda por Cardboard. No es una imagen de referencia AR y no cambia los resultados estructurales. No hace falta recompilar ni reemplazar el ZIP de Xcode para escanearlo.

Fuente: https://developers.google.com/cardboard/reference/unity/class/Google/XR/Cardboard/Api
Perfil original: Google Cardboard I/O 2015, ejemplo en `Google.XR.Cardboard.Api.SaveDeviceParams` del SDK 1.35.0.
