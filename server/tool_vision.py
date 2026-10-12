"""Herramientas para que Claude pueda VER la escena de Unity."""

import base64

from mcp.server.fastmcp import Image


def registrar(mcp, llamar_unity):
    def _capturar(x, y, z, mirar_x, mirar_y, mirar_z, fov, ancho, alto) -> Image:
        respuesta = llamar_unity(
            "capture_scene",
            x=x,
            y=y,
            z=z,
            targetX=mirar_x,
            targetY=mirar_y,
            targetZ=mirar_z,
            fov=fov,
            width=ancho,
            height=alto,
        )
        if not respuesta.startswith("IMG:"):
            raise RuntimeError(respuesta)
        return Image(data=base64.b64decode(respuesta[4:]), format="png")

    @mcp.tool()
    def captura_escena(
        x: float,
        y: float,
        z: float,
        mirar_x: float,
        mirar_y: float,
        mirar_z: float,
        fov: float = 60,
        ancho: int = 960,
        alto: int = 540,
    ) -> Image:
        """Devuelve una imagen de la escena de Unity vista desde un punto.
        (x, y, z) es la posición de la cámara y (mirar_x, mirar_y, mirar_z) el punto al que mira.
        Para ver a nivel de calle usa y entre 2 y 6. fov es el ángulo de visión en grados.
        Úsala después de colocar o mover objetos para revisar el resultado:
        edificios al revés, objetos flotando o enterrados, cosas superpuestas."""
        return _capturar(x, y, z, mirar_x, mirar_y, mirar_z, fov, ancho, alto)

    @mcp.tool()
    def vista_aerea(
        centro_x: float,
        centro_z: float,
        altura: float = 250,
        ancho: int = 960,
        alto: int = 540,
    ) -> Image:
        """Devuelve una vista desde arriba en diagonal de una zona de la escena de Unity.
        centro_x y centro_z son el punto central de la zona. 'altura' es lo alto que se pone la
        cámara: 250 abarca unos 300 de ancho; sube la altura para ver una zona más grande.
        Úsala para revisar el orden general de una zona."""
        return _capturar(
            centro_x,
            altura,
            centro_z - altura * 0.8,
            centro_x,
            0,
            centro_z,
            60,
            ancho,
            alto,
        )