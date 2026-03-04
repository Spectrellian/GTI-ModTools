@echo off
setlocal

REM Ensure execution from the repository root (location of this .bat file).
cd /d "%~dp0"

if not "%~1"=="" (
    call :RunTool %*
    exit /b %ERRORLEVEL%
)

call :EnsureFolders

:MENU
echo.
echo GTI-ModTools CLI Menu
echo 1^) Just convert Image_In into Image_Out ^(auto mode^)
echo 2^) Guided workflow ^(IMG -^> PNG, edit, PNG -^> IMG+BSJI^)
echo 3^) Exit
set /p MENU_CHOICE=Select an option [1-3]: 

if "%MENU_CHOICE%"=="1" (
    call :RunTool
    exit /b %ERRORLEVEL%
)

if "%MENU_CHOICE%"=="2" (
    call :GuidedFlow
    exit /b %ERRORLEVEL%
)

if "%MENU_CHOICE%"=="3" (
    exit /b 0
)

echo Invalid selection.
goto :MENU

:GuidedFlow
echo.
echo [Step 1/4] Copy your original .img files into Image_In.
echo Keep folder structure.
pause

echo.
echo [Step 2/4] Converting all .img in Image_In to .png in Image_Out...
call :RunTool --to-png "Image_In" "Image_Out"
if not "%ERRORLEVEL%"=="0" (
    echo IMG to PNG conversion failed.
    exit /b %ERRORLEVEL%
)

echo.
echo [Step 3/4] Pick any PNGs you want to edit.
echo Edit them and place the edited PNG files into Image_In.
echo Keep the format suffix in the file name (example: icon_start_0x02.png).
echo You can place as many edited PNGs as you want.
pause

echo.
echo [Step 4/4] Converting all PNGs in Image_In back to IMG and adjusted BSJI in Image_Out...
call :RunTool --to-img "Image_In" "Image_Out"
if not "%ERRORLEVEL%"=="0" (
    echo PNG to IMG conversion failed.
    exit /b %ERRORLEVEL%
)

echo.
echo Guided flow finished.
exit /b 0

:RunTool
cmd.exe /C dotnet run --project "GTI-ModTools.Images.CLI\GTI-ModTools.Images.CLI.csproj" -- %*
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" (
    echo Conversion failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%

:EnsureFolders
if not exist "Base" mkdir "Base"
if not exist "Image_In" mkdir "Image_In"
if not exist "Image_Out" mkdir "Image_Out"
exit /b 0
