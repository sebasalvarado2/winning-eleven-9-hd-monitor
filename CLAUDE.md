# Contexto para asistentes (Claude Code)

Proyecto: Winning Eleven 9 PC. Dos partes: `monitor/` (ingeniería inversa del gameplay) y `we9ui/` (menús HD). Leer `README.md` de cada una.

## Reglas
- **Nunca** modificar `WE9_original.exe`. `WE9.exe` es copia: se puede parchear, con backup fechado y nota en `monitor/notas/re_notas.md`.
- we9ui: solo menús/HUD/logos/interfaz. **Fuera de alcance:** camisetas, caras, pelo, público, todo lo que maneje kitserver.
- Ultrawide abandonado por el usuario: no retomar salvo que lo pida.
- No ejecutar herramientas descargadas sin permiso explícito (el usuario autorizó Real-ESRGAN ncnn-vulkan).
- Sin etiquetas rojas de prueba en el juego final.
- Responder preguntas como preguntas; no construir entregables no pedidos.
- Idioma: español rioplatense.

## Por dónde empezar
- Monitor: `monitor/notas/CONTEXTO_SESION.txt`, `re_estado.md`, últimas vueltas de `re_notas.md`.
- WE9UI: `we9ui/README.md` y `we9ui/docs/PRUEBAS_PENDIENTES.txt`.

## Pitfalls técnicos
- Proxy d3d8: solo parches de vtable encadenados e inline hooks; nada de wrappers COM; no agrandar texturas al crearlas.
- Rutas fijas en `E:\` en los scripts.
