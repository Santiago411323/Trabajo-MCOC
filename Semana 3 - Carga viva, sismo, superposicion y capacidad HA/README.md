# Semana 3 - Carga viva, sismo, superposicion y capacidad HA

## Objetivo

Construir los casos base para la interaccion posterior del modelo completo y comenzar la validacion de capacidad de hormigon armado.

El script principal reutiliza el JSON del edificio completo:

```text
P1L2/unity_visualizador/Assets/Resources/estructura_completo_unity.json
```

## Ejecucion

Desde la raiz del repo:

La forma mas facil para Windows es ejecutar este archivo que esta en la raiz del repositorio:

```text
EJECUTAR_SEMANA3.bat
```

Si el repositorio esta descargado en `Descargas`, se puede abrir con `Windows + R` pegando una ruta como esta:

```text
%USERPROFILE%\Downloads\Trabajo-MCOC\EJECUTAR_SEMANA3.bat
```

Si esta en el Escritorio:

```text
%USERPROFILE%\Desktop\Trabajo-MCOC\EJECUTAR_SEMANA3.bat
```

Ese archivo crea `.venv`, instala `openseespy` y `matplotlib`, y abre el menu de Semana 3.

Tambien se puede ejecutar manualmente con PowerShell:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\semana3.py"
```

Para trabajar paso a paso solo la carga viva `Q` y el sismo pseudoestatico `EX/EY`:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --sc-kg-m2 500
```

La forma mas simple es abrir el menu interactivo:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py"
```

Tambien se puede abrir con doble click en:

```text
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/abrir_menu.bat
```

El menu enumera que resultados se pueden pedir y avisa si necesita ID o no:

```text
1. Resumen global Q + sismo              (sin ID)
2. Area tributaria y Q de una viga       (con ID de viga)
3. Area y Q superficial de una losa      (con ID de losa)
4. Sismo EX/EY por piso                  (sin ID)
5. Superposicion R y verificacion        (sin ID, pide lambdas)
6. Capacidad HA Fiber Section            (sin ID)
7. Ejemplos de IDs disponibles           (sin ID)
8. Ruta del JSON completo de resultados  (sin ID)
```

La Parte C tambien se puede correr directamente con la combinacion por defecto:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --sc-kg-m2 500 --superposicion
```

O cambiando los coeficientes de la combinacion:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --sc-kg-m2 500 --superposicion --lambdaG 1.0 --lambdaQ 0.5 --lambdaEX 1.0 --lambdaEY 0.3
```

La Parte D se puede correr directamente con:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --capacidad-ha
```

Al correr la Parte D tambien se exporta para Unity:

```text
P1L2/unity_visualizador/Assets/Resources/semana3_resultados_unity.json
```

En Unity:

```text
1 = diagrama axial
2 = diagrama de corte
3 = diagrama de momento
4 = primeros puntos de curva P-M HA
0 = ocultar diagramas
```

Consulta puntual de una viga:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --sc-kg-m2 500 --id B3002_V60/80
```

Consulta puntual de una losa:

```powershell
& ".venv\Scripts\python.exe" "Semana 3 - Carga viva, sismo, superposicion y capacidad HA\carga_viva_sismo.py" --sc-kg-m2 500 --id L1
```

Salidas:

```text
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/resultados_semana3.json
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/carga_viva_sismo.json
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/fiber_COL70_70.png
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/M_phi_COL70_70.png
Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/P_M_COL70_70.png
```

## Parte A - Carga Viva

Se usa la misma geometria tributaria de Semana 2, leyendo `liveLoad` y `areaTributaria` desde el JSON completo.

Verificacion obtenida:

```text
Area tributaria total = 4005.876 m2
Q transferida = 16809.851 kN
q_Q equivalente = 4.196 kN/m2
q_Q * A = 16809.851 kN
Error = 0.000000 kN
```

Nota: `q_Q` no es unico en todo el edificio porque hay perfiles distintos por nivel. Por eso se reporta un `q_Q equivalente` global y un rango de intensidades por viga.

## Parte B - Sismo Pseudoestatico

Se generan dos casos independientes:

```text
EX: fuerza lateral en X
EY: fuerza lateral en Y
```

Supuesto de masa:

```text
W_piso = D_piso + 0.5 L_piso
F_sismo_piso = 0.20 * W_piso
```

Resultado global:

```text
Corte basal EX = 6560.582 kN
Corte basal EY = 6560.582 kN
```

El JSON de resultados guarda por piso:

```text
D_piso
L_piso
masa equivalente
centro de masa estimado
nodo de aplicacion
torsion estimada respecto del CM
```

## Parte C - Superposicion

Casos base:

```text
G
Q
EX
EY
```

Combinacion arbitraria usada:

```text
R = 1.0 G + 0.5 Q + 1.0 EX + 0.3 EY
```

Se compara contra una corrida explicita equivalente en OpenSees.

Verificacion obtenida con `SC = 500 kg/m2`:

```text
desplazamiento max |OpenSees explicito - superposicion| = 1.401e-11 m
reaccion max       |OpenSees explicito - superposicion| = 3.633e-10 kN
fuerza interna max |OpenSees explicito - superposicion| = 2.871e-12
```

Errores de verificacion obtenidos son numericos de orden maquina:

```text
desplazamiento: ~1e-12 m
reaccion: ~1e-10 kN
fuerza interna: ~1e-12 kN
```

Esto confirma que el modelo lineal cumple superposicion para los casos construidos.

## Parte D - Capacidad HA

Se construye una seccion `Fiber` de columna:

```text
COL70/70_FIBER
b = 0.70 m
h = 0.70 m
fc' = 25 MPa
fy = 420 MPa
400 fibras de hormigon
8 barras de acero
Area por barra = 0.000510 m2
```

Ademas se declara una `ops.section("Fiber", ...)` en OpenSees con:

```text
Concrete01
Steel01
patch rect 20x20
layer straight para 8 barras
```

Graficos generados:

```text
fiber_COL70_70.png: discretizacion de fibras y barras
M_phi_COL70_70.png: curva momento-curvatura aproximada
P_M_COL70_70.png: primeros puntos de interaccion P-M
```

Resultados principales obtenidos:

```text
Seccion: COL70/70_FIBER
b x h = 0.70 x 0.70 m
Hormigon: Concrete01, fc' = 25 MPa
Acero: Steel01, fy = 420 MPa, Es = 200000 MPa
Fibras de hormigon = 400 (20 x 20)
Refuerzo = 8 barras, area por barra = 0.000510 m2
Ast = 0.004080 m2
Cuantia = 0.833 %
Po aproximado = 12039.400 kN
```

Primeros puntos P-M reportados:

```text
P =     0.000 kN, M = 256.373 kN*m
P =  1805.910 kN, M = 606.264 kN*m
P =  3611.820 kN, M = 865.979 kN*m
P =  7223.640 kN, M = 891.056 kN*m
P = 12039.400 kN, M =  26.010 kN*m
```

Interpretacion inicial:

```text
Al aumentar la compresion axial P, aumenta inicialmente la capacidad a momento por mayor bloque comprimido. Al acercarse a Po, la capacidad a momento tiende a bajar hacia cero.
```

## Nota De Modelo

Para estabilizar corridas lineales en OpenSees, el script fija nodos auxiliares aislados y ancla componentes desconectadas sin apoyo. Esos nodos provienen principalmente de paneles de losa/diafragma visuales del JSON completo y no modifican la transferencia tributaria usada para G/Q.
