# Semana 3 — Avance: casos base y curvas de interacción

**Proyecto:** modelo estructural UANDES, Edificio 1.  
**Unidades:** kN, m, kN·m, rad.

Los resultados numéricos de este informe provienen de `P1L2/Edificio 1 y 2/semana3/resultados/`. El visualizador y los resultados del edificio completo se mantienen en la carpeta `Semana 3 - Carga viva, sismo, superposicion y capacidad HA/`.

## 1. Casos base

Se construyeron cuatro casos lineales independientes sobre el mismo modelo OpenSees:

| Caso | Descripción | Aplicación |
|---|---|---|
| `G` | Carga permanente | Peso propio, losa y terminaciones según el modelo de Semana 2 |
| `Q` | Carga viva | Carga uniforme por viga usando áreas tributarias |
| `EX` | Sismo pseudoestático X | Fuerzas laterales en dirección X |
| `EY` | Sismo pseudoestático Y | Fuerzas laterales en dirección Y |

Los casos se ejecutan con `base_cases.py`, `part_a_live.py` y `part_b_seismic.py`. La respuesta de una combinación se obtiene sin modificar la geometría ni las propiedades entre corridas.

## 2. Carga viva

Se reutilizó la misma geometría tributaria de Semana 2. Para cada viga se lee el área tributaria y se aplica la carga lineal equivalente:

```text
w_Q = q_Q · A_tributaria / L_viga
```

La conservación se verificó comparando la carga aplicada a las vigas con `q_Q·A`:

| Magnitud | Resultado |
|---|---:|
| Área total tributaria | 2480.308 m² |
| Carga viva transferida | 10706.003 kN |
| Carga viva esperada `q_Q·A` | 10706.003 kN |
| Error absoluto | 3.64·10⁻¹² kN |
| Error relativo | 3.40·10⁻¹⁴ % |

Las intensidades usadas fueron 4.905 kN/m² en CIELO_1S a CIELO_3 y 1.962 kN/m² en CIELO_4. La conservación confirma que el reparto de áreas tributarias se reutiliza sin pérdida de carga.

## 3. Sismo pseudoestático

Se adoptó:

```text
W_i = D_i + 0.5 Q_i
F_i = C · W_i
C = 0.20
```

La fuerza se aplica en el centro de masa del diafragma de cada piso. Los pesos y fuerzas por piso son:

| Piso | `W_i` [kN] | `F_i` EX/EY [kN] | Centro de masa `(x,y,z)` [m] |
|---|---:|---:|---|
| CIELO_1S | 4306.733 | 861.347 | (18.234, 6.742, −4.01) |
| CIELO_1 | 4306.733 | 861.347 | (18.234, 6.742, −0.05) |
| CIELO_2 | 4306.733 | 861.347 | (18.234, 6.742, 3.91) |
| CIELO_3 | 4306.733 | 861.347 | (18.234, 6.742, 7.87) |
| CIELO_4 | 3284.796 | 656.959 | (18.234, 6.742, 11.83) |
| **Total** | — | **4102.346** | — |

### Resultados globales

| Magnitud | EX | EY |
|---|---:|---:|
| Fuerza lateral total | 4102.346 kN | 4102.346 kN |
| Corte basal por reacciones | 4102.346 kN | 4102.346 kN |
| Error de corte basal | 7.55·10⁻¹¹ kN | 5.00·10⁻¹¹ kN |
| Desplazamiento máximo de techo | 0.03502 m | 0.00571 m |
| Rotación máxima de diafragma `rz` | 1.7·10⁻⁵ rad | 1.28·10⁻⁴ rad |

El edificio es aproximadamente seis veces más rígido frente a `EY`, debido a la distribución de muros en esa dirección. La mayor rotación aparece en `EY`, consistente con la asimetría de rigidez respecto al centro de masa.

## 4. Superposición

Se evaluó la combinación:

```text
R = 1.0 G + 0.5 Q + 0.3 EX + 0.2 EY
```

