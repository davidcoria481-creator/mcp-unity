import time

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


if __name__ == "__main__":
    mcp.run()