from mcp.server.fastmcp import FastMCP

mcp = FastMCP("unity")


@mcp.tool()
def ping() -> str:
    """Prueba de conexión: responde pong."""
    return "pong desde mi MCP de Unity"


@mcp.tool()
def sumar(a: int, b: int) -> int:
    """Suma dos números (solo para probar que los parámetros funcionan)."""
    return a + b


if __name__ == "__main__":
    mcp.run()