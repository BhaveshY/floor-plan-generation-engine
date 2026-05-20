@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-web.ps1" %*
if errorlevel 1 (
  echo.
  echo Floor Plan Engine Web failed to start. Check the message above for details.
  pause
  exit /b %errorlevel%
)
