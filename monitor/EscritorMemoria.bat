@echo off
REM Escribe/congela valores en la memoria de WE9 EN VIVO (no toca archivos). Abrir con el juego ya corriendo.
REM Pide permisos de administrador. Escribir "ayuda" dentro. "restaurar" devuelve todo.
cd /d "%~dp0"
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
EscritorMemoria.exe %*
pause
