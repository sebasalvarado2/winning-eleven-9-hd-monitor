# MAPA DEL GAMEPLAY DE WE9 (resumen de la ingenieria inversa)

Resumen ordenado de lo entendido leyendo `WE9_original.exe` (base 0x400000) con REA + Ghidra.
Detalle y evidencia: `re_notas.md` (vueltas 1-29). Estado/como retomar: `re_estado.md`.
Nombres `FUN_xxxxxxxx` son los de Ghidra; los nombres en español son MIOS (propuestas), no del codigo original.
Confianza: **[C]** confirmado leyendo codigo/bytes, **[H]** hipotesis (falta confirmar jugando).

---------------------------------------------------------------------
## 1. Bucle principal [C]
```
FUN_005b96a0  tick por frame (red/replay)
  -> FUN_005b9590  switch(nivel0)
        0 FUN_005b86f0 init | 1 FUN_005b9060 | 2 FUN_005b87d0 | 3 FUN_005b9220 CARGA del partido
        4 FUN_005b9350  UN FRAME DE PARTIDO
             switch(nivel1) -> rutina de FASE (ver 2)
             despues, siempre: 5f8fe0, 63f540, 5be2f0 (eventos/estadisticas), 61d060, 624030, 5ba8c0, 5baf30, 5c89e0
```
Estado del partido = **10 niveles anidados** en `0xFF078C[0..9]` (VA). Escribir un nivel pone a 0 los mas profundos.
Setter `FUN_005b9800(nivel,valor)`, getter `FUN_005b9880(nivel)`. Tabla de quien fija cada valor: `re_estados_partido.txt`.
Modo de juego global: `DAT_03be1a8c` (valores vistos 4, 5, 7, 10, 14; el 5 tiene muchos predicados propios 0x616f30-0x617460) [H sobre su significado].

## 2. Fases (nivel1) [C la estructura / H los significados]
| nivel1 | rutina | que es |
|---|---|---|
| 0 | 645e20 | PRE-PARTIDO: carga, intro, inicializa 23 jugadores (nivel2 2..0xE) |
| 1 | 674590 | **JUEGO EN VIVO** (IA 5deeb0 + jugadores 644d30 + pelota 5cedb0) |
| 2 | 65eb80 | REANUDACION / pelota parada (ejecutor, tiro directo si cerca del arco) |
| 3 | 5dc250 | pelota fuera por banda / otra reanudacion (pipeline) [H baja] |
| 4 | 672f10 | **TIRO DE ESQUINA (corner)** [H alta: bandera en (±13465, ±8729)] |
| 9 | 674440 | **SAQUE DE ARCO** [H media-alta] |
| 8 | 606490 | arranque en modo 7 |
| 0xA (10) | puntero | ESPERA / entre momentos (entretiempo, fin, repeticion) |
| 0xB | 5bdfb0 | arranque modo 10; pausa tras evento |
| 0xD | 623150 | **editor de posicion de pelota** (mover con cruceta) |
| 0x18, 0x1C | 671830, 670d70 | variantes de reanudacion |
| otros 6,7,0xF-0x17,0x19-0x1B,0x1D-0x20 | punteros `DAT_03be148c..14d4` | manejadores por defecto instalados por `FUN_007c1aa0` (en 0x7bc000-0x7f7640; Ghidra no los reconoce como funciones) |
Todas las fases de pelota parada comparten esqueleto en nivel2 y terminan en `FUN_005da700` (= juego).

## 3. La pelota (puntero `[0xC20834]`) [C]
Posicion X `+0x10`, ALTURA `+0x14`, Z `+0x18`; previa `+0x1c/+0x20/+0x24`; velocidad `+0x4c`; direccion `+0x54` (angulo 16 bits);
angulo al arco rival `+0x58`; efecto/giro `+0x70`; **estado `+0x80`** con historial `+0x82..+0x96`; tick `+0x98`.
Historial de posiciones reales en `+0x468 + i*4` (X) y `+0xB68 + i*4` (Z).
Setter de estado: `FUN_005c8830`. Tick por frame: `FUN_005cedb0` -> fisica `FUN_005c8b20` -> colision con arco `FUN_00640690` -> linea de fondo `FUN_0063fd70`.
Estados (0..0x10): tabla de quien los fija en `re_estado_pelota.txt`. [H]: 1-4 tipos de golpe; 0xB-0xE reanudaciones/contacto; **0xD rebote en palo/red**; **0x10 arquero con la pelota** (`DAT_03be0fb8` = quien la tiene).
Clasificador contacto->estado: `FUN_005c4e10`.

