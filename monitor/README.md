# Monitor (ingeniería inversa de gameplay)

Herramientas en C# (.NET Framework 4, sin dependencias) que leen/escriben la memoria del proceso `WE9.exe`, más las notas de análisis estático (REA + Ghidra) y dinámico (Frida).

## Contenido
- `WE9Monitor.cs` – monitor en vivo (jugadores, pelota, estados).
- `LoggerEstados.cs/.bat` – registra estados/animaciones por frame a `logs\` (analizar con `analiza_log.js`).
- `EscritorMemoria.cs/.bat` – escribe valores en memoria (mods de jugadas/habilidades).
- `aplicar_jugadas.bat/.ps1` – aplica tablas de jugadas (`jugadas_*.csv/txt`).
- Datos: `adn_jugadores.txt`, `equipos_jugadores.txt`, `nombres_jugadores.txt`, `habilidades.txt`, `watches.txt`, `tiro_angulo.txt`, `video.txt`, `movimientos_vistos.txt`.
- `notas/` – **leer primero** `CONTEXTO_SESION.txt` (estado por sesión) y `re_estado.md`; el diario completo es `re_notas.md`; `MAPA_GAMEPLAY.md` y `PROPUESTAS_MODS.md` son las síntesis.

## Compilar y correr
1. Copiá esta carpeta a `E:\KONAMI\Winning Eleven 9\Monitor\`.
2. Ejecutá `compilar.bat` (usa `csc.exe` de .NET 4: `C:\Windows\Microsoft.NET\Framework\v4.0.30319`). Genera los `.exe`.
3. Abrí WE9 y luego `LoggerEstados.bat` / el monitor **como administrador** si no puede abrir el proceso.
4. Los logs se guardan en `logs\` (ignorado por git).

## Seguir el trabajo
- Cada hallazgo se agrega a `notas/re_notas.md` como una nueva "vuelta" (siguiente: 145) y se resume en `re_estado.md`.
- Análisis estático: abrir `WE9_original.exe` (solo lectura) en Ghidra 12.1.x con el MCP **REA**; observación en vivo con Frida (`start_frida_session` sobre `WE9.exe`).
- Direcciones/offsets conocidos: ver `notas/re_*.txt` y `MAPA_GAMEPLAY.md`.
- No parchear `WE9_original.exe`; para parches usar `WE9.exe` con backup fechado y anotarlo en `re_notas.md`.
