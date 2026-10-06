# Cafetería y terraza −4 m — P1L4

El usuario confirmó nivel −4 m. VisualCafe toma E1_24 y limita la planta
con su vano y el punto de muro cercano hacia el núcleo del ascensor.
Planta aproximada: X−3.12 a7.33, Z Unity−11.25 a−5.145; piso visual−3.97
(3 cm sobre la cota−4). Terraza exterior hasta Z−18.85, al mismo nivel.
Estas dimensiones son una interpretación del modelo/foto, no arquitectura
levantada. No se alteran coordenadas estructurales ni resultados.

6 mesas interiores y6 exteriores, sillas, quitasoles exteriores, barra,
espresso con grupos y tazas, molinillo, vitrina, refrigerador, maceteros,
pavimento, cielo y rótulo. Geometría decorativa sin colliders ni IDs FE.

El nuevo acceso parte del final de la escalera E1_220→Terraza Y4:
X=TerraceContactX+0.4, Y4.03. Un plano conecta hacia la ruta exterior;
la bajada continua es de3.20m de ancho, con contrahuellas≤0.18m y
pasamanos. Termina en la terraza aY−3.97. Los peldaños mantienen sus
caras horizontales y se apoyan en cuerpos sólidos, sin bloques flotantes.

VisualSiteTerrain recorta la superficie y caras laterales alrededor de
cafetería y escalera. La explanada/vehículos/vereda se separan de la terraza
para no atravesarla. Capas tiene Cafetería / terraza; Escaleras controla la
nueva bajada; Terreno controla el conjunto, y el filtro FOUNDATION identifica
la planta inferior. Todos muestra la ambientación completa.

Archivos: VisualCafe.cs (+meta), StructureViewer.cs, VisualSiteTerrain.cs,
VisualCampusSite.cs. No se cambian cargas, OpenSees o geometría FE.