## 4. Geometria [C]
- Linea de fondo/gol: **|X| = 13465**. Palos en **(X=13440, Z=±951)** (arco de 1902 u de ancho). Pelota radio de choque ~40.
- Limites: banda |Z| ~ 8345 (solver), reanudacion |X|<=13401 y |Z|<=8601. Jugadores pueden moverse hasta |X|<=13721, |Z|<=8985.
- Saque de arco en (±12089, ±2280). Zona de area chica: |Z|<2344 con |X|>10649..12057.

## 5. Jugadores [C]
Array de 23 jugadores en `0x3BCF880`, 0x290 bytes c/u (indice 0..22; 0xFF se trata como 1; `% 0x17`).
`+0x00` id, `+0x04` objeto animacion (`+0x30` = movimiento actual, `+0x38` frame), `+0x3F` numero, `+0x40` equipo, `+0x41` lado (±1 = direccion de ataque),
**`+0x42` ESTADO de accion**, `+0x44` subestado, `+0x50` humano, `+0x52` angulo, `+0x77` receptor del pase, `+0x79` modo, `+0x7C` potencia, `+0xF8/+0xFC` destino, `+0x100` cansancio, `+0x120/+0x128` X/Z.
Fichas/stats: `0x3BD3E30` + (equipo*0x20+numero)*0xD1 dwords.
**Update por frame: `FUN_006444f0`** -> switch por estado -> tabla en `re_estado_jugador_manejadores.txt`. Ejemplos: 0x30 = patear (`FUN_0065b5a0`), arquero = zona 0x74-0x75, 0x0E recibir/controlar (`FUN_005c9860`).
IA: `FUN_005deeb0` (880 lineas). Orden/control: `FUN_0077c760`. Control humano: `FUN_0066dec0`.

## 5b. Velocidad y control [C]
- Velocidad actual `+0x48`; velocidad MAXIMA `+0x100 = 0x1900 - 12*b` con `b = byte +0x8C` (de la ficha; b mayor = mas lento). Topes por contexto: 0x1900 correr, 0x1100 regate, 0x1D00 sprint, 0x1700/0x1300/0xD00 otros (todos `- 12b`). Aceleracion `FUN_005fe2c0` = (0x1900-12b)/(DAT_03be1c7c*param/60).
- Estado 0x0E (`FUN_006c58f0`) = control/conduccion de pelota (movs 0x319-0x36C, 0x1D7, 0x37F...). Estado 0x0B (`FUN_00701e70`) = perseguir/interceptar con punto predicho `DAT_03be1090/1094` (`FUN_00760ae0`).
- Entrada: `jugador+0x108` mascara; direcciones 0x10/0x20/0x40/0x80; botones desde 0x100 (0x800 = control/toque, 0x200 = accion principal).

## 5c. DIFICULTAD DE LA IA [C uso / H semantica]
- Byte `DAT_03be0f4c` (VA 0x3BE0F4C) = nivel 0..5, parte del bloque de ajustes 0x3BE0F40-0x3BE0F5F. Usado en 95 lugares de 44 funciones (ver re_notas.md vuelta 35).
- Tablas por nivel (leidas del exe): `0xAEB1B8` = 64,56,48,32,8,8 (aleatoriedad de decision de la CPU, `FUN_005f0fc0`); `0xAEC7F8` = 80,64,40,24,16,16 (`FUN_006f1800`).
- Humano: error de direccion de patada solo si nivel < 4 (`FUN_0065b5a0`:903); asistencias en niveles 0-1; ramas dificiles en nivel > 3 y 5.
- Mod: escribir 0x3BE0F4C en caliente cambia la IA; editar las dos tablas cambia la precision de la CPU por nivel.

## 5d. Posicionamiento y tactica [C estructura / H significados]
- Jugador sin pelota: estado 0x01 (`FUN_007697b0`) corre al punto objetivo `jugador+0xC0/+0xC4` (X,Z) con movimiento 0x22A; estado 0x1B (`FUN_0074c9d0`) = ir a recibir el pase / pelota suelta (predice la llegada con `FUN_005ec4d0`).
- Los objetivos los calculan: `0x60e9d0`, `0x723f60`, `0x60f310`, `0x62df50`, `0x7ac150` y otras (tactica/formacion). `0x723f60`: saque de arco (fase 9?) ejecutor en X=±13209, resto ±8729.

