# Armadura editable de vigas — P1L4

Configuración inicial de V60/80, V40/80, V30/80 y V30/45: 2 barras superiores
y 2 inferiores Ø10, sin barras laterales, As total 314.159 mm². Distancia
al centro longitudinal 50 mm (editable, coherente con el cálculo de fibras).
17 estribos dobles Ø10@100 mm, interpretados como 4 ramas resistentes.
Entre primer y último estribo hay 1.60 m; no se asigna automáticamente ese
tramo a todo el vano ni se define su origen longitudinal.

El editor permite guardar por ID/elementTag dimensiones, materiales,
distribución longitudinal, diámetro, cantidad de estribos, diámetro,
separación y ramas. Se guardan en model_edits.json y se consumen por el
exportador Python. GUARDAR + PREVIEW marca los resultados desactualizados;
REANALIZAR OPENSEES regenera fuerzas/curvas y recarga la escena.

Las vigas muestran Interacción P-M, Sección de fibras, Tensión-deformación,
Diagramas de esfuerzos, Momento-curvatura, Deformada y Trazabilidad.
Las etiquetas longitudinales dejan de asumir 18 Ø25.

P-M nominal y M-Φ a P≈0 se calculan con las rutinas existentes de sección
rectangular, Concrete01/Steel01 y compatibilidad de deformaciones. Eje My.
La demanda de viga se examina en 41 estaciones del vano, incluidos extremos.
No se interpreta el momento biaxial resultante como demanda My.
Si la edición hace distinta la armadura superior/inferior, no se dibuja una
envolvente P-M reflejada falsa; el panel informa que faltan curvas por signo.

Los estribos se dibujan esquemáticamente y se guardan, pero no alteran el
hormigón no confinado ni generan una capacidad de corte inventada. El modelo
global sigue elástico; variar armadura actualiza capacidad, no convierte el
edificio en un modelo no lineal. Con 4 Ø10 algunas vigas exceden capacidad
desde gravedad; no se fuerza un estado OK. La armadura inicial no es un diseño
aprobado.

Validación: test_armadura_vigas.py PASS para las cuatro secciones, polos P-M,
As, curvas finitas, estribos y edición individual conservando los otros
elementos. actualizar_armadura_vigas.py preservó exactamente geometría,
cargas, fuerzas, combinaciones y desplazamientos del JSON actual. Evaluador
C# de daño: 25101 comprobaciones PASS, E1_1 excede nominal desde frame 0.
Se regeneraron los cuatro casos sísmicos X y el caso Y/1.00x para el nuevo
hash de datos; las respuestas elásticas máximas permanecen iguales.

Prueba visual en Editor 6000.6.0f1: viga B3044_V60/80.1, ID401. Inspector
con 4 Ø10 y As314.2; menú completo, sección de fibras, P-M y M-Φ verificados.
Curva M-Φ de fibras muestra M máximo49.674 kN·m, igual al archivo.
P-M muestra demanda324.298 kN·m y C6.225 (no cumple). Editor muestra
2/2/0, Ø10, recubrimiento al centro50 mm, estribos17/Ø10/100mm/4ramas.
Console: 0 errores, 0 warnings. No se pulsó GUARDAR/REANALIZAR en la prueba
visual para preservar la configuración inicial; el contrato de edición se
comprobó en Python con un archivo temporal.