Se comparó la suma ponderada de cuatro corridas independientes contra una corrida explícita de OpenSees con los cuatro patrones simultáneos:

| Respuesta comparada | Error máximo |
|---|---:|
| Desplazamientos | 7.15·10⁻¹⁶ m |
| Reacciones globales | 8.89·10⁻¹¹ kN |
| Reacciones por nodo | 4.59·10⁻¹¹ kN |
| Fuerzas internas | 1.59·10⁻⁸ kN |

La corrida explícita convergió y el resultado se marcó como válido. Los errores son numéricos y están dentro de la tolerancia `1e-6` usada por el verificador.

## 5. Momento-curvatura

La sección representativa es la columna `COL70/70_FIBER`:

- Sección: 0.70 × 0.70 m.
- Hormigón: `Concrete01`, `f'c = 30 MPa`.
- Acero: `Steel01`, `fy = 420 MPa`, `Es = 200 GPa`.
- Armadura: 8 barras de diámetro 25 mm, `A_s = 3927 mm²`, cuantía 0.801 %.
- Discretización vigente: 20 × 20 fibras de hormigón, 400 fibras.

Para cada curvatura `φ` se busca la deformación axial que equilibra el axial objetivo y luego se integra el momento de las fibras:

```text
P = Σ σ_i A_i
M = Σ σ_i A_i y_i
```

Con `P = 0`, la curva alcanza aproximadamente `M = 654.4 kN·m`. Con `P = 0.2 Pn0 = 2499 kN`, alcanza `M = 1238.1 kN·m`.

La rigidez inicial se obtiene de la pendiente inicial `dM/dφ`; para `P = 2499 kN` los primeros puntos son aproximadamente `(φ,M)=(0.00024,144.3)` y `(0.00048,288.4)`, por lo que la pendiente secante inicial es cercana a `6.0·10⁵ kN·m²`.

El criterio de término de la curva vigente es `φ = 0.12 1/m` en el script de la sección de fibras. Para el modelo de capacidad HA del edificio completo se extendió la curva hasta `φ = 0.080 1/m`; la primera fluencia estimada es:

```text
ε_y = fy/Es = 0.00210
φ_y = ε_y/d' = 0.00420 1/m
M_y ≈ 425.9 kN·m
```

La discretización actual es 20×20. La sensibilidad debe compararse corriendo 10×10, 20×20 y 40×40 con los mismos materiales y tolerancias; el resultado versionado contiene la corrida 20×20, por lo que esa comparación aún queda como control numérico pendiente.

## 6. Curva P-M de columna

La envolvente se obtiene fijando varios niveles de compresión axial `P`, resolviendo la compatibilidad de deformaciones en la sección de fibras y guardando el máximo momento alcanzable para cada `P`.

Puntos representativos de `COL70/70_FIBER`:

| `P` [kN] | `P/Pn0` | `Mmax` [kN·m] |
|---:|---:|---:|
| 0.0 | 0.00 | 654.4 |
| 1249.5 | 0.10 | 987.7 |
| 2499.0 | 0.20 | 1238.1 |
| 3748.5 | 0.30 | 1409.5 |
| 5622.8 | 0.45 | 1525.0 |
| 7497.0 | 0.60 | 1502.4 |

La capacidad aumenta con compresión moderada por el mayor bloque comprimido y luego comienza a disminuir al acercarse a la compresión pura.

## 7. Curva P-M de muro

Los muros están implementados actualmente como **elementos equivalentes elásticos** (`wall_section_props`), con propiedades `A`, `Iy`, `Iz` y `J`. El modelo no contiene todavía armadura longitudinal/transversal ni una sección `Fiber` de muro, por lo que no es válido presentar una envolvente RC P-M numérica como si estuviera calculada.

La envolvente que corresponde implementar en la dirección principal del muro es:

1. Seleccionar el muro de mayor longitud, `t = 0.20 m`, `L = 3.40 m`.
2. Discretizar la sección `t × L` en fibras.
3. Definir `Concrete01` y las armaduras reales del muro.
4. Fijar sucesivamente `P` y resolver el equilibrio axial.
5. Integrar el momento en la dirección principal y obtener `Mmax(P)`.

