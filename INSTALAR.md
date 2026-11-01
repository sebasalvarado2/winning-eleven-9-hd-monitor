# Instalar en otra PC (dejar el juego igual que en la PC original)

Repo **privado y personal**: incluye texturas HD, DLL y binarios generados a partir del juego. No redistribuir.

## Pasos
1. Instalar WE9 en `E:KONAMIWinning Eleven 9` (misma ruta). Kitserver ya viene en `gamekitserver` (sin `GDB`). Debe existir `WE9_original.exe` (no tocar).
2. Clonar este repo en cualquier lado (`git clone`), y copiar el contenido de `game\` encima de la carpeta del juego:
   ```
   robocopy game "E:\KONAMI\Winning Eleven 9" /E
   ```
   Esto agrega `d3d8.dll` (proxy HD), `d3d8hd\` (ini + 722 texturas HD), `kitserver\hd_menus\` (fondos HD), y los `.cfg` de kitserver (afs2fs.root, resolucion 3440x1440 en `kload.cfg` -> **editar `dx.fullscreen.*` a tu monitor**).
3. Crear el `WE9.exe` parcheado (copia, no toca el original):
   `python game\aplicar_parche_exe.py "E:\KONAMI\Winning Eleven 9"`
4. Ejecutar `Instalar_64bits.bat` (como admin) para el registro e idioma.
5. Monitor: copiar `monitor\` a `E:\KONAMI\Winning Eleven 9\Monitor\` (los .exe ya compilados vienen incluidos; `compilar.bat` los regenera).
6. Herramientas de menus: copiar `we9ui\` a `E:\WE9_UI\` (incluye Real-ESRGAN en `ai\re2`, PNG/AFS extraidos, scripts y fuentes del proxy).
7. Para desactivar el HD: borrar `d3d8.dll` del juego.

Detalles de cada proyecto: `we9ui/README.md`, `monitor/README.md`.
