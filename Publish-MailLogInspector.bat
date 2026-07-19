@echo off
setlocal

echo Mail Log Inspector - Publiceren naar C:\Apps\Mail Log Inspector
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-MailLogInspector.ps1"

if %ERRORLEVEL% neq 0 (
    echo.
    echo FOUT: Publiceren mislukt. Controleer de uitvoer hierboven.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Gereed. Druk op een toets om te sluiten.
pause >nul
