"""Carga todas las herramientas extra del servidor."""

import importlib
from pathlib import Path

import descargas


def registrar_todo(mcp, llamar_unity):
    descargas.registrar(mcp, llamar_unity)

    # Cualquier archivo tool_*.py de esta carpeta que tenga una función
    # registrar(mcp, llamar_unity) se carga solo.
    for archivo in sorted(Path(__file__).parent.glob("tool_*.py")):
        importlib.import_module(archivo.stem).registrar(mcp, llamar_unity)