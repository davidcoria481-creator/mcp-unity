"""Descarga segura de modelos 3D desde internet hacia el proyecto de Unity.

Protecciones:
- Solo https y solo desde los sitios de HOSTS_PERMITIDOS (también en cada redirección).
- Solo se guardan modelos y texturas (EXTENSIONES_PERMITIDAS). Nunca scripts,
  ejecutables ni .unitypackage.
- Límites de tamaño y de cantidad de archivos.
- Los archivos van siempre dentro de Assets/Importados/<nombre_del_pack>.
"""

import io
import re
import shutil
import time
import zipfile
from pathlib import Path
from urllib.parse import urlparse

import ssl; import truststore; import httpx
# Sitios desde los que se permite descargar (el dominio exacto o un subdominio).
# Agrega otro sitio aquí solo si confías en él.
HOSTS_PERMITIDOS = [
    "kenney.nl",
    "opengameart.org",
    "github.com",
    "githubusercontent.com",
]

# Tipos de archivo que SÍ se guardan. Todo lo demás se descarta.
EXTENSIONES_PERMITIDAS = {
    ".fbx", ".obj", ".mtl",
    ".png", ".jpg", ".jpeg", ".tga", ".bmp",
    ".txt", ".md",
}

MAX_DESCARGA_MB = 200
MAX_DESCOMPRIMIDO_MB = 600
MAX_ARCHIVOS = 5000

CABECERAS = {"User-Agent": "Mozilla/5.0 (compatible; mcp-unity)"}


def _host_permitido(url: str) -> bool:
    p = urlparse(url)
    if p.scheme != "https" or not p.hostname:
        return False
    host = p.hostname.lower()
    return any(host == h or host.endswith("." + h) for h in HOSTS_PERMITIDOS)


def _descargar(url: str) -> tuple[bytes, str]:
    """Descarga siguiendo las redirecciones a mano, revisando cada sitio.
    Devuelve (datos, url_final)."""
    actual = url
    limite = MAX_DESCARGA_MB * 1024 * 1024

    with httpx.Client(timeout=60, follow_redirects=False, headers=CABECERAS, verify=truststore.SSLContext(ssl.PROTOCOL_TLS_CLIENT)) as cliente:
        for _ in range(6):
            if not _host_permitido(actual):
                host = urlparse(actual).hostname
                raise ValueError(
                    f"El sitio '{host}' no está en la lista de permitidos (o no usa https)."
                )

            with cliente.stream("GET", actual) as r:
                if r.status_code in (301, 302, 303, 307, 308):
                    destino = r.headers.get("location")
                    if not destino:
                        raise ValueError("Redirección sin destino.")
                    actual = str(httpx.URL(actual).join(destino))
                    continue

                r.raise_for_status()
                total = 0
                partes = []
                for trozo in r.iter_bytes():
                    total += len(trozo)
                    if total > limite:
                        raise ValueError(f"La descarga supera {MAX_DESCARGA_MB} MB.")
                    partes.append(trozo)
                return b"".join(partes), actual

    raise ValueError("Demasiadas redirecciones.")


def _limpiar_nombre(nombre: str) -> str:
    limpio = re.sub(r"[^A-Za-z0-9_-]+", "_", nombre.strip()).strip("_")
    return limpio[:60]


def _extraer_zip(datos: bytes, destino: Path) -> tuple[int, dict]:
    """Extrae solo los tipos permitidos. Devuelve (archivos_guardados, omitidos_por_tipo)."""
    guardados = 0
    omitidos: dict = {}
    limite = MAX_DESCOMPRIMIDO_MB * 1024 * 1024
    total_bytes = 0

    with zipfile.ZipFile(io.BytesIO(datos)) as z:
        infos = z.infolist()
        if len(infos) > MAX_ARCHIVOS:
            raise ValueError("El zip tiene demasiados archivos.")

        raiz = destino.resolve()
        for info in infos:
            if info.is_dir():
                continue

            partes = [
                p for p in info.filename.replace("\\", "/").split("/") if p not in ("", ".")
            ]
            if not partes or any(p == ".." or ":" in p for p in partes):
                omitidos["ruta_insegura"] = omitidos.get("ruta_insegura", 0) + 1
                continue

            ext = Path(partes[-1]).suffix.lower()
            if ext not in EXTENSIONES_PERMITIDAS:
                clave = ext or "(sin extension)"
                omitidos[clave] = omitidos.get(clave, 0) + 1
                continue

            salida = destino.joinpath(*partes).resolve()
            if raiz not in salida.parents:
                omitidos["ruta_insegura"] = omitidos.get("ruta_insegura", 0) + 1
                continue

            salida.parent.mkdir(parents=True, exist_ok=True)
            with z.open(info) as origen, open(salida, "wb") as f:
                while True:
                    trozo = origen.read(1024 * 1024)
                    if not trozo:
                        break
                    total_bytes += len(trozo)
                    if total_bytes > limite:
                        raise ValueError(
                            f"El contenido descomprimido supera {MAX_DESCOMPRIMIDO_MB} MB."
                        )
                    f.write(trozo)
            guardados += 1

    return guardados, omitidos


