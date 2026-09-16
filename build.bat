@echo off
setlocal
set VERSION=v4.0

rem Clear proxy environment variables to allow direct access
set "HTTPS_PROXY="
set "HTTP_PROXY="
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
)

echo ==========================================
echo Building RyzenQuiet PRO %VERSION%
echo ==========================================

rem Close running instance if any to allow replacing binary
taskkill /F /IM RyzenQuietPro.exe >nul 2>&1
taskkill /F /IM RyzenQuietPro-%VERSION%.exe >nul 2>&1
taskkill /F /IM RyzenQuietPro-%VERSION%-Lite.exe >nul 2>&1
taskkill /F /IM RyzenQuietPro-v3.0.exe >nul 2>&1
taskkill /F /IM RyzenQuietPro-v3.0-Lite.exe >nul 2>&1
taskkill /F /IM RyzenQuiet.FanService.exe >nul 2>&1
dotnet build-server shutdown >nul 2>&1

dotnet clean RyzenQuietPro.csproj -c Release
if exist "obj\Release" rd /s /q "obj\Release"
if exist "build\temp_standalone" rd /s /q "build\temp_standalone"
if exist "build\temp_lite" rd /s /q "build\temp_lite"
if exist "build\lite" rd /s /q "build\lite"

rem Standalone edition (Self-contained, ~70 MB)
dotnet publish RyzenQuietPro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\temp_standalone

rem Lite edition (Framework-dependent, ~1.2 MB)
dotnet publish RyzenQuietPro.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\temp_lite

rem Addon: RyzenQuiet Fan Service (Isolated Plugin)
dotnet publish RyzenQuiet.FanService\RyzenQuiet.FanService.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\plugins\FanService

if exist "build\temp_standalone\RyzenQuietPro.exe" (
    move /Y "build\temp_standalone\RyzenQuietPro.exe" "build\RyzenQuietPro-%VERSION%.exe" >nul
    rd /s /q "build\temp_standalone" >nul 2>&1

    if exist "build\temp_lite\RyzenQuietPro.exe" (
        move /Y "build\temp_lite\RyzenQuietPro.exe" "build\RyzenQuietPro-%VERSION%-Lite.exe" >nul
        rd /s /q "build\temp_lite" >nul 2>&1
        if exist "%USERPROFILE%\Documents\RyzenQuietPro-%VERSION%-Lite.exe" (
            copy /Y "build\RyzenQuietPro-%VERSION%-Lite.exe" "%USERPROFILE%\Documents\RyzenQuietPro-%VERSION%-Lite.exe" >nul
        )
    )

    rem Clean up any leftover artifacts
    if exist "build\RyzenQuietPro.exe" del /f /q "build\RyzenQuietPro.exe" >nul 2>&1
    if exist "build\lite" rd /s /q "build\lite" >nul 2>&1

    echo.
    echo ------------------------------------------
    echo Build completed successfully!
    echo Output directory: build\
    echo Executables:
    echo   build\RyzenQuietPro-%VERSION%.exe [Standalone with embedded runtime, ~70 MB]
    echo   build\RyzenQuietPro-%VERSION%-Lite.exe [Ultralight .NET 8 dependent, ~1.2 MB]
    echo   build\plugins\FanService\RyzenQuiet.FanService.exe [Fan Monitor Addon]
    echo ------------------------------------------
) else (
    echo.
    echo Build failed!
)

echo.
pause
