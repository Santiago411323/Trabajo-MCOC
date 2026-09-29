# Corrección de lectura de fuerzas en Unity

Se corrigió la parte Unity de los hallazgos de `auditoria_diagramas.md`, conservando la distribución de paneles y los botones My/Mz/Vy/Vz/Axial. No se cambiaron los scripts de análisis, el JSON, las cargas ni las conexiones de OpenSees.

## Cambios

- `UnityData` convierte las acciones globales del JSON legado a acciones locales una sola vez al cargar. Conserva los registros originales sin modificarlos. La base local reproduce `geomTransf` de P1L3 y utiliza los nodos analíticos.
- `FrameForces` centraliza la evaluación de N, Vy, Vz, T, My y Mz con equilibrio y signos de extremo. N es positivo en tracción. El panel nuevo, la información de selección y las tablas consumen esta misma evaluación.
- Se eliminó la interpolación directa entre acciones nodales de signo opuesto y el agregado de una parábola desde `uniformLoad`. Con factores G/Q/EX/EY nulos, todos los esfuerzos son cero.
- La longitud analítica y los ejes originales se utilizan para fuerzas incluso cuando las columnas tienen ajustes visuales de cota.
- P-M utiliza axial local con compresión positiva y momentos locales. Las combinaciones guardadas se consultan por separado de los factores interactivos.
- La información de restricciones consulta los apoyos declarados. Para el formato legado también identifica las restricciones adicionales de P1L3, marcándolas como **inferidas**. No asigna un empotramiento al suelo a cada unión viga-columna.
- La carga móvil se mantiene como simulación local separada. Su base usa Vz/My para flexión vertical y N positivo en tracción; ya no se suma un escalar de carga móvil a una resultante biaxial. La información del elemento y el panel seleccionado muestran la combinación OpenSees, sin incorporar silenciosamente esa simulación.
- Los datos ausentes, no finitos o con un sistema de coordenadas desconocido no se sustituyen con aproximaciones de la geometría. Un exportador futuro puede declarar `p1l4.elementForceCoordinates="local"` o `"global"`; los datos locales no se rotan dos veces. Ese metadato no se ha añadido al JSON actual, que sigue siendo global.

## Verificación realizada

Se ejecutó el código C# real de `UnityData`, `FrameForces` y `StructureData` contra referencias nuevas de `ops.eleResponse(id, 'localForce')`, obtenidas reproduciendo el modelo sin escribir resultados de producción.

- 462 barras × 7 casos/combinaciones = **3.234 registros**.
- Acciones locales y seis esfuerzos internos evaluados en I, 25%, centro, 75% y J.
- Combinaciones C1/C2/C3 reconstruidas por superposición; factores negativos y todos los factores en cero.
- Restricciones mostradas contrastadas con los **303 nodos efectivamente fijados** por OpenSees: 26 declarados, 25 anclajes de componentes desconectadas y 252 nodos aislados.
- Casos sin datos, exportación explícitamente local, coordenadas desconocidas, NaN y longitud nula.
- Caso patrón de viga restringida en flexión, L=6 m y q=10 kN/m: My de extremo -30 kN·m, My central +15 kN·m y Vz de extremo ±30 kN según la convención documentada.
- **203.882 comprobaciones aprobadas**. Diferencia máxima numérica observada: **0,000276898**; tolerancia absoluta 0,01 kN o kN·m. La pequeña diferencia corresponde a la precisión `float` usada por los datos Unity frente a OpenSees en doble precisión.
- Compilación de todos los scripts del visualizador con el compilador y las referencias de Unity 6000.6.0f1. La interacción y presentación en Play no se comprobaron visualmente en esta ejecución.

Regresiones comprobadas: E1_84/C1 da Mz=-8,5362 kN·m en el centro; E1_272/C1 da N=-2620,7559 kN (compresión).

Para repetir la comprobación numérica en Windows:

```powershell
./P1L4/tests/verificar_fuerzas_unity.ps1 -Python "ruta/al/python.exe"
```

El Python utilizado debe ser compatible con los paquetes del proyecto (Python 3.12). El script permite especificar también `-EditorData` y utiliza una carpeta temporal para compilados y referencias, sin modificar los resultados de producción.

## Pendiente en Python/OpenSees

Unity ahora representa los esfuerzos del modelo actual. Permanecen las cargas gravitacionales concentradas en nodos, las 25 vigas desconectadas con anclajes automáticos y la ausencia de rigidez de muros/diafragmas en la corrida P1L3. Esto exige revisar el modelo y reanalizar; no puede resolverse mediante signos o curvas del visualizador. Por ese motivo, bajo las cargas nodales actuales es esperable ver cortes constantes y momentos lineales en cada barra.

Para usar la corrección no es necesario regenerar el JSON: salir de Play, actualizar Assets y volver a Play.
