# Ventanas y entorno del campus — P1L4

VisualFrameFacade conserva la detección de marcos exteriores y excluye los
muros estructurales. Sustituye el cerramiento salmón por vidrio azul grisáceo,
marcos oscuros y montantes espaciados como máximo 1.40 m. Divide cada tramo
de columna en los niveles de vigas que cruzan la fachada, descontando la
semialtura de vigas superior/inferior. Los paneles se registran por piso;
el toggle Ventanas los oculta junto con sus marcos. No llevan colliders.

VisualCampusSite crea un entorno estilizado inspirado en las fotografías:
explanada de estacionamientos con autos, calzada y marcas, veredas, árboles,
cancha con arcos y suelo continuo. El terreno mantiene niveles de apoyos
existentes; la cancha se conecta visualmente a la terraza Y4 cuando existe.
La ubicación de estos objetos es aproximada, no un levantamiento topográfico.
El campus depende de Terreno y se oculta en Solo estructura.

Archivos: VisualFrameFacade.cs, VisualCampusSite.cs (+meta),
VisualSiteTerrain.cs y StructureViewer.cs. No se modifica OpenSees, JSON
estructural, masas, apoyos, conectividad ni resultados numéricos.

Validación en Unity 6000.6.0f1: compila; en Play ventanas ON/OFF oculta y
restaura vidrio/marcos; filtro CIELO_2 muestra su franja correspondiente y
Todos restaura la fachada; Terreno OFF oculta suelo/campus y ON los recupera.
Console de la prueba Play: 0 errores y 0 warnings. Apariencia revisada en
vista general con edificio, estacionamientos, árboles, calles y cancha.
