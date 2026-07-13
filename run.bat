@echo off
REM Builds mouseclicker.app (if needed) and launches it.
cd /D "%~dp0"
if not exist "MouseClicker.exe" (
    echo Building MouseClicker...
    powershell -ExecutionPolicy Bypass -File "build.ps1"
    if errorlevel 1 (
        echo Build failed.
        pause
        exit /b 1
    )
)
start "" "MouseClicker.exe"
