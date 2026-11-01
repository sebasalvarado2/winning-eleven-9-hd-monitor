# WE9UI – menús / HUD / logos en HD

**Alcance:** solo menús, HUD, logos e interfaz. NO camisetas, caras, pelo, público ni nada que maneje kitserver.
**Ultrawide:** intentado y abandonado (`[aspect] enable=0`); el juego queda estirado 4:3 → pantalla.

## Cómo funciona (resumen)
El juego dibuja en 4:3 sobre superficies 1024x1024 y las estira. Todas las texturas son A8R8G8B8 MANAGED, rellenadas con `LockRect` nivel 0 (~1475 por corrida). Hay dos mecanismos complementarios:

1. **afs2fs (kitserver)** – reemplaza entradas de `e_text.afs` / `S_TEXT.AFS` por `kitserver\hd_menus\<AFS>\menu_<N>.bin`. Solo sirve para **fondos a pantalla completa** (UV normalizadas). Hojas de sprites, título, HUD e intros usan rects en píxeles → a 2x se ven recortados; no usarlo ahí.
2. **Proxy `d3d8.dll`** (`d3dhook/d3d8proxy.cpp`) – parches de vtable encadenados (nunca wrappers COM: kload.DLL provoca recursión infinita), inline-hook con trampolín, y:
   - `bindswap`: al `SetTexture` cambia la textura por una HD buscando `d3d8hd\hd\<hash>.tga`, hash FNV-1a64 de (w,h,píxeles).
   - suavizado en vivo de texto/etiquetas (Catmull-Rom premultiplicado + curva de alpha).
   - volcado de texturas (`dump`) y log de uso (`usage`).
   - **Nunca agrandar texturas al crearlas** (kserv/GDB rellenan escudos, números, camisetas → corrupción). Dejar `sizes=` vacío.

## Requisitos
- Python 3 + `pillow numpy scipy`.
- MSVC 2022 (x86) para compilar el proxy.
- [Real-ESRGAN ncnn-vulkan](https://github.com/xinntao/Real-ESRGAN/releases) en `E:\WE9_UI\ai\re2` (modelos incluidos; GPU Vulkan). La salida puede salir en blanco bajo carga de GPU → `ai_pipeline.py` valida y reintenta.
- Juego con kitserver (kload) y `afs2fs` activos; `kitserver\afs2fs.cfg` con `afs.root = "hd_menus"`.

## Layout
```
E:\WE9_UI\tools\      <- we9ui/tools/*.py
E:\WE9_UI\d3dhook\    <- we9ui/d3dhook/*
E:\WE9_UI\ai\re2\     <- Real-ESRGAN
E:\WE9_UI\e_text\     <- entradas extraídas (afs.py)
```
Los scripts tienen estas rutas fijas.

## Flujo A – fondos por afs2fs
```
python tools\afs.py "E:\KONAMI\Winning Eleven 9\dat\S_TEXT.AFS" E:\WE9_UI\e_text   # NNNN.bin
```
Formato de entrada/MAG: cabecera +0x14 w, +0x16 h, +0x18/19 formato (0x13 = 8bpp paleta256 en +0x80; 0x14 = 4bpp paleta16 en +0x40). 8bpp = swizzle PSMT8 (`epack.py`); 4bpp = PSMT4 (`psmt4.py`, `enc4.py`).
Escalar con `upscale_menu_rest.py` / `upscale_entry.py <preview> <min_side> <entradas…>`; el resultado va a `kitserver\hd_menus\{e_text.afs,S_TEXT.AFS}\menu_<N>.bin` (en **ambas** carpetas: el juego usa el AFS inglés incluso en español). Nunca re-escalar una textura ya 2x: partir de la fuente original.

## Flujo B – proxy + IA (el principal)
1. Compilar: `d3dhook\build.bat` → `build\d3d8.dll`. Copiar a la carpeta del juego como `d3d8.dll` (backup de la versión previa).
2. Copiar `d3d8hd.ini.ejemplo` a `<juego>\d3d8hd\d3d8hd.ini` y poner `dump=1 dumpall=1 usage=1`.
3. Jugar/navegar menús → se llena `d3d8hd\dump\`.
4. `python d3dhook\ai_pipeline.py` → escala con Real-ESRGAN y escribe `d3d8hd\hd\<hash>.tga` (32-bit BGRA). Los hashes extra a incluir van en `ui_extra_hashes.txt`.
5. Poner `dump=0 dumpall=0 usage=0` y jugar. Para desactivar todo: borrar `d3d8.dll` del juego.

Claves del ini: `[hd] enable dump dumpall hd smooth bindswap labels usage scale sizes maxarea log` y `[aspect] enable bgcover target3d` (dejar `enable=0`).
Log: `d3d8hd\d3d8hd.log`.

## Pendiente / ideas
- Verificar v3.8 (escudo Liverpool, números de equipo, glitch "MAX").
- Obtener un `usage` en partido real para decidir IA vs redibujo del HUD.
- Fuente de texto HD real (glyph cache): origen no encontrado; hoy se suaviza.
- Ver `docs/PRUEBAS_PENDIENTES.txt`.

## Lecciones
- afs2fs 2x en sprites → recortes; solo fondos.
- Wrappers COM → crash con kload; usar vtable chain + inline hooks.
- Texturas IA en blanco → validar (`good()`) y reintentar / fallback Lanczos.
- Parchear C con heredocs de bash rompe backslashes: usar el editor.