Por tanto, esta sección queda identificada como **pendiente del avance actual**; los valores existentes del muro corresponden a rigidez elástica equivalente, no a capacidad última de hormigón armado.

## 8. Verificación de hormigón armado

Para la columna 70×70 se compararon puntos de la fibra con una estimación simplificada mediante bloque rectangular de compresión. El cálculo simplificado usa `f'c`, `A_g`, `A_s`, `fy`, equilibrio de fuerzas y brazo interno; la curva de fibras incorpora compatibilidad y la ley constitutiva completa.

| Punto | Fibra `P` [kN] | Fibra `M` [kN·m] | Referencia simplificada [kN·m] |
|---|---:|---:|---:|
| Flexión pura | 0 | 654.4 | 517.1 |
| Compresión moderada | 2499 | 1238.1 | aumenta respecto a flexión pura |
| Zona balanceada | 5623 | 1525.0 | 1452.5 en el cálculo nominal |
| Compresión pura | 12495 aprox. | 0 | `0.85 f'c A_g = 12495 kN` |

La diferencia entre fibra y cálculo simplificado es esperable: el bloque nominal usa una distribución idealizada, mientras que las fibras integran deformaciones y tensiones en toda la sección. La comparación no debe mezclar directamente los resultados H-25 del cálculo manual anterior con la corrida H-30 de fibras sin indicar el cambio de material.

## 9. Primera demanda-capacidad

Se eligió la columna `E1_284`, ubicada a `z = 2.0 m`. Para la combinación de demanda se usó:

```text
R = 1.0 G + 0.5 Q + 0.3 EX + 0.2 EY
```

La demanda extraída es:

```text
P_d = 343.26 kN
M_d = 760.49 kN·m
```

La capacidad de momento disponible reportada es `φM_n = 536.86 kN·m`. Por tanto:

| Indicador | Resultado |
|---|---:|
| Utilización axial | 0.044 |
| Utilización a flexión | 1.417 |
| Utilización total | 1.417 |
| Veredicto | **NO CUMPLE** |

El punto `(P_d,M_d)` queda por encima de la capacidad disponible en flexión. La verificación completa evaluó 89 columnas y encontró 17 que no cumplen; `E1_284` es la columna crítica reportada.

## 10. Uso de IA

Durante el desarrollo, el agente propuso inicialmente representar los huecos de losa como un recorte rectangular fijo. El grupo revisó la propuesta contra la geometría real de los muros y detectó que el recorte no coincidía con sus huellas en planta.

La convención corregida fue generar los huecos a partir del rectángulo envolvente de cada muro, subdividir las celdas en los bordes del hueco y agregar nodos auxiliares antes de eliminar nodos sin elementos. Esta revisión evitó contar losas fuera de su posición arquitectónica y dejó explícita la trazabilidad entre muros, paneles y nodos.

## Archivos de reproducción

- `P1L2/Edificio 1 y 2/semana3/base_cases.py`
- `P1L2/Edificio 1 y 2/semana3/part_a_live.py`
- `P1L2/Edificio 1 y 2/semana3/part_b_seismic.py`
- `P1L2/Edificio 1 y 2/semana3/part_c_superposicion.py`
- `P1L2/Edificio 1 y 2/semana3/part_d_fiber.py`
- `P1L2/Edificio 1 y 2/semana3/resultados/part_a_carga_viva.json`
- `P1L2/Edificio 1 y 2/semana3/resultados/part_b_sismo.json`
- `P1L2/Edificio 1 y 2/semana3/resultados/part_c_superposicion.json`
- `P1L2/Edificio 1 y 2/semana3/resultados/part_d_fiber.json`
- `Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/M_phi_COL70_70.png`
- `Semana 3 - Carga viva, sismo, superposicion y capacidad HA/resultados/P_M_COL70_70.png`
