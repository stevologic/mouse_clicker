@echo off
REM Builds ClickForge (if needed) and launches it.
cd /D "%~dp0"
if not exist "ClickForge.exe" (
    echo Building ClickForge...
    powershell -ExecutionPolicy Bypass -File "build.ps1"
    if errorlevel 1 (
        echo Build failed.
        pause
        exit /b 1
    )
)
start "" "ClickForge.exe"
