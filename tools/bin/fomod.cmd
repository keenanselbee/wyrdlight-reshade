@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\build-fomod.ps1" %*
exit /b %ERRORLEVEL%
