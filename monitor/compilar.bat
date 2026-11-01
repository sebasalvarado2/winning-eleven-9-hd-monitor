@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize /out:WE9Monitor.exe WE9Monitor.cs
if %errorlevel% neq 0 (echo Error al compilar & pause & exit /b 1)
echo Compilado: WE9Monitor.exe
