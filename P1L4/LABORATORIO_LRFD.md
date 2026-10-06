# Laboratorio LRFD — visualización y evaluación independiente

En Play: Capas → **Método LRFD**. El botón está fijo bajo el de sismos.
Los controles se abren dentro del scroll de Capas; no hay una ventana flotante
adicional. Todos los efectos y flechas se ocultan al cerrar el laboratorio.

## Demostraciones visuales

«Demo lluvia», «Demo nevada» y «Demo viento» configuran intensidades visibles.
«Ver las 7 cargas» configura valores no nulos de todas las acciones; nieve
y agua retenida pueden coexistir, pero no se emiten lluvia y nieve juntas.
Estos presets no aplican esfuerzos automáticamente ni seleccionan una
combinación certificada. La leyenda y los controles muestran sus valores.

Cada acción tiene filtro de visibilidad. La opción de símbolos de carga cero
mantiene flechas/rótulos grises para explicar dónde actuaría una acción
ausente, sin representarla como fuerza no nula. Ocultar una acción es solo
visual: no cambia sus valores en una combinación.

D se representa con pesos simbólicos; L con personas en pisos; Lr con
mantenimiento de cubierta. Su cantidad es una representación didáctica,
no un cálculo de masa por persona ni un cambio del modelo.
Los rótulos 3D muestran las siete acciones, sus intensidades y unidades.
La opción de abrir fachada/techo ayuda a ver cargas interiores y restaura
la visibilidad anterior al cerrar el laboratorio.

Lluvia y nieve activan una cubierta de nubes procedurales en una sola malla,
cielo nublado, menor luz solar y bruma ligera. La transición es gradual.
Las nubes derivan sobre la huella del modelo y siguen la dirección visual
del viento. Se guardan y restauran cámara, iluminación ambiental, sol y
niebla al cerrar, deshabilitar o suspender el laboratorio por un sismo.
Los efectos son gráficos; no calculan sombra, presión o cargas adicionales.

## Acciones y unidades

- D y L: factores de intensidad independientes para casos base G/Q ya
  exportados. No cambian por sí solos los resultados; usar «Aplicar demanda
  D/L disponible» para actualizar la superposición estática del visualizador.
- Lr: sobrecarga de cubierta kN/m² y flechas amarillas.
- S: espesor de nieve en cm y densidad en kg/m³; q=h·ρ·9,80665/1000 kN/m².
- R: altura de agua retenida en mm; q=h·9,80665 kN/m². No se confunde
  intensidad de precipitación con carga instantánea. Modelo visual sin drenaje.
- W: velocidad m/s, dirección y Cp editable; presión de referencia
  q=0,613·v²·Cp/1000 kN/m². No es presión normativa de fachadas validada.
- E: factor visual de flechas sísmicas, sin sacudida ficticia. No es magnitud
  Richter ni aplica factores al registro. Para respuesta dinámica real usar
  el módulo de simulación sísmica existente.

Lluvia y nieve tienen emisores excluyentes. Cambiar clima conserva la
acumulación previa; «Limpiar acumulación» elimina nieve/agua y pausa la
acumulación. El tiempo ambiental acelerado se indica explícitamente.
Una precipitación de 50 mm/h durante 60 s a 120x acumula 100 mm si no hay
drenaje. El espesor mostrado utiliza metros Unity reales, sin amplificarlo.

Las superficies acumuladas siguen las piezas de cubierta existentes
VisualFlatRoof, sin inventar un plano que conecte edificios separados.
Las flechas son símbolos didácticos de acción, no vectores de fuerza nodal
exportados. No tienen colisiones ni participan en la selección estructural.
Los efectos quedan suspendidos durante la reproducción sísmica.

## Combinaciones y alcance

### Resultados U1–U7

En Método LRFD → **Resultados U1–U7** → **Evaluar las 7 combinaciones**.
Configurar primero el escenario en la pestaña de clima y seleccionar una
viga/columna. El análisis pausa la acumulación para evaluar una instantánea.
La tabla informa P compresión positiva, My/Mz y Vy/Vz en la estación
concomitante que gobierna D/C P–My (41 muestras). No son cinco máximos
independientes de cinco posiciones distintas. Si falta capacidad, elige
la mayor demanda |My| y muestra pendiente en lugar de un estado seguro.

Se evalúan 22 variantes: alternativas Lr/S/R en U2/U3/U4, L o W como
acompañante en U3 y ±EX/±EY separados en U5/U7. No se selecciona la
variante más crítica comparando presiones; se comparan las respuestas
del elemento. La combinación gobernante se destaca. Pulsar una fila o
«Mostrar gobernante» aplica el caso independiente a los diagramas y
desplazamientos existentes. «Recorrer U1–U7» cambia de fila cada cuatro
segundos; no es una historia sísmica ni una progresión de daño.