## 5e. Reanudaciones: circulo de exclusion [C]
- Escala: ~260 unidades por metro. `FUN_0060e9d0` empuja los objetivos de los jugadores fuera de un circulo alrededor del punto (radio `FUN_0060d5c0`: 0xA00=2560 por defecto, 0x500 el que saca en corner, 0x700/0xA40/0xB00 en fase 3). Recorte final de objetivos: |X|<=13337, |Z|<=8601.
- `FUN_0062df50` decide si un jugador va al punto predicho de la pelota: usa el temporizador `_DAT_03be1a28` (frames desde el ultimo toque) y los umbrales `DAT_03be1a2e..1a4c` (tiempos de reaccion de la IA; inicializados en runtime).

## 5f. Colocacion para reanudar y ejecutor [C estructura]
- Indice 0 del array de 23 = **arbitro** [H alta]. `FUN_00656a10` coloca a los 22 (objetivo por `FUN_006534c0` + ruido por stat de disciplina), `FUN_00656430` elige al ejecutor (salta arqueros 1/0xC, invisibles, estado 0x46). `DAT_03be0ff4[equipo]` = linea defensiva / fuera de juego [H].
- Fase 3: `5da500` (coloca) -> `5da5b0` (espera frames) -> ... -> `5da680` -> `5db580` (ejecutor a la pelota) -> `5db8b0` -> juego. El ejecutor usa el estado 0x2C (`FUN_005dc370`). Fase 2 = tiro libre/reanudacion con barrera (r=2560) [H].
- Condicion/forma: stats BASE 0x3BD3E4C -> ACTIVOS 0x3BD3E24 via `FUN_005e3960` con la forma (`FUN_005d2110`).

## 5g. Estados de ejecutores de saque [C flujo / H nombres]
- 0x2B arquero con la pelota (`FUN_00645680`, ent. `645070`), 0x36 arquero reparte (`FUN_00758140`, pelota 9), 0x2C saque con pie (`FUN_005dc370`, pelota 6), 0x2D recoger y lanzar (`FUN_00673790`, pelota 7; saque de banda?), 0x30 patear (`FUN_0065b5a0`).
- Traductor de animaciones `FUN_006d8ff0` (re_traductor_animaciones.txt): codigo de accion -> 17 ranuras 0x5CA-0x5DA segun el paquete.

## 5h. Tiro libre / penal / barrera [C flujo / H nombres]
- Estado 0x2F (`FUN_00671fc0`) = espera + APUNTADO: `+0xFC` = angulo de apuntado, limitado a ±0xD00 del arco; botones 0x2000 (`+0xF8=3`) y 0x4000 (`+0xF8=1`) lanzan la maquina de patear (estado 0x30). 10649 = punto de penal.
- Estado 0x35 (`FUN_0065f9d0`) = BARRERA: se agacha/gira (mov 0x108) o salta (mov 0x109) al salir el tiro. Estado 0x31 = espera pasiva.
- `DAT_03be1098` = tipo de jugada (6 = saque de centro/pre-partido, 8 = ejecutando el saque, ...). `DAT_03b8e888 == 2` = tiro directo a puerta.

## 5i. Botones y potencia de la patada [C]
- Mascara de flancos `jugador+0x10C` / mantenida `+0x108`. Botones (`FUN_00659010`): **0x4000 tiro, 0x1000 tiro con efecto, 0x2000 pase, 0x8000 pase largo/centro (solo en campo de ataque), 0x400 modificador**, 0x200/0x800 control/toque, direcciones 0x10/0x20/0x40/0x80. Tipo de patada `+0xF8`: 1 tiro, 2/3 pase, 4 pase largo.
- Potencia `+0x7C`: sube +4 por frame mientras se mantiene el boton (sub 0x37), tope 0x80. Apuntado `+0xFC` limitado a ±0x1800 (juego) o ±0xD00 (tiro libre) del arco. Velocidad del tiro especial = (potencia>>3)*0xB0+0x480.

