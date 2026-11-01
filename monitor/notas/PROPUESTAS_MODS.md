# PROPUESTAS DE MODS / PARCHES (solo ideas, NADA aplicado ni probado) - 27/10
Regla: no tocar WE9.exe / WE9_original.exe. Probar siempre sobre una COPIA, o escribiendo memoria en caliente con una herramienta aparte. Todo `[H]` = hipotesis a confirmar con `LoggerEstados.bat`.
Detalle y evidencia: `re_notas.md` (vuelta indicada) y `MAPA_GAMEPLAY.md`.
Ver tambien: MAPA_GAMEPLAY.md (mapa consolidado), re_notas.md (hallazgos), CONTEXTO_SESION.txt seccion 9 (indice general). Decisiones del 28/10 al final de este archivo.

| # | Idea | Donde (VA) | Como | Riesgo / confianza | Vuelta |
|---|---|---|---|---|---|
| 1 | Cambiar la dificultad de la IA en caliente | byte `0x3BE0F4C` (0..5) | escribir 0..5 | bajo / C | 35 |
| 2 | Hacer la CPU mas/menos precisa por nivel | tablas `0xAEB1B8` (64,56,48,32,8,8) y `0xAEC7F8` (80,64,40,24,16,16) | editar bytes en la copia del exe | medio / H | 35 |
| 3 | Severidad de arbitros (faltas, tarjetas, tiempo agregado) | tabla `0xD89CD8 + id*0x68`: +0x46, +0x50, +0x51, +0x52 | editar en copia del exe | bajo / H en semantica | 71 |
| 4 | Duracion del partido | `0x3BE0F50` antes del partido (cada tiempo (opc+1)*150 s) o `0x3BE0F74` (frames restantes) en caliente | escribir | bajo / C | 53 |
| 5 | Marcador editable / contar goles dobles | shorts en `0x3B86FA4` (eq0) y `0x3B876E8` (eq1) | escribir | bajo / C goles | 54 |
| 6 | Pelota mas rapida/lenta o con mas curva | floats `0x3BE1A78` (arrastre), `0x3BE1A80` (divisor Magnus), `ball+0xE4` (friccion) | escribir DESPUES de `FUN_007b6360`/`FUN_00623f80` | medio / H | 72-73 |
| 7 | Sin faltas (modo arcade) | evitar `FUN_005d73e0` desde `FUN_005cbc60` | parche en copia, o mantener `DAT_03b8f804 != 0x7FFF` | alto / sin probar | 45 |
| 8 | Sin fuera de juego | `FUN_005da370`/`FUN_005d8e30` (marcan atacantes en offside) | parche en copia | medio / H | 48 |
| 9 | Sin lesiones | opcion `0x3BE0F4B = 0` | escribir (antes) | bajo / C | 47 |
| 10 | Forzar tiro libre/penal en un punto | `DAT_03be0fc8/fcc/fd0` + `FUN_005b9800(1,2)` | llamar/escribir | alto / H | 45-46 |
| 11 | Cambiar formacion/tactica de la CPU | `0x3B90D20 + eq*0x270` y llamar `FUN_005bf3c0(eq)`, bandera `0x3BE1070=1` | escribir | medio / H | 66 |
| 12 | Estilo de pase (corto/largo) por jugador | `entidad(0x3BCF880+i*0x290)+0x241` (0 corto x0.8, 1 normal, 2 largo x1.2) | escribir | bajo / C uso | 67 |
| 13 | Nuevo modo de juego | tabla de manejadores de fase `0x3BE148C..14D4` (instalada por `FUN_007c1aa0`) o `FUN_005b9800(nivel,valor)` | reemplazar punteros | alto / H | 17-18 |
| 14 | Mover la pelota/jugadores (editor) | fase 0xD (`FUN_00623150`), pelota `[0xC20834]`+0x10/0x14/0x18, jugadores `+0x120/+0x128` | escribir | bajo / C | 22 |
| 15 | Saque de centro / forzar periodos y prorroga | `0x3BE1456/57` y `FUN_005cf780`; `DAT_03be1c55` periodo | escribir/llamar | medio / H | 55 |

## Como probar (cuando vuelvas)
1. Abrir WE9.exe y luego `LoggerEstados.bat` (solo lectura; compilado, nunca probado con el juego).
2. Jugar un partido provocando: falta, tiro libre, corner, saque de arco, gol, offside, lesion; mirar `logs\estados_partido.txt` y comparar con `re_estados_partido.txt`.
3. Para los items de memoria en caliente se necesita una herramienta de ESCRITURA aparte (el logger NO escribe). Pedirme que la haga si querés.

## Herramienta de escritura en caliente (28/10)
`EscritorMemoria.bat` / `.exe` / `.cs`: consola que escribe o congela valores en WE9.exe EN EJECUCION (no toca archivos; cerrar el juego descarta todo; `restaurar` devuelve los originales). Atajos para los items 1, 4, 5, 6, 9, 12, 14 (`dificultad`, `duracion`, `goles`, `tiempo`, `lesiones`, `pase`, `pelota`, `jugador`, `arrastre`, `magnus`, `friccion`) y comandos genericos `ver/esc/cong`. Compilada, NUNCA probada con el juego. Usar primero en modo exhibicion/amistoso.

## Decisiones y pendientes (28/10) - NADA aplicado
- **Sin faltas**: CORRECCION: `0x3BE0F74` es el RELOJ (frames restantes), ponerlo en 0 termina el tiempo, no apaga faltas. Opciones: (a) parche en COPIA anulando la llamada a `FUN_005d73e0` dentro de `FUN_005cbc60` (hay que sacar los bytes con REA; hacer `WE9_sinfaltas.exe`, no tocar originales); (b) en caliente congelar `0x3B8F804 != 0x7FFF` (puede trabar el partido, sin probar).
- **Tiro libre siempre al angulo**: factible, falta ubicar el momento del disparo y el valor de apuntado (`+0xFC`, limite tiro libre +-0xD00 vs juego +-0x1800, vuelta de tiro especial). Pendiente 1-2 vueltas.
- **Habilidades ocultas**: NO mapeadas. `habilidades.txt` es plantilla (solo Elastica 0x5A bit3 y Regate 0x58 bit0, sin confirmar). Falta seguir que bits de los datos del jugador lee el codigo de movimientos/tiros.
- **Sistema de logros / carteles tipo F9-F10 del Monitor**: idea. Se haria leyendo memoria por polling (como `LoggerEstados`) y mostrando un cartel por encima (como WE9Monitor). Fuentes de eventos:
  - Gol: shorts de goles `0x3B86FA4` / `0x3B876E8` suben, o nivel1 = 0x17 [C].
  - Gol de tiro libre: fase 2 (tiro libre) o falta registrada `0x3B8F800` reciente + gol dentro de pocos segundos sin otro toque [H].
  - Falta / tarjeta: registro `0x3B8F800/0x3B8F820` (tipo, sancion, ids) [C]. Corner = fase 4, fuera de juego = fase 0xC, lesion = fase 0x10 [C].
  - Pase: transiciones de estado de jugador (`ent+0x42`, ver `re_estado_jugador.txt`) + pelota `[0xC20834]+0x80`; hay que confirmar con `LoggerEstados` que ids son pase corto/largo/filtrado [H].
  - Autor del gol / quien pateo: sin confirmar (probar `0x3BE0FB8..FBB` y entidad con estado de tiro al momento del gol) [H].