`lrfd_analysis.py` crea casos OpenSees independientes para cubierta a
1 kN/m² y viento en cuatro sentidos. Reutiliza las funciones de armado
del modelo y los casos exportados G/Q/EX/EY, sin editar esos análisis.
Verifica convergencia y equilibrio de cargas/reacciones. Solo consulta
localForce en barras existentes del dominio y nodeDisp en nodos nativos,
evitando consultas de respuesta de barra a shells o nodos huérfanos.
Las unidades se cachean con hashes de modelo, catálogo y módulo.

Cubierta: se sustraen las superficies tapadas por losas superiores, se
respetan aberturas y se reparte la presión por las áreas receptoras del
catálogo. Para exposición parcial, se conservan proporcionalmente las
fracciones receptoras del panel entero. La carga equivalente uniforme
va a las vigas con eleLoad, conservando peso total; esta aproximación no
resuelve una placa con presión parcial.
Viento: presión de referencia en fachada envolvente rectangular por
edificio, distribuida uniformemente a los nodos de la cara a barlovento.
Ángulo positivo se transforma coherentemente con Unity X/Z y OpenSees
X/Y. No incluye succión de techo, presiones internas ni un estudio
aerodinámico normativo. E se define expresamente como ±EX/±EY exportados;
no se afirma que cumpla toda la definición normativa de E.

Capacidad de referencia: sección rectangular Whitney + acero
elastoplástico a εcu=0,003, compatibilidad de deformaciones, φ variable
0,65–0,90 según εt y dos ramas independientes por signo de My.
Se reducen **P y M** en cada punto: no solo M de toda la curva con 0,9.
Para columnas se aplica el límite axial de referencia de estribos
0,8·0,65·Po. Se interpolan las envolventes para obtener D/C.
El resultado «Excede P–My» significa que excede ese criterio parcial:
no certifica falla completa ni colapso. No incluye cortante, torsión,
esbeltez, efectos de segundo orden, interacción biaxial ni todas las
exigencias sísmicas/detallado. La armadura mínima/máxima se consulta en
Sección de fibras y no se reemplaza con esta comprobación de resistencia.

Los archivos se escriben en `lrfd_results/`, excluido de Git: configuración,
caché de unidades y `resultados_unity.json`. Se rechazan resultados de otro
hash de modelo; modificar intensidades o actualizar el modelo invalida la
evaluación hasta recalcular. C1/C2/C3 y JSON de base permanecen intactos.
Los casos LRFD se registran únicamente en las tablas de datos de la sesión.
No se añaden incrementos móviles no incluidos en el escenario. El radar
nominal P–My se pausa en casos LRFD para no mezclar sus criterios.

Las siete fórmulas corresponden a los **apuntes aportados por el usuario**;
no se presentan como adopción certificada de una edición normativa.
A=Lr/S/R es una alternativa seleccionable, nunca la suma de las tres.
La tercera fórmula permite seleccionar L o 0,8W como acompañante.
Los factores de mayoración son distintos de la intensidad física de carga.

El proyecto base exporta G/Q/EX/EY. Para Lr, S, R y W se requieren las
respuestas independientes descritas arriba; no se inventan a partir de
los efectos ambientales. El botón rápido de la pestaña Escenario solo
admite D/L. Para acciones nuevas se usa la pestaña Resultados U1–U7.
Las cargas visibles que no pertenecen a esa combinación no se agregan a ella.

Si solo D/L son no nulas, se permite superponer la respuesta lineal base
con γD·intensidadD y γL·intensidadL. No ejecuta ni modifica análisis previos,
JSON, geometría, armaduras ni presets exportados C1/C2/C3. Los diagramas
existentes muestran esa superposición en la barra superior.

La capacidad P–My φRn se evalúa como referencia independiente; no se adopta
φ=0,9 para todos ni se convierte el radar nominal en una verificación LRFD.
Falta confirmar/adoptar normativa, ampliar las acciones idealizadas y
completar las verificaciones de diseño antes de certificar el proyecto.

## Pruebas

`tests/verificar_armadura_seccion.ps1` incluye conversión de nieve/agua/viento,
acumulación con tiempo acelerado, las siete fórmulas, exclusividad Lr/S/R y
rechazo de combinaciones con respuestas ausentes. También compila el runtime
completo. La prueba visual en Editor y física en teléfono es independiente.

`tests/test_lrfd_analysis.py` contrasta U1 exacta y U4 con análisis OpenSees
directo, conserva el hash del modelo y verifica 22 variantes finitas.
Ejecutar primero lrfd_analysis con validation_scenario.json para generar
validation_results.json. Las pruebas C# comprueban también reducción de
capacidad, límite axial y rechazo de demanda fuera de envolvente.