## 5j. Contacto de la patada [C]
- Sub 8 de `FUN_0065b5a0`: en el frame de contacto, si la pelota esta a <256 u del pie y `FUN_0062caa0` ok -> `FUN_00778400`; si no, falla. Direccion `+0xFC` (humano) / `+0x5A` (CPU) con error `rand_sym(0x7F6-norm*0x12)` solo para CPU o dificultad < 4. Movimientos de tiro: 0xC9 (frente), 0xCA (giro), 0x0A, 0x0F (toque suave), 0xEE. Cabezazo/pelota alta: estados 0x09/0x40 [H].

## 5k. Fisica de la pelota [C]
- Paso `FUN_005c8b20` (real y simulada). Eje vertical invertido: suelo = -28.0, mas negativo = mas alto. Modo `ball+0x5e`: 0 quieta, 1 aire, 2 rueda, 3 adherida a un jugador, 4 desviada.
- Aire: gravedad 188 u/frame2 (*dt) + Magnus `efecto*vel^2*4.069e-5/_DAT_03be1a80`; arrastre `_DAT_03be1a78`. Rebote: restitucion 0.53-0.625, pierde 4.7-15.6% de velocidad horizontal. Rodando: -20/frame y friccion `ball+0xE4`; se detiene a vel<0x100. Velocidades en unidades/256 por frame.
- Predictor: `FUN_005caca0` (rand 0..223 frames por tick) llena X/alt/Z/angulo/velocidad[224]; `FUN_005cae70` busca cuando cae.
- Constantes fijadas al cargar por `FUN_007b6360`: `0x3BE1A78` = 4551266.0 (arrastre aire), `0x3BE1A80` = 1400000.0 (divisor Magnus), `0x3BE1A84` = 160.0, `[[0xC206C0]] = 60/fps` (paso de tiempo). Friccion de piso `ball+0xE4 = ((v-100)*0.02+1)*160` (`FUN_00623f80`). Mods: escribir esos floats DESPUES de la carga [sin probar].

## 2b. TIPO DE JUGADA <-> FASE [C la correspondencia / H los nombres]
- `DAT_03be1098` (byte): 1=fase 3 (banda?), 2=fase 4 (corner?), 3=fase 0x18, 4=fase 12 (offside), 5=fase 9 (saque de arco), 6=fase 0 (saque de centro), 7=fase 8 (TANDA DE PENALES, modo 7), 8=ejecutando el saque, 9=fase 0x1C.
- Fase 0x17 = GOL (bandera `DAT_03be1552` levantada por `FUN_0063f540` cuando `FUN_0063f130`: |X|>=13465, |Z|<939, |alt|<625).
- Fase 12 = offside / pelota detenida (despachador `FUN_006711c0`): pasa a la fase 2 (tiro libre) o a la 0x17 si entra un gol.
- Modo 7 (`DAT_03be1a8c`) = tanda de penales, `DAT_03be1c55 = 4`.

## 5l. Reloj del partido [C]
- Periodo `DAT_03be1c55` (0/1 tiempos, 2/3 extra, 4 penales). Duracion `DAT_03be0f50`: cada tiempo dura (opc+1)*150 s (extra: *50 s). Restante en frames: `DAT_03be0f74` (decrementa `FUN_005b8f50`; init `FUN_005be180` = fps*(opc+1)*(150|50)). Minuto base 0/45/90/105. Tiempo agregado `DAT_03be0f78` (max 9 min), anunciado al azar en el ultimo minuto. Minuto actual `FUN_005be040`.
- Mod: escribir `DAT_03be0f74` en caliente alarga/acorta; `DAT_03be0f50` antes del partido.

## 6. Movimientos (animaciones) [C]
Setter `FUN_005b2d60(anim, id, frame)`; ~500 llamadores; tabla constante ID->funciones en `re_movimiento_callers.txt` (282 IDs).
Los IDs vistos en juego estan en `movimientos_vistos.txt` (sin nombre todavia). Tabla de animaciones: puntero `[0xC206C0]`.

## 7. Tiros y pases [C]
- Patear: `FUN_0065b5a0` (maquina de estados) -> `FUN_00778400` (error del tiro, ver punto de parche `0x77A962`).
- Pase: `FUN_006853a0` / `FUN_0075c940`. **Solver de trayectoria `FUN_0075b020`** simula la pelota hasta encontrar potencia/elevacion.
- Error de direccion `0x65D774` no actua con humano si `DAT_03be0f4c >= 4`.
- Gambeta limpia = `FUN_00629E30`; jugadas ocultas por ID = `FUN_004DBCF0` (ver contexto del mod).

