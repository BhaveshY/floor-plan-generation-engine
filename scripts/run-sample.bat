@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-sample.ps1" %*
if errorlevel 1 (
  echo.
  echo Floor plan generation failed. Check the message above for details.
  pause
  exit /b %errorlevel%
)
echo.
echo Floor plan generation finished.
pause
