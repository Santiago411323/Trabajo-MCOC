# Estacionamiento alto y ambientación de fútbol

VisualCampusSite eleva el perfil continuo del estacionamiento por la diferencia
entre la terraza superior y el apoyo más alto. En el modelo actual: +4.50m;
su extremo alto alcanza Y4, con pendiente hacia la calle y el extremo bajo.
Autos, líneas, vereda y árboles usan el mismo perfil. No se cambian nodos FE.

Cancha105×68m: 22 jugadores en equipos azul/naranjo, árbitro y balón.
Movimiento ambiental suave dentro de la cancha, sin físicas de partido ni
relación con cargas/OpenSees. Los porteros se mantienen cerca de los arcos.

Seis filas de graderías sobre talud al costado +X, aproximadamente43 hinchas
de pie detrás de los bancos, seis banderas, dos lienzos y animación de aliento.
Talud40%, huellas1.10m y desnivel0.44m por fila. Árboles trasladados detrás
de la barra para no cruzar los asientos. La ambientación depende de Terreno.

VisualFootballCrowd agrupa cada persona por material (tres renderers), elimina
colliders y libera meshes/materiales al reconstruir la escena. Las trayectorias
son exclusivamente decorativas. No alteran geometría, masa ni resultados.

Archivos: VisualCampusSite.cs; VisualFootballCrowd.cs y su meta.