- **LANZAR LA PELOTA: `FUN_005cb310(jugador, angulo, velocidad, elevacion, reset)`** = unico punto por donde sale cada patada (34 llamadores). Pone ball +0x54 angulo, +0x2c elev*-188, +0x4c velocidad, limpia poseedor (DAT_03be0f82/0fb8), flag +0x5e (0 quieta, 1 aire, 2 piso). Wrapper `FUN_005cb4d0` fija el efecto +0x70 = potencia/4.
- Despeje: `FUN_006b4a20` (estados 0x0D/0x37/0x44), ultima accion 0x13, pelota estado 4 o 0xA.

## 8. Reglas, eventos, estadisticas [C estructura / H significados]
- Pelota fuera/reanudacion: `FUN_0063fc70/63fe30/63ff30/640390`, `FUN_006579e0` (punto y ejecutor), `FUN_00657580`.
- Eventos: `FUN_0067ee00` + tabla estatica `0xAEC238` (estado de pelota -> tipo de evento); estadisticas por equipo en `0x3B86FA4 + equipo*0x744` (goles al inicio).
- Ultima accion sobre la pelota: `DAT_03be0fe5` (8 tiro, 0x15 pase CPU...).
- Comentarista: `FUN_0067c7d0`.
- Faltas / tarjetas / offside: YA UBICADAS (ver 8c-8e). Falta confirmar nombres jugando con `LoggerEstados.bat`.

## 8b. Arquero [C estructura]
- Acciones por estado de jugador: 0x21 estirada con rechace (`FUN_00755040`, pelota estado 8 si queda cerca de su arco), 0x22 atajada principal (`FUN_00746330`), 0x1E/0x20 liberan la pelota, 0x1F, 0x29, 0x42 atajadas cortas, 0x2A, 0x24-0x28, 0x52 (escena).
- Ayudantes: `FUN_007a2e80` (alcanza?), `FUN_007a3d40` (direccion del rechace), `FUN_007a4190/4220` (alcance). El rechace sale por `FUN_005cb310`. Flag de pelota `+0x5e == 4` = rechazada por el arquero.
- Stats: ficha `DAT_03bd3e38>>7`, `DAT_03bd3e28`, `DAT_03bd3e34>>0xe`.

## 8c. FALTAS, TARJETAS Y LESIONES [C flujo / H algunos significados]
- Cadena: `FUN_005cbc60` (disputa jugador-pelota) -> `FUN_005d73e0` (si nivel1==1, sin decision pendiente `DAT_03b8f804==0x7FFF` y `DAT_03be0f74!=0`) -> `FUN_005d5ec0` (clasifica: el que entra en estado 0x0C/0x0F/0x43 con mov 0x305/0x306=barrida; geometria; area Z<5160) -> `FUN_005d3ef0` (registra, suma dano a `DAT_03bd3ea0`, lesion si dano>0xF) -> `FUN_005d4a10` (acumulado `DAT_03bd3e9c` + umbrales del perfil del arbitro `0xFF6BEE+0x68*i` -> tipo `DAT_03b8f801`: 1 falta, 2 amarilla, 3 roja, 4 roja directa) -> `FUN_005d72b0` -> nivel1=6.
- Decision pendiente: `DAT_03b8f800` tipo, `DAT_03b8f801` sancion, `DAT_03b8f804` demora (0x7FFF = nada), `DAT_03b8f808` bytes infractor/victima, `DAT_03be0fc8/fcc/fd0` punto del tiro libre.
- Mods: NO usar `0x3BE0F74` (es el tiempo del partido, ver 8d). Sin faltas: mantener `DAT_03b8f804 != 0x7FFF` o parchear la llamada a `FUN_005d73e0` en `FUN_005cbc60` [sin probar]. Editar el perfil del arbitro (8d); umbrales 0x23/0x32/100/0x28/0x5A.

