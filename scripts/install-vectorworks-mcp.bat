@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%install-vectorworks-mcp.ps1" %*
if errorlevel 1 (
  echo.
  echo Vectorworks MCP setup failed. Check the message above for details.
  pause
  exit /b %errorlevel%
)
echo.
echo Vectorworks MCP setup finished.
pause
