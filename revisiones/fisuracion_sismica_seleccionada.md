# Indicadores y fisuración ilustrativa del elemento seleccionado

## Alcance

Se añade al panel sísmico existente la selección por ID/tag, chequeo de capacidad
nominal disponible, estimación de primera fisuración, primeros instantes críticos
y una animación superficial sobre viga, columna o muro equivalente seleccionado.
No se modifica el modelo OpenSees ni sus desplazamientos/fuerzas temporales.

El modelo sigue siendo elástico. Una excedencia de criterio no equivale a
fractura física. Los trazos de grietas son una ilustración controlada por
los umbrales calculados; no representan anchos ni trayectorias físicas.

## Criterios

- Acciones locales resistentes del Transient, con G+0.5Q incluido.
- Equilibrio local verificado antes de reconstruir esfuerzos interiores; válido
  para masas nodales, barras sin masa distribuida y gravedad uniforme actuales.
- 41 estaciones para estimar máxima tensión elástica de sección bruta y demanda.
- Fisuración estimada: `sigma_t > 0.63 sqrt(fc)` [MPa], hormigón de peso normal.
- P-M: exclusivamente curvas exportadas compatibles con geometría y materiales,
  conservando límites axiales y capacidades nulas en los polos.
- La columna de 18 barras 5/5/4/4 no tiene simetría al girar 90 grados. Su curva
  disponible se usa en P–My, sin afirmar capacidad P–Mz ni interacción biaxial.
- Muros: eje fuerte en el plano; se declara la armadura supuesta de las curvas.
- Vigas sin acero/capacidad exportada: capacidad última NO DISPONIBLE.
  No se adopta As mínima como si fuera la armadura existente.
- Umbrales de inspección DCR: 0.60, 0.85 y excedencia estricta >1+1e-6.
  No son niveles de daño calibrados. Corte, torsión y estabilidad no evaluados.

## Historial y visualización

Los esfuerzos y DCR son de cada muestra original. Las grietas ilustrativas usan
los máximos anteriores al instante consultado. Al retroceder en el tiempo no se
conservan eventos futuros. Cambiar caso/selección limpia el dibujo. La escala
deformada modifica solo posiciones visuales, sin alterar los criterios numéricos.

Nueve estaciones y cuatro caras, patrón gráfico determinista, crecimiento suave
en las regiones de tracción estimadas. El contorno amarillo identifica selección;
una banda roja señala excedencia P-M. No se generan piezas sueltas ni colapso.
Si el primer umbral ocurre en t=0 se identifica la gravedad como estado inicial.

## Pruebas numéricas

`P1L4/seismic/validate_damage.ps1` compila el código C# real, independiente de Unity.
PASS: 25101 comprobaciones de unidades, soluciones manuales, polos axiales,
capacidades ausentes, signos de caras, historia temporal y equilibrio de fuerzas
reales de 1.50x/X. Incluye una viga con máximo interior bajo carga uniforme.

Casos de demostración del archivo actual:

Estos valores corresponden a la prueba anterior a incorporar 4 Ø10 en vigas.
Con la armadura nueva E1_1 tiene capacidad nominal y excede desde gravedad;
ver armadura_vigas_editable.md.

| Elemento | Primera fisuración estimada | Primera excedencia del criterio disponible |
|---|---:|---:|
| E1_1 (viga) | 0.00 s, gravedad | No evaluada: falta capacidad |
| E1_229 (columna) | 1.02 s | 1.80 s, P–My nominal |
| MURO_E1_1.1 | 0.86 s | 0.94 s, P–My en el plano |

Para repetir: cargar X/1.50x, seleccionar el ID/tag y usar IR A EXCEDENCIA,
ENFOCAR y ANIMAR FISURACIÓN. Guía completa en `P1L4/seismic/README.md`.

## Verificación en Unity

Compilación del Editor 6000.6.0f1 verificada. En Play, X/1.50x y E1_229:
primera excedencia a 1.80 s, DCR 1.272; animación superficial y enfoque del
elemento comprobados. RESTART vuelve a t=0, DCR 0.048, sin fisuras futuras.
E1_1 informa fisuración estimada desde gravedad y capacidad última N/D.
ANIMAR FISURACIÓN activa Solo seleccionado para evitar que otras piezas
oculten el elemento; desactivar ese toggle recupera el edificio completo.
Cada fisura crece suavemente al alcanzar su umbral, también durante Play.
Revisión final de Console: 0 errores y 0 warnings. Vista aislada de E1_229
comprobada después de la última compilación; los elementos vecinos ya no
ocultan la animación. Los muros fueron comprobados numéricamente; esta
sesión de prueba visual cubrió columna y viga.