## 8d. Lesion, perfil del arbitro, tiempo agregado [C/H]
- Lesion: `FUN_005d39c0` (solo si opcion `0x3BE0F4B` != 0, jugador caido y >=8 jugadores disponibles): puntaje = (propension + condicion*300 + dano/3 + rand(6) + bono)*0.1693; gravedad 1/3/4/5 segun rand; nivel de lesion por jugador `DAT_03bd3eb0`; log de eventos tipo 2 (`FUN_005d2d20`).
- Perfil del arbitro: estructura de 0x68 bytes en `0xFF6BB0 + i*0x68` (memoria de ejecucion): +0x46 (VA 0xFF6BF6) prob. de pitar, +0x50 (0xFF6C00) indulgencia en el area, +0x51/+0x52 tiempo agregado. Tiempo agregado: `FUN_005d1ee0` -> `DAT_03be0f78`.
- `DAT_03be0f74` = FRAMES RESTANTES del periodo (reloj, ver 5l); NO es interruptor de faltas (las faltas solo operan mientras sea != 0).

## 8e. Fuera de juego y fase de falta [C/H]
- **Offside**: `FUN_005da370` (tras cada pase marca en `DAT_03b8f742` un bit por companero mas alla de la linea `DAT_03be1004[rival]`), `FUN_005d8e30` (cada frame: atacante a <256 u de la pelota y pelota >64 u pasada la linea -> `FUN_005d8d50`), `FUN_005d95c0` (cuenta atras `DAT_03b8f748`; confirma y fija el punto del saque). Fase 12 (0xC).
- Lineas por equipo: `FUN_00681960` (`DAT_03be1004` = 2do mas atrasado = linea de offside; `DAT_03be0ff4` = mas adelantado).
- **Fase 6 (falta)**: 7 pasos (tabla `0x7C0AE0`): pitazo `7c0070` -> `7c03b0` -> camara `7bf010` -> repeticion `7be950` (gesto 0x489 del arbitro) -> `7beb60` -> `7beeb0` -> cierre `7c0530` (lesion -> fase 0x20, si no 0x10). Resolvedor: `FUN_005d7910` (1 pitar, 2 ventaja).
- Todos los manejadores de fase son `switch(nivel2)` con tabla de saltos.

## 9. Como modear a partir de esto (ideas, NO probado)
- Nuevo modo: reemplazar la tabla de manejadores de fase (`DAT_03be148c..14d4`) o parchear `FUN_007c1aa0`.
- Forzar eventos: llamar `FUN_005b9800(nivel,valor)` / `FUN_005c8830(estado)`.
- Tiro libre a la esquina: dar al solver `0x75B020` distancia/elevacion/efecto fijos, o escribir la velocidad de la pelota real.
- Pelota/jugadores editables en memoria con las estructuras de arriba.

## 10. Herramientas
- `LoggerEstados.bat`: registra estados reales (solo lectura) -> `logs\estados_partido.txt`.
- Archivos de tablas: `re_estados_partido.txt`, `re_estado_pelota.txt`, `re_estado_jugador.txt`, `re_estado_jugador_manejadores.txt`, `re_movimiento_callers.txt`, `re_eventos_api.txt`.
- Volcados de pseudocodigo (temporales, se regeneran): `...\scratchpad\decomp`, `decomp2` (parcial), `decomp3`.

## 11. Saque de centro, periodos y presentacion [C flujo / H nombres] (vueltas 55-57)
- Fase 0 (`FUN_00645e20`): arranque fija `DAT_03be1098=6`; paso 10 inicializa la cancha (`FUN_00645b10` coloca 22 jugadores, `DAT_03be0fe5=0xFF`, `DAT_03b8f801=0`); pasos 0xF/0x10 corren `FUN_00644d30` + `FUN_005cedb0` (mismo motor que el juego en vivo) con `DAT_03be0f7c` = frames desde el saque.
- Cambio de periodo `FUN_005cf780`: carga periodo/lado desde `0x3BE1456/57`, limpia jugadores, nivel1=0xA nivel2=2, o `DAT_03be1c3c=1` (fin del partido).
- `FUN_005d14e0` (cada frame) = secuencia de PRESENTACION de fin de periodo/partido (hasta 6 pantallas, `DAT_03be144d` indice, `DAT_03be144e` etapa). No es el menu de cambios (esos estan fuera del gameplay, sin ubicar).

