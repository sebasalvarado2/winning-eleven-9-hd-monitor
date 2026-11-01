@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x86 >nul
cd /d E:\WE9_UI\d3dhook
cl /nologo /LD /O2 /MT /EHsc d3d8proxy.cpp /Fo:build\ /Fe:build\d3d8.dll /link /SUBSYSTEM:WINDOWS
