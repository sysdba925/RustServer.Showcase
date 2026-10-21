@echo off
setlocal
cd /d "%~dp0"
echo Publishing RustServer.Web - Production
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -Channel Production
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" (
    echo PUBLISH FAILED - exit code %result%. See the error above.
) else (
    echo PUBLISH SUCCESS - final version and release URL are shown above.
)
echo.
pause
exit /b %result%