## 12. Alineacion, cambios y tacticas [C estructura / H significados] (vueltas 62-69)
- Plantilla: `0x3BD3DD0 + eq*0x6880 + i*0x344` (32 fichas por equipo, la carga `FUN_00472c00`). Lista de orden de plantel: `0x3BE1EC6 + eq*0xDE` (32 bytes; los 11 primeros = titulares), formacion/estilo `0x3BE1EC5`.
- Registro de cambios: `0x3BE1F11/12 + fila*0x14 + eq*0xDE` (7 filas: sale, entra), contadores `0x3BE106A/6C/6E[eq]`; bandera `DAT_03be1070 = 1` = alineacion cambiada. Aplica un paquete completo `FUN_00977e30(eq, paquete)` -> `FUN_0066cfb0` (alineacion y 3 formaciones) -> `FUN_005bf3c0` (copia la formacion activa a las entidades).
- Tactica por equipo: `0x3B90D20 + eq*0x270` (3 formaciones x 171 bytes; seleccion en `+1`). Por jugador, 9 bytes en `entidad+0x238..0x240` (SIN lector directo hallado) y `entidad+0x241` = estilo de pase (0 corto x0.8, 1 normal, 2 largo x1.2) [C].

## 13. Arbitros [C tabla / H semantica de campos] (vuelta 71)
- Tabla estatica de perfiles en el exe: `0xD89CD8 + id*0x68` (>=4 arbitros). `FUN_009aa440` copia 4 al perfil de ejecucion `0xFF6BB0 + ranura*0x68` al cargar el partido. Campos usados por faltas/tarjetas: `+0x46` (id0=50, id1=65, id2=40, id3=40), `+0x50` (50/40/60/40), `+0x51` (90/40/60/40), `+0x52` tiempo agregado (0/10/15/10). Editar esa tabla cambia la severidad del arbitro.

## 14. Estado de cobertura (27/10 ~18:10)
- Leido: 0x5b0000-0x7fffff completo (maquina de estados, fases, jugadores, pelota, faltas, offside, reloj, goles/estadisticas, arqueros, saques, tanda de penales, tacticas/alineacion/cambios aplicados, arbitros, fisica).
- NO leido: 0x400000-0x5affff (menus, plantel, escritores de la tabla de cambios, cargadores de opciones) y 0x800000+ (libreria/render/audio/red). Para entender los menus de cambio/tactica y opciones habria que leer `0x472c00`, `0x977e30` y vecinos.
- Sin confirmar en juego (usar `LoggerEstados.bat`): nombres de estados de jugador/pelota, significado de los contadores [6]-[13] de estadisticas, 9 bytes de rol tactico, sentido de `v` en la friccion.

## 15. Control, ordenes e input [C estructura / H significados] (vueltas 75-80)
- Update por entidad `FUN_006444f0`: (1) `FUN_0077c760` ordenes/control; (2) clase por `FUN_00641830` (arbitro = 0xF; arqueros `ent+0x76=8`); (3) clase <=0xE -> rutina fija, si no `FUN_005b2e00` (anim) + despacho por `ent+0x42` (`0x644C30`/`0x644B20`); (4) cierre: `FUN_0073a180` y decremento de temporizadores `+0x92,+0x6C,+0x93,+0x95,+0x6F,+0x6D,+0x72,+0x75,+0x6E,+0x142`.
- Tabla de ORDENES: `0x3B8F4A0 + fila*0x2E` (22 filas): `+0` tipo (0..7 = ranura humana, 8 = arquero/especial, 0xFF = libre), `+1` equipo, `+3` subrol, `+0xA` indice de entidad, `+0xE` id. `ent+0x76` = tipo de la fila asignada; **`ent+0x50` = 0 humano / 1 CPU / 2 especial** (`FUN_005dd540`). `DAT_03b8f610/611` = fila seleccionada de cada equipo.
- INPUT: `FUN_005e13f0` lee 8 dispositivos a `0x3B8F660 + d*0x18` (+0 flancos, +4, +8 mantenidos, +0xC) tras `FUN_005e1210` (traduccion) y `FUN_006106c0` (remapeo por pad). `FUN_005c53e0/5e1810` copian a `ent+0x108` (mantenida), `+0x10C` (flancos), `+0x110` (ultima valida, congelada si `+0x6E != 0`), `+0x13C/0x13E` (ejes del stick). `DAT_03be0f1a` (0/0x4000/0x8000) = orientacion de camara que rota el stick.
- Botones de juego: bits 8-15 (0x100..0x8000). Mods: inyectar input en `0x3B8F660/668`; cambiar `ent+0x50` para dar/quitar control.
