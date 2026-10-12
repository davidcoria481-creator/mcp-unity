import time
from typing import Optional

import httpx
from mcp.server.fastmcp import FastMCP

mcp = FastMCP("unity")

UNITY_URL = "http://127.0.0.1:8090/"


def llamar_unity(comando: str, **datos) -> str:
    """Envía una orden al plugin de Unity y devuelve su respuesta."""
    payload = {"command": comando, **datos}
    resultado = None
    ultimo_error = ""

    # Si Unity está recompilando, el plugin tarda unos segundos en volver:
    # reintentamos varias veces antes de rendirnos.
    for _ in range(6):
        try:
            r = httpx.post(UNITY_URL, json=payload, timeout=20)
            resultado = r.json()
            break
        except httpx.ConnectError:
            ultimo_error = "no puedo conectar con Unity (¿está abierto el proyecto?)"
            time.sleep(2)
        except Exception as e:
            return f"Error: {e}"

    if resultado is None:
        return "Error: " + ultimo_error

    if resultado.get("ok"):
        return resultado.get("message", "OK")
    return "Error de Unity: " + resultado.get("message", "desconocido")


# ---------------------------------------------------------------- Básicas


@mcp.tool()
def ping() -> str:
    """Prueba de conexión con el servidor MCP (no usa Unity)."""
    return "pong desde mi MCP de Unity"


@mcp.tool()
def unity_ping() -> str:
    """Comprueba la conexión con el editor de Unity y devuelve su versión."""
    return llamar_unity("ping")


@mcp.tool()
def crear_gameobject(nombre: str, x: float = 0, y: float = 0, z: float = 0) -> str:
    """Crea un GameObject vacío en la escena activa de Unity, en la posición x, y, z."""
    return llamar_unity("create_gameobject", name=nombre, x=x, y=y, z=z)


# ---------------------------------------------------------------- Scripts y consola


@mcp.tool()
def leer_consola(cantidad: int = 30) -> str:
    """Devuelve los últimos mensajes de la Console de Unity (logs, warnings y errores).
    Úsala después de crear o modificar scripts para ver si hubo errores de compilación."""
    return llamar_unity("read_console", count=cantidad)


@mcp.tool()
def leer_script(ruta: str) -> str:
    """Lee un script C# del proyecto. La ruta es relativa a Assets, por ejemplo 'Scripts/Player.cs'."""
    return llamar_unity("read_script", path=ruta)


@mcp.tool()
def crear_script(ruta: str, contenido: str, reemplazar: bool = False) -> str:
    """Crea un script C# dentro de Assets. La ruta es relativa a Assets, por ejemplo 'Scripts/Player.cs'.
    Si el archivo ya existe, solo se reemplaza con reemplazar=True (se guarda una copia de seguridad).
    Unity recompila después; usa leer_consola para comprobar si hay errores."""
    return llamar_unity("create_script", path=ruta, content=contenido, overwrite=reemplazar)


# ---------------------------------------------------------------- Lectura de la escena


@mcp.tool()
def listar_jerarquia(filtro: str = "", ruta: str = "", maximo: int = 100) -> str:
    """Lista los objetos de la escena activa de Unity (solo lectura). Por cada objeto devuelve
    nombre, posición, rotación Y, escala, tamaño total, cantidad de hijos y el prefab de origen.
    Sin 'ruta' muestra los objetos raíz de la escena. Con 'ruta' (por ejemplo 'Calles') muestra
    los hijos de ese objeto. 'filtro' deja solo los objetos cuyo nombre contiene ese texto."""
    return llamar_unity("list_hierarchy", filter=filtro, path=ruta, maxResults=maximo)


@mcp.tool()
def listar_prefabs(filtro: str = "", carpeta: str = "Assets", maximo: int = 60) -> str:
    """Lista los prefabs y modelos disponibles en el proyecto de Unity (solo lectura).
    'filtro' deja solo las rutas que contienen ese texto (por ejemplo 'traffic' o 'building').
    'carpeta' limita la búsqueda a una carpeta dentro de Assets."""
    return llamar_unity("list_prefabs", filter=filtro, folder=carpeta, maxResults=maximo)


# ---------------------------------------------------------------- Edición de la escena
# Todo se puede deshacer en Unity con Ctrl + Z.
# Las rutas de objetos se escriben con barras, por ejemplo 'Vegetacion/Palmeras/Palm_03'.


