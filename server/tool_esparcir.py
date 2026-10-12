"""Herramienta para poner muchos objetos de una sola vez dentro de una zona."""


def registrar(mcp, llamar_unity):
    @mcp.tool()
    def esparcir_prefabs(
        prefabs: list[str],
        cantidad: int,
        centro_x: float,
        centro_z: float,
        forma: str = "anillo",
        radio_min: float = 0,
        radio_max: float = 50,
        ancho: float = 0,
        largo: float = 0,
        grupo: str = "",
        distancia_minima: float = 5,
        margen: float = 1.5,
        escala_min: float = 1,
        escala_max: float = 1,
        rotacion_aleatoria: bool = True,
        altura: float = 0,
        apoyar_en_suelo: bool = False,
        evitar_objetos: bool = True,
        semilla: int = 0,
    ) -> str:
        """Pone muchos objetos de una sola vez dentro de una zona de la escena de Unity (máximo 200).
        'prefabs' es la lista de rutas completas de prefabs (por ejemplo
        'Assets/ithappy/Cartoon_City_Free/Prefabs/Props/traffic_light_001.prefab'); en cada
        posición se elige uno al azar. Usa listar_prefabs para encontrarlas.
        Zona: con forma='anillo' se reparten entre radio_min y radio_max alrededor de
        (centro_x, centro_z) (con radio_min=0 es un círculo). Con forma='rectangulo' se usa un
        rectángulo centrado en (centro_x, centro_z) de 'ancho' (eje X) por 'largo' (eje Z).
        'grupo' es la ruta del grupo donde quedan (se crea si no existe).
        'distancia_minima' separa los objetos entre sí; con evitar_objetos=True no se ponen
        encima de edificios u objetos altos que ya existen, dejando 'margen' de espacio.
        'escala_min' y 'escala_max' varían el tamaño (1 = el del prefab).
        'altura' es la altura y donde se apoyan; con apoyar_en_suelo=True se usa la altura del
        suelo con collider y 'altura' queda como respaldo si no hay.
        'semilla' repite el mismo resultado (0 = al azar; la respuesta dice cuál se usó).
        Se deshace con un solo Ctrl + Z. Prueba primero con pocos objetos (10 a 20) y revisa con
        captura_escena o vista_aerea antes de poner más."""
        return llamar_unity(
            "scatter_prefabs",
            prefabs=prefabs,
            parent=grupo,
            shape="rectangulo" if forma.lower().startswith("rect") else "anillo",
            centerX=centro_x,
            centerZ=centro_z,
            rMin=radio_min,
            rMax=radio_max,
            sizeX=ancho,
            sizeZ=largo,
            count=cantidad,
            minDistance=distancia_minima,
            margin=margen,
            scaleMin=escala_min,
            scaleMax=escala_max,
            randomRotation=rotacion_aleatoria,
            yBase=altura,
            snapToGround=apoyar_en_suelo,
            avoidObjects=evitar_objetos,
            seed=semilla,
        )