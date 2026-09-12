@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -DesktopShortcut
if errorlevel 1 (
  echo Installation failed. Please read the message above.
) else (
  echo Installation complete. Open ShuiMan from the Start menu.
)
pause
