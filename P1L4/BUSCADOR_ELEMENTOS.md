# Buscar elementos por ID o elementTag

En `StructureViewerScene`, pulsar Play. El campo **BUSCAR ELEMENTO · ID / TAG**
queda en la parte fija superior de Capas; los filtros y coincidencias están al
inicio de su contenido desplazable.

1. Escribir un ID numérico, `elementTag` o identificador de origen. Ejemplos:
   `72`, `E1_72`, `E1_229`, o el ID completo de una losa.
2. Elegir **Todos / Vigas / Columnas / Muros / Losas**. Las coincidencias se
   actualizan al escribir; los tags no distinguen mayúsculas/minúsculas.
3. Pulsar **BUSCAR** o Enter: si hay una única coincidencia, se selecciona.
   Con varias, elegir la fila por tipo, ID, tag, piso y edificio.
4. Al seleccionarla, se activa su capa, el filtro de piso vuelve a Todos,
   se abre su información en el inspector y se centra suavemente la cámara.
   Las vigas/columnas/muros conservan la selección amarilla existente;
   las losas usan su resaltado y reparto tributario existentes.

Un número puede ser ID de una viga y de un muro: se muestran ambos, sin elegir
arbitrariamente. Las coincidencias exactas de ID/tag/origen tienen prioridad
sobre las parciales. Una búsqueda parcial muestra todos los candidatos en
páginas de cinco. `×` limpia la consulta. No modifica cargas ni resultados.

Buscar incluye objetos ocultos, no solo lo que se ve en pantalla. Mientras
se escribe en el campo, los atajos de diagramas y navegación quedan suspendidos;
al pulsar Buscar, seleccionar o hacer click fuera, se libera el teclado.
La búsqueda está deshabilitada durante la reproducción sísmica y no se incorpora
a la escena AR. Un recorrido de radar activo se detiene al elegir otro elemento.

Implementación: `StructuralSearchIndex.cs`, `StructuralElementSearch.cs` y
los puntos de integración de `StructureViewer`, `ElementPicker` y `OrbitCamera`.
El índice se reconstruye al recargar el modelo, incluidas las ediciones.

Verificación: `tests/verificar_armadura_seccion.ps1` incluye casos de búsqueda
exacta, parcial, ambigua, por tipo, losa, origen e ID inexistente, y compila el
runtime de escritorio. La prueba de controles en el Editor es independiente.
