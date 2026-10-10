import httpx
from mcp.server.fastmcp import FastMCP

mcp = FastMCP("unity")

UNITY_URL = "http://127.0.0.1:8090/"


def llamar_unity(comando: str, **datos) -> str:
    """Envía una orden al plugin de Unity y devuelve su respuesta."""
    payload = {"command": comando, **datos}
    try:
        r = httpx.post(UNITY_URL, json=payload, timeout=15)
        resultado = r.json()
    except httpx.ConnectError:
        return "Error: no puedo conectar con Unity. ¿Está abierto el proyecto?"
    except Exception as e:
        return f"Error: {e}"

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


if __name__ == "__main__":
    mcp.run()