"""Herramienta para crear el terreno de la isla."""


def registrar(mcp, llamar_unity):
    @mcp.tool()
    def crear_terreno_isla(
        centro_x: float = 520,
        centro_z: float = -12,
        tamano: float = 1400,
        radio_isla: float = 560,
        altura_montana: float = 130,
        angulo_montana: float = 90,
        altura_colinas: float = 5,
        nivel_agua: float = -1.5,
        profundidad_mar: float = 30,
        crear_agua: bool = True,
        resolucion: int = 257,
        semilla: int = 0,
        nombre: str = "Isla",
        grupo: str = "Terreno",
    ) -> str:
        """Crea el terreno de una isla en la escena de Unity: suelo plano a altura 0 alrededor del
        centro (la ciudad), colinas suaves, una montaña, costa irregular con playa y mar.
        (centro_x, centro_z) es el centro de la isla (el centro de la ciudad); 'tamano' es el
        lado del terreno en metros y 'radio_isla' el radio aproximado de la isla.
        'altura_montana' es la altura máxima de la montaña y 'angulo_montana' hacia dónde queda,
        en grados desde el eje X hacia el eje Z (90 = norte, +Z; 0 = este, +X; 180 = oeste; 270 = sur).
        'altura_colinas' son las colinas fuera de la ciudad (0 = todo plano). 'nivel_agua' es la altura
        y del mar (debe ser menor que 0) y 'crear_agua' agrega el plano de agua azul.
        'resolucion' 257 (rápido, bueno para móvil) o 513 (más detalle). 'semilla' repite la misma
        forma (0 = al azar; la respuesta dice cuál se usó). Genera texturas de arena, pasto y roca
        y las guarda en Assets/Terreno. Si ya existe un terreno con ese nombre, bórralo antes con
        borrar_objeto. Se puede quitar con Ctrl + Z, pero los archivos de Assets/Terreno quedan."""
        return llamar_unity(
            "create_island_terrain",
            name=nombre,
            parent=grupo,
            centerX=centro_x,
            centerZ=centro_z,
            size=tamano,
            islandRadius=radio_isla,
            mountainHeight=altura_montana,
            mountainAngle=angulo_montana,
            hillHeight=altura_colinas,
            waterLevel=nivel_agua,
            seaDepth=profundidad_mar,
            createWater=crear_agua,
            resolution=resolucion,
            seed=semilla,
        )