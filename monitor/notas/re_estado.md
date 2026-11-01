# INDICE: contexto completo en CONTEXTO_SESION.txt (seccion 9 = orden de lectura). Este archivo = como retomar REA + bloques por fecha (los mas nuevos al final).
# RE_ESTADO - registro para retomar la ingenieria inversa (REA + Ghidra)

Actualizado: 27/10/2026 ~06:55. Si se cierra Claude Code, pasale este archivo y decile
"lee re_estado.md y re_notas.md del Monitor y segui".

## Archivos
- re_notas.md   : hallazgos tecnicos (vueltas 1-9). NO repetir lo ya documentado ahi.
- re_estado.md  : este archivo (estado, como retomar, proximos pasos).
- contexto_sesion.txt : contexto general del mod.

## Como retomar (en orden)
1. Reiniciar Claude Code si hace falta (para cargar el MCP "rea").
2. REA pierde los ajustes al reiniciarse: volver a subir el timeout de Ghidra:
   update_analysis_settings -> ghidra.startup_timeout_ms = 1800000
3. open_binary  E:\KONAMI\Winning Eleven 9\WE9_original.exe
   (el primer analisis tarda ~10-20 min; las llamadas se van a segundo plano a los 120 s: es normal).
4. Seguir el volcado de pseudocodigo con resume=true (ver abajo).
5. Cada hallazgo nuevo: anotarlo en re_notas.md ANTES de seguir (asi no se pierde).

## Volcado masivo de pseudocodigo
- Herramienta: decompile_all, rango 0x5b0000-0x7a0000 (3346 funciones del gameplay).
- Carpeta: C:\Users\Sebas\AppData\Local\Temp\claude\C--WINDOWS-system32\09ac6e15-bec1-4a97-af78-4895e7b3a94b\scratchpad\decomp
  (functions\0xDIRECCION.c, decompile-index.json, decompile-journal.jsonl).
  Es carpeta temporal: si se pierde, se regenera (no es critico).
- Para continuar: decompile_all con la misma carpeta, address_start/end iguales, resume=true, limit<=800.
- Estado: ~1273 de 3346 hechas al cortarse (la sesion de REA se cerro sola ~06:27 con un limit de 2800;
  usar limits chicos de ~800 para que una caida no pierda mucho).
- Con el volcado completo: grep de "FUN_005b2d60" (setter de movimiento) y de constantes de movimiento.

## Hecho (resumen; detalle en re_notas.md)
- 0x4DBCF0 jugadas ocultas por ID; 0x629E30 gambeta; 0x620400/0x6204E0 random; 0x5BE390 goles.
- 0x65D774: error de direccion de patada, NO aplica a humano con nivel>=4 (por eso el NOP no hacia nada).
- 0x77a962 `SAR EDI,1` (D1 FF): punto de parche para anular el error de velocidad del tiro (propuesto, NO aplicado).
- FUN_005b2d60 = SET_MOVIMIENTO (anim+0x30); FUN_005b3f70 = transicion con blend; FUN_005b2f70 = init (idle 0x1BF).
- DAT_03be0fd8/fdc/fe0 = punto de contacto del pie segun animacion (FUN_006065d0).

## Proximos pasos
1. Terminar el volcado y hacer una tabla ID de movimiento -> funcion que lo dispara.
2. Identificar chilena / tijera / faltas (cruzar con movimientos_vistos.txt, que tiene 635 IDs sin nombre).
3. Tiro libre a la esquina: leer FUN_0075b020 (velocidad) y el efecto *(c20834+0x70); decidir como distinguir tiro libre de tiro normal.
4. Opcional: callers de 0x5BE390 para el evento de reacciones E/F.

## Avance del volcado (actualizar)
- ~07:20: 2071 de 3346 hechas (2 fallan: 0x607e30 y 0x6444f0, el decompilador murio; ignorarlas). Tanda siguiente (800) en curso.
- Tabla generada: re_movimiento_callers.txt (ID de movimiento -> funciones que llaman al setter 0x5b2d60 con ID constante). Se regenera con grep sobre decomp\functions cuando termine el volcado.
- IDs mas repetidos en llamadas al setter: 0x1BF (idle, 16 veces), 0x2B4, 0x114, 0x457, 0x391/0x392/0x393, 0x38C, 0xC1, 0x24, 0x11.

## Bitacora de incidentes
- 06:27 la sesion de REA se cerro sola durante decompile_all (limit 2800): se perdio el ajuste del timeout.
- ~07:00 reabierto; el reintento fallo por timeout 330000 (ajuste perdido). Se volvio a subir a 1800000 y se relanzo (limit 800, resume).
  LECCION: tras cualquier reinicio de REA, lo PRIMERO es update_analysis_settings (timeout 1800000), recien despues open_binary / decompile_all.

## Reglas
- Nada se modifico en el juego. WE9.exe / WE9_original.exe intactos. Los parches son solo propuestas.
- No ejecutar nada bajado de internet.
- Hablar en español rioplatense.

## Optimizacion (27/10 ~13:35)
- REA lanza Ghidra headless con -Xmx2G, ParallelGCThreads=2, CICompilerCount=2 (hardcodeado en rea-agents/dist/ghidra/GhidraJavaLaunch.js). El heap sale de la variable de entorno GHIDRA_HEADLESS_MAXMEM (o GHIDRA_MAXMEM). Para subirlo: setear GHIDRA_HEADLESS_MAXMEM=6G (usuario) y reiniciar Claude Code. PC: Ryzen 7 2700 (16 hilos), 16 GB RAM.
- Tras reiniciar REA: 1) update_analysis_settings ghidra.startup_timeout_ms=1800000 y ghidra.decompile_timeout_seconds=20 (salta funciones que cuelgan, ej. 0x607e30); 2) open_binary; 3) decompile_all resume=true limit<=200.
- El analisis inicial de Ghidra tarda ~3 min y Java usa ~1 GB; no es lo que revienta la RAM.

## Estado final del volcado (27/10 ~14:10)
- VOLCADO COMPLETO: 3340/3346 (fallan 0x607e30 0x6444f0 0x67f590 0x67f9e0 0x6e2010 0x6e43e0, ignoradas). close_binary hecho. Carpeta temporal: ...\scratchpad\decomp\functions\0xDIRECCION.c (OJO: los archivos se llaman 0x....c, no FUN_....c).
- re_movimiento_callers.txt regenerado (282 IDs). Hallazgos y candidatos de chilena/tijera en re_notas.md, Vuelta 10.
- Java de REA arranco con -Xmx2G (GHIDRA_HEADLESS_MAXMEM=6G no llego al proceso, hay que reiniciar Claude Code para que lo tome). Con 2G anduvo bien: RAM libre 8-11 GB, Java ~1.4 GB. Con timeout 20 s las tandas de 200 tardaron pocos minutos.
- Proximos pasos: (1) confirmar chilena/tijera jugando (IDs 0x24A, 0x24B, 0xE1-0xE7); (2) leer FUN_00630220 (valida el movimiento) y FUN_006db9c0/6902b0 completas; (3) tiro libre a la esquina (FUN_0075b020); (4) callers de 0x5BE390 para reacciones E/F.

## PLAN GAMEPLAY (27/10 ~14:40) - objetivo del usuario: entender TODO el gameplay (faltas, tiro libre, movimientos, reglas, flujo del partido); menus y resto del juego no importan por ahora. Despues: descompilar para hacer mods/su propio WE9.
- Volcado 1 (hecho): 0x5b0000-0x7a0000 -> scratchpad 09ac6e15...\decomp\functions (3340/3346).
- Volcado 2 (en curso): 0x400000-0x5affff -> C:\Users\Sebas\AppData\Local\Temp\claude\C--WINDOWS-system32\1baf3840-e19d-4b7b-8767-5b2b2b0037a3\scratchpad\decomp2 (resume=true, limit 200 por tanda; mismos address_start/end). Motivo: ahi viven jugadas ocultas (0x4dbcf0), goles (0x5be390) y probablemente reglas/faltas.
- Volcado 3 (pendiente): 0x7a0000-0x850000 (continuacion del gameplay).
- Que buscar: (1) quien decide una FALTA (colision/contacto entre jugadores, chequeos de estado de jugador en el suelo, silbato, tarjetas); (2) flujo del partido (saque, pelota fuera, gol, tiro libre, corner, penal): maquina de estados global; (3) tiro libre: callers de FUN_0075c940 (tipos 5); (4) seleccion de movimiento FUN_00630220.
- Cada hallazgo -> re_notas.md antes de seguir.

## Estado 27/10 ~15:00 (reemplaza al plan anterior)
- Volcados: 1 (0x5b-0x7a, 3340 fn, decomp\), 2 (0x40-0x5b, solo 400 de 8163, decomp2\), 3 (0x7a-0x80, 667 fn completo, decomp3\). Binario ABIERTO en REA (Java -Xmx2G, RAM libre ~11 GB). Si se cierra, reabrir: update_analysis_settings -> open_binary (analisis ~11 min).
- DESCUBRIMIENTO CLAVE: bucle principal FUN_005b9590 -> switch(nivel0) -> FUN_005b9350 (frame del partido) -> switch(nivel1) con ~30 fases. Ver re_notas.md vueltas 14-16 y re_estados_partido.txt.
- Siguiente: leer las rutinas de fase (lista en vuelta 17) para encontrar faltas / tiro libre / saques; anotar cada una en re_notas.md.

## Estado 27/10 ~15:35 (ULTIMO)
- Hallazgos nuevos: ver re_notas.md vueltas 19-21. Archivos nuevos: re_estados_partido.txt (niveles de estado del partido), re_estado_pelota.txt (17 estados de la pelota).
- Siguiente: leer FUN_005c4e10 (clasificador de estados de pelota), luego 0x5c9c70, 0x63f1c0, 0x640690, 0x6a6bb0, 0x645680, 0x66ee20, 0x731f70 para encontrar la FALTA y el TIRO LIBRE.
- Binario sigue ABIERTO en REA (Java ~1.2-2 GB). Si hay que reabrirlo: update_analysis_settings(startup 1800000, decompile 20) y open_binary (el analisis tarda ~11 min).

## Estado 27/10 ~17:35 (ULTIMO)
- Lectura estatica ya cubre: bucle principal, 33 fases del partido, juego en vivo, pelota (estructura y 17 estados), jugador (estado +0x42 y estructura), eventos y estadisticas, geometria de cancha y arco, tiros y pases. NO ubicada la FALTA.
- HERRAMIENTA NUEVA: Monitor\LoggerEstados.bat (ver re_notas.md vuelta 28). Usar cuando el usuario juegue: registra los estados reales y permite ubicar falta/tiro libre/corner/penal al instante.
- Si no se juega: siguiente lectura estatica = la IA FUN_005deeb0 (880 lineas) en trozos, FUN_00644d30 (jugadores; 6444f0 cuelga el decompilador: leer en asm), y funciones de colision/entrada (zona 0x68-0x6b: FUN_0068d140 = patada, FUN_006a57f0 = giro, etc.).

## Estado 27/10 ~20:15 (ULTIMO)
- Documento resumen: MAPA_GAMEPLAY.md (leer primero). Evidencia: re_notas.md vueltas 1-34. Tablas: re_estados_partido.txt, re_estado_pelota.txt, re_estado_jugador.txt, re_estado_jugador_manejadores.txt, re_movimiento_callers.txt, re_eventos_api.txt.
- Herramienta: LoggerEstados.bat (estados + entrada del humano). Compilado, sin probar con el juego.
- Pendiente (si se juega): 1) correr el logger y provocar falta/tiro libre/corner/penal/barrida; 2) mapear botones (bits 0x100-0x8000) apretandolos uno por uno; 3) leer 0x74c9d0, 0x7697b0, 0x724640, 0x635630, 0x6f98a0 (candidatos a contacto).
- Pendiente (sin jugar): leer el manejador de fase por puntero (0x7c0a30, 0x7be200, 0x7c1a50, 0x7f7640...) en asm; los estados 0x2E,0x32,0x33,0x46-0x4E,0x51; el origen de +0x8C (velocidad) en la inicializacion de jugadores (FUN_005e04b0).
- Binario sigue abierto en REA (Java ~2 GB).

## Estado 27/10 ~03:10 - HALLAZGO: SISTEMA DE FALTAS ENTENDIDO (re_notas.md vueltas 45-46, MAPA_GAMEPLAY.md 8c)
- Barridas = movimientos 0x305/0x306; falta/tarjeta/lesion en FUN_005d5ec0 / 5d3ef0 / 5d4a10; fase de falta = nivel1 = 6 (FUN_005d72b0).
- LoggerEstados ahora tambien registra el registro de falta (0x3B8F800/0x3B8F820). Compilado.
- Pendiente: manejador de la fase 6 (0x7c0a30, LAB sin funcion: leer en asm), FUN_005d39c0 (lesion), FUN_005d2d20, offside, penal (area), perfil del arbitro 0xFF6BEE (quien lo carga).

## Estado 27/10 ~05:15 - FALTAS, LESIONES, OFFSIDE Y FASE 6 ENTENDIDOS (re_notas.md vueltas 45-49, MAPA 8c/8d/8e)
- Pendiente: manejador de la fase 12 (offside, FUN_006711c0) y 0x10/0x20 (tras falta/lesion); quien carga el perfil del arbitro (0xFF6BB0) y los floats fisicos (0x3BE1A78/80); penal (area); cambios/sustituciones; menus fuera de alcance.
- Probar con el juego usando LoggerEstados.bat (ya registra faltas) para confirmar hipotesis.

## PLAN AUTONOMO (el usuario se fue ~3-4 h desde 27/10 ~16:20, queda la PC sola; seguir con tandas cortas y anotar todo)
Orden: (1) GOL: deteccion, quien suma el marcador, fase que dispara, celebracion y repeticion; (2) fase 12 (offside) y fases 0x10/0x20 (post-falta/lesion); (3) PENAL; (4) SAQUE DE CENTRO (kick-off); (5) cambios/sustituciones y tiempo (descanso, tiempo extra, penales); (6) quien carga perfil del arbitro 0xFF6BB0 y floats fisicos 0x3BE1A78/80 (usar xrefs/analyze_function); (7) pasar a limpio MAPA_GAMEPLAY.md. Reglas: no tocar nada del juego; cuidar RAM (Java ~2 GB, ok); todo hallazgo a re_notas.md ANTES de seguir.

## ACTUALIZACION 27/10 ~18:10 (autonomo)
- Vueltas 54-73 hechas: marcador/estadisticas (0x67f880), saque de centro y periodos, presentacion de fin de periodo, lesion/fase 0x10, plantillas/cambios/tacticas (0x472c00, 0x977e30, 0x66cfb0, 0x5bf3c0), arbitros (tabla estatica 0xD89CD8), constantes de fisica (0x7b6360, 0x623f80). Ver MAPA_GAMEPLAY.md secciones 8f, 11-14.
- Proximo: leer 0x400000-0x5affff selectivo (modulo de menus/plantel), confirmar con LoggerEstados jugando, y empezar a plantear parches (siempre en COPIA del exe).

## CIERRE AUTONOMO 27/10 ~19:30
- Vueltas 54-83 completadas (ver re_notas.md). Documentos al dia: `MAPA_GAMEPLAY.md` (secciones 8f, 11-15), `PROPUESTAS_MODS.md` (15 ideas con direcciones), `re_notas.md`.
- Sesion REA cerrada para liberar RAM. Para retomar: `open_binary` WE9_original.exe, ajustar `ghidra.startup_timeout_ms=1800000`, y los volcados de pseudocodigo siguen en el scratchpad `09ac6e15.../decomp` (si existe).
- Pendiente (requiere jugar o leer 0x400000-0x5affff): confirmar con `LoggerEstados.bat`; cambio manual de jugador (menus); cambio automatico de jugador controlado (`0x3BE0FBB`); lectores de `ent+0x254`; 9 bytes de rol tactico `ent+0x238..0x240`; direccion de la config de botones por pad.

## 28/10 - pedidos nuevos (sin hacer, solo anotado)
EscritorMemoria.exe creado (escribe/congela memoria en vivo, sin probar). Ideas anotadas en PROPUESTAS_MODS.md: sin faltas (parche en copia), tiro libre al angulo, mapa de habilidades ocultas, sistema de logros por polling. Correccion: 0x3BE0F74 es el reloj, no interruptor de faltas.

## 31/10 madrugada - ESTADO FINAL DE LA SESION (leer junto con re_notas.md vueltas 84-92)
- Logger (LoggerEstados.cs): v4 compilado y probado en 2 partidos; v5 (fuente corregida) compila pero falta instalar el .exe: cerrar la ventana de LoggerEstados.bat y compilar con el csc de .NET 4 (Framework\v4.0.30319\csc.exe /target:exe /out:LoggerEstados.exe LoggerEstados.cs).
- Lista de eventos del juego: [0xFF694C]+0x12EC (vuelta 88). Fichas de jugador: tabla [0x38B77C4]+id*0x7C en formato option file, datos desde +0x2F (vuelta 89).
- Proyecto externo analizado: E:\Descargas\we9-pes6-main (en uso por otras personas). Quedan sin leer: src\fafa\acciones.py (reglas de quites, intercepciones y pases), partidos.py, investigar.py, docs\integracion\NIVELES_POR_RENDIMIENTO.md, PLAN.md.
- REA: abierto con WE9_original.exe (Java ~2 GB). Cerrar con close_binary si no se sigue. Hz/FPS: ver vuelta 87 (la variable [0xDB4F00] es refresco Y limitador; subir solo los Hz no mejora; recomendado G-Sync a 60 Hz).
- Proximos pasos sugeridos: (1) instalar v5; (2) partido con humano para tarjetas, penal, lesion y botones; (3) leer acciones.py y portar quites e intercepciones; (4) sistema de logros por polling (ACCION 0x04 en vivo + lista del juego para confirmar); (5) decidir si se encara la interpolacion para FPS reales.
- 31/10 00:57: LoggerEstados v5 INSTALADO (LoggerEstados.exe compilado desde LoggerEstados.cs; equipo por bloque+0x40, cambios de jugador, REGISTRO_04/05, fases 16/17/27). Sin probar todavia en partido.

## 31/10 tarde - MENUS HD (ver CONTEXTO_SESION.txt seccion 11 y re_notas.md vueltas 93-98)
- Menus descifrados en e_text.afs / S_TEXT.AFS (924 PNG en E:\WE9_UI\png_e). Reempaquetado listo (tools\epack.py). Prueba con kitserver afs2fs preparada (kitserver\hd_menus, afs2fs.cfg con debug=1, backup .bak) y SIN confirmar en el juego. Retomar leyendo kitserver\WE9.log y preguntando que carteles rojos "E<entrada> R<recurso>" vio el usuario.
