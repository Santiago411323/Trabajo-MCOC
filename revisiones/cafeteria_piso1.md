# Cafetería y plataforma del piso 1

- La cota estructural Z=4 corresponde a Y=4 en Unity. El pavimento decorativo se coloca a Y=4,03 para evitar superficies coincidentes; mobiliario, techos y galería suben juntos 8 metros.
- La cafetería se identifica con el filtro `CIELO_1`. Su acceso desde la terraza Y=4 pasa a ser plano; se conserva la escalera existente E1_220 → terraza.
- La plataforma superior se extiende en −X hasta cubrir la cafetería, sus terrazas y los pequeños peldaños del borde posterior. El talud rocoso de 9 m comienza en ese borde y baja hacia −X; las rocas se trasladan con él.
- La cancha conserva su cota anterior. Se mantienen los controles para ocultar el ambiente.
- Cambios idénticos en los proyectos Android e iOS, exclusivamente en geometría decorativa: no se modificaron nodos, elementos, cargas, resultados ni los paquetes sísmicos.

## Verificación

PASS en Unity para ambos proyectos: altura del pavimento y techo, cobertura completa del largo de la cafetería, unión continua entre plataforma y talud y ausencia de superposición del talud con la cafetería. Además se verificaron los ocho casos sísmicos, selección/diagramas VR y retorno a AR. Registros: `Logs/cafe-floor-validation.log` en cada proyecto.

El entregable iOS se actualiza a build 13. La integridad y los recursos del ZIP se registran en `validacion_zip_ios.json`.
