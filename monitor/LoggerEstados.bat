@echo off
REM Registra los estados del partido de WE9 en logs\estados_partido.txt (solo lectura).
REM Abrir ANTES o DURANTE el juego; dejarlo abierto mientras se juega. Pide permisos de administrador.
cd /d "%~dp0"
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
LoggerEstados.exe %*
pause
