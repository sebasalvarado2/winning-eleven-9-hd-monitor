@echo off
title Winning Eleven 9 - Registro 64 bits

:: Pedir permisos de administrador (necesarios para escribir en HKLM)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Pidiendo permisos de administrador...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "KEY=HKLM\SOFTWARE\WOW6432Node\KONAMIWE9\WE9"
set "DIR=%~dp0"

echo.
echo  Carpeta del juego: %DIR%
echo.
echo  Idioma:
echo    1. Espanol
echo    2. Ingles
echo.
choice /c 12 /n /m " Elige 1 o 2: "
if errorlevel 2 (set "ES=0" & set "EN=1") else (set "ES=1" & set "EN=0")

reg add "%KEY%" /v installdir  /t REG_SZ    /d "%DIR%\" /f >nul
reg add "%KEY%" /v installfrom /t REG_SZ    /d "%~d0\" /f >nul
reg add "%KEY%" /v code        /t REG_SZ    /d "NXUDPACVEM2XKPC96AYU" /f >nul
reg add "%KEY%" /v text_s      /t REG_DWORD /d %ES% /f >nul
reg add "%KEY%" /v text_e      /t REG_DWORD /d %EN% /f >nul
reg add "%KEY%" /v lang_s      /t REG_DWORD /d %ES% /f >nul
reg add "%KEY%" /v lang_e      /t REG_DWORD /d %EN% /f >nul
reg add "%KEY%" /v lang_f      /t REG_DWORD /d 0 /f >nul
reg add "%KEY%" /v lang_g      /t REG_DWORD /d 0 /f >nul
reg add "%KEY%" /v lang_i      /t REG_DWORD /d 0 /f >nul
reg add "%KEY%\1.0" /f >nul

if %errorlevel% neq 0 (
    echo  Error al escribir en el registro.
) else (
    echo.
    echo  Listo. Ejecuta settings.exe para configurar y luego WE9.exe para jugar.
)
echo.
pause
