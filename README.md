# Winning Eleven 9 – Monitor + WE9UI (menús HD)

Dos proyectos independientes sobre **Winning Eleven 9 (PC)**:

| Carpeta | Qué es |
|---|---|
| [`monitor/`](monitor/) | Ingeniería inversa del gameplay: monitor/logger de estados en memoria, escritor de memoria, tablas de jugadas/habilidades y todas las notas de REA + Ghidra (`monitor/notas/re_notas.md`, vueltas 1–144). |
| [`we9ui/`](we9ui/) | Menús/HUD/logos en HD sin tocar archivos originales: herramientas de AFS, reemplazo vía kitserver `afs2fs`, y un proxy `d3d8.dll` que cambia texturas por versiones HD (IA) y suaviza texto. |

> **Este repo NO incluye assets del juego** (AFS, texturas extraídas, dumps, TGA HD, ejecutables). Todo eso se regenera localmente desde tu propia copia del juego.

## Requisitos generales
- Windows 10/11, copia propia de WE9 en `E:\KONAMI\Winning Eleven 9` (+ kitserver/kload y afs2fs para `we9ui`).
- Los scripts tienen **rutas fijas en `E:\`** (ver cada README). Si usás otra unidad, hacé búsqueda/reemplazo de `E:\WE9_UI` y `E:\KONAMI\Winning Eleven 9`.
- Reglas: **nunca modificar `WE9_original.exe`**. `WE9.exe` (copia) se puede parchear con backup fechado.

## Empezar
- Monitor → [`monitor/README.md`](monitor/README.md)
- WE9UI → [`we9ui/README.md`](we9ui/README.md)
- Contexto para asistentes de IA (Claude Code) → [`CLAUDE.md`](CLAUDE.md)

## Layout esperado en tu PC
```
E:\KONAMI\Winning Eleven 9\
    WE9.exe, WE9_original.exe, dat\, kitserver\, d3d8.dll (proxy), d3d8hd\
    Monitor\      <- contenido de monitor/ de este repo
E:\WE9_UI\
    tools\        <- we9ui/tools
    d3dhook\      <- we9ui/d3dhook
    ai\re2\       <- Real-ESRGAN ncnn-vulkan (descarga aparte)
```