@mcp.tool()
def crear_grupo(nombre: str, padre: str = "", x: float = 0, y: float = 0, z: float = 0) -> str:
    """Crea un grupo (GameObject vacío) en la escena activa para organizar objetos.
    'padre' es la ruta de otro grupo para anidarlo (por ejemplo 'Vegetacion').
    x, y, z es la posición del grupo en el mundo. Si el grupo ya existe, no hace nada."""
    return llamar_unity("create_group", name=nombre, parent=padre, x=x, y=y, z=z)


@mcp.tool()
def mover_a_grupo(
    grupo: str,
    nombres: Optional[list[str]] = None,
    filtro: str = "",
    origen: str = "",
) -> str:
    """Mueve objetos de la escena dentro de un grupo, manteniendo su posición en el mundo.
    'grupo' es la ruta del grupo destino. Elige qué mover con 'nombres' (lista de nombres exactos)
    o con 'filtro' (texto contenido en el nombre). Sin 'origen' busca entre los objetos raíz;
    con 'origen' busca entre los hijos de esa ruta. Se deshace de un solo paso con Ctrl + Z."""
    return llamar_unity(
        "reparent", parent=grupo, names=nombres or [], filter=filtro, path=origen
    )


@mcp.tool()
def renombrar_objeto(ruta: str, nombre_nuevo: str) -> str:
    """Cambia el nombre de un objeto de la escena. 'ruta' es su ruta, por ejemplo 'Prefab'."""
    return llamar_unity("rename_object", path=ruta, name=nombre_nuevo)


@mcp.tool()
def transformar_objeto(
    ruta: str,
    x: Optional[float] = None,
    y: Optional[float] = None,
    z: Optional[float] = None,
    rotacion_y: Optional[float] = None,
    escala: Optional[float] = None,
) -> str:
    """Cambia la posición, la rotación o la escala de un objeto de la escena.
    Para cambiar la posición (en el mundo) hay que indicar x, y y z juntos; usa listar_jerarquia
    para conocer los valores actuales. 'rotacion_y' es el giro en grados sobre el eje vertical.
    'escala' es una escala uniforme (1 = tamaño original)."""
    datos: dict = {"path": ruta}

    posicion = [x, y, z]
    if any(v is not None for v in posicion):
        if any(v is None for v in posicion):
            return (
                "Error: para cambiar la posición indica x, y y z juntos "
                "(usa listar_jerarquia para ver los valores actuales)."
            )
        datos.update(setPosition=True, x=x, y=y, z=z)

    if rotacion_y is not None:
        datos.update(setRotation=True, rotY=rotacion_y)

    if escala is not None:
        datos.update(setScale=True, scale=escala)

    if len(datos) == 1:
        return "Error: no indicaste ningún cambio."

    return llamar_unity("set_transform", **datos)


@mcp.tool()
def borrar_objeto(ruta: str, forzar: bool = False) -> str:
    """Borra un objeto de la escena (se puede deshacer con Ctrl + Z).
    Si el objeto tiene más de 10 objetos adentro, hay que usar forzar=True."""
    return llamar_unity("delete_object", path=ruta, force=forzar)


@mcp.tool()
def instanciar_prefab(
    prefab: str,
    x: float = 0,
    y: float = 0,
    z: float = 0,
    rotacion_y: Optional[float] = None,
    escala: Optional[float] = None,
    padre: str = "",
    nombre: str = "",
) -> str:
    """Pone en la escena una copia de un prefab o modelo del proyecto.
    'prefab' es la ruta completa, por ejemplo
    'Assets/ithappy/Cartoon_City_Free/Prefabs/Props/traffic_light_001.prefab'
    (usa listar_prefabs para encontrarlos). x, y, z es la posición en el mundo.
    'rotacion_y' y 'escala' son opcionales; si no se indican, se respetan los del prefab.
    'padre' es la ruta de un grupo donde colocarlo y 'nombre' un nombre opcional."""
    datos: dict = {
        "prefab": prefab,
        "parent": padre,
        "name": nombre,
        "setPosition": True,
        "x": x,
        "y": y,
        "z": z,
    }

    if rotacion_y is not None:
        datos.update(setRotation=True, rotY=rotacion_y)

    if escala is not None:
        datos.update(setScale=True, scale=escala)

    return llamar_unity("instantiate_prefab", **datos)


@mcp.tool()
def guardar_escena() -> str:
    """Guarda la escena activa de Unity en su archivo."""
    return llamar_unity("save_scene")


if __name__ == "__main__":
   import modulos; modulos.registrar_todo(mcp, llamar_unity); mcp.run()