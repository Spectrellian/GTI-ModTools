@echo off
setlocal

REM Ensure execution from repository root (location of this .bat file).
cd /d "%~dp0"

if not exist "Base" mkdir "Base"
if not exist "Image_In" mkdir "Image_In"
if not exist "Image_Out" mkdir "Image_Out"
if not exist "ExportedFiles" mkdir "ExportedFiles"

dotnet run --project "GTI-ModTools.WPF\GTI-ModTools.WPF.csproj"
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" (
    echo WPF launch failed with exit code %EXIT_CODE%.
    pause
)
exit /b %EXIT_CODE%
