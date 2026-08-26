# Proyecto MCOC

Codigos en Python para analisis estructural 2D y 3D.

## Archivos

- `analisis_marco.py`: analiza el marco de la imagen y entrega desplazamientos, reacciones, axial, corte y momento.
- `benchmark_3d_opensees.py`: benchmark 3D en OpenSeesPy con marco de un vano en X, un vano en Y, losa descargada a vigas, apoyos, cargas y resultados.
- `LAB_semana1_benchmark_3D.md`: ficha del problema 3D con geometria, secciones, materiales, apoyos, cargas y verificacion.
- `DEFENSA_individual_3D.md`: guia para explicar GDL, ejes, `geomTransf`, `Iy`, `Iz` y verificaciones.
- `resultados_verificacion_3d.md`: archivo generado por el benchmark con comparacion contra referencias.
- `marco_2d.py`: modelo de marco 2D con OpenSeesPy.
- `modelo_2d_minimo.py`: ejemplo minimo de viga 2D con OpenSeesPy.

## Ejecutar

```bat
python analisis_marco.py
```

Si `python` no funciona en Windows:

```bat
py analisis_marco.py
```

Para ejecutar el benchmark 3D:

```bat
python benchmark_3d_opensees.py
```

El benchmark 3D genera la imagen `benchmark_3d_modelo.png`, el archivo `resultados_verificacion_3d.md` y muestra desplazamientos, reacciones, equilibrio global y fuerzas internas.

## Instalar dependencias

```bat
pip install -r requirements.txt
```