def _anotar_creditos(importados: Path, nombre, autor, licencia, pagina, url) -> None:
    fecha = time.strftime("%Y-%m-%d")
    linea = (
        f"{fecha} | {nombre} | autor: {autor} | licencia: {licencia} | "
        f"pagina: {pagina or '-'} | descarga: {url}\n"
    )
    with open(importados / "CREDITOS.txt", "a", encoding="utf-8") as f:
        f.write(linea)


def registrar(mcp, llamar_unity):
    """Agrega al servidor MCP las herramientas de importación."""

    @mcp.tool()
    def info_proyecto() -> str:
        """Muestra la versión de Unity, el pipeline de render (URP, HDRP o Built-in),
        el espacio de color y la carpeta Assets del proyecto. Úsala antes de importar
        modelos para saber si los materiales serán compatibles."""
        return llamar_unity("project_info")

    @mcp.tool()
    def descargar_asset(
        url: str, nombre: str, autor: str, licencia: str, pagina: str = ""
    ) -> str:
        """Descarga un pack de modelos 3D gratuito desde internet y lo guarda en
        Assets/Importados/<nombre> del proyecto de Unity.
        'url' debe ser el enlace DIRECTO al archivo (.zip, .fbx u .obj), con https, de un
        sitio permitido (kenney.nl, opengameart.org, github.com). Solo se guardan modelos y
        texturas; los scripts y otros archivos se descartan.
        Antes de usarla: confirma que la licencia permite usarlo en un juego (CC0 o CC-BY,
        nunca 'solo uso no comercial') y pídele a la persona que apruebe ese pack.
        'autor', 'licencia' y 'pagina' (la página del pack) se anotan en CREDITOS.txt."""
        info = llamar_unity("project_info")
        ruta_assets = None
        for linea in info.splitlines():
            if linea.startswith("Assets: "):
                ruta_assets = Path(linea[len("Assets: "):].strip())
        if ruta_assets is None or not ruta_assets.is_dir():
            return "Error: no pude saber dónde está la carpeta Assets del proyecto. " + info

        nombre_limpio = _limpiar_nombre(nombre)
        if not nombre_limpio:
            return "Error: el nombre del pack no es válido."
        if not autor.strip() or not licencia.strip():
            return "Error: indica el autor y la licencia del pack."

        importados = ruta_assets / "Importados"
        destino = importados / nombre_limpio
        if destino.exists():
            return f"Error: ya existe Assets/Importados/{nombre_limpio}. Usa otro nombre."

        try:
            datos, url_final = _descargar(url)
        except Exception as e:
            return f"Error al descargar: {e}"

        destino.mkdir(parents=True)
        try:
            if zipfile.is_zipfile(io.BytesIO(datos)):
                guardados, omitidos = _extraer_zip(datos, destino)
            else:
                ruta_url = Path(urlparse(url_final).path)
                ext = ruta_url.suffix.lower()
                if ext not in EXTENSIONES_PERMITIDAS:
                    raise ValueError(
                        f"El archivo es de tipo '{ext or 'desconocido'}', que no se permite. "
                        "Necesito el enlace directo a un .zip, .fbx u .obj (no la página web)."
                    )
                nombre_archivo = _limpiar_nombre(ruta_url.stem) + ext
                (destino / nombre_archivo).write_bytes(datos)
                guardados, omitidos = 1, {}
        except Exception as e:
            shutil.rmtree(destino, ignore_errors=True)
            return f"Error al guardar: {e}"

        detalle = ", ".join(f"{k}: {v}" for k, v in sorted(omitidos.items()))

        if guardados == 0:
            shutil.rmtree(destino, ignore_errors=True)
            return "Error: el archivo no tenía modelos ni texturas permitidos. Omitidos: " + (
                detalle or "ninguno"
            )

        _anotar_creditos(importados, nombre_limpio, autor, licencia, pagina, url_final)

        resumen = [f"Guardado en Assets/Importados/{nombre_limpio} ({guardados} archivos)."]
        if omitidos:
            resumen.append(
                "Se omitieron archivos de otros tipos (por seguridad o porque Unity no los "
                "usa directamente): " + detalle + "."
            )
        resumen.append(llamar_unity("refresh_assets"))
        resumen.append(
            "Siguiente paso: listar_prefabs con carpeta='Assets/Importados/"
            + nombre_limpio
            + "' para ver los modelos."
        )
        return "\n".join(resumen)