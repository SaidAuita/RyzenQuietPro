@echo off
setlocal
set "HTTPS_PROXY="
set "HTTP_PROXY="
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo ==================================================================
echo   Building RyzenQuiet PRO for macOS (v3.0)
echo   Architectures: osx-x64 (Intel/VM) + osx-arm64 (Apple Silicon)
echo ==================================================================
echo.

echo [1/3] Compiling osx-x64 (Intel 64-bit / VM / ThinkPad x230)...
dotnet publish RyzenQuiet.Mac\RyzenQuiet.Mac.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\mac
if %ERRORLEVEL% neq 0 (
    echo [ERROR] osx-x64 build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Compiling osx-arm64 (Apple Silicon M1/M2/M3/M4)...
dotnet publish RyzenQuiet.Mac\RyzenQuiet.Mac.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\mac_arm64
if %ERRORLEVEL% neq 0 (
    echo [ERROR] osx-arm64 build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Packaging macOS Application Bundle, Installers and Zip Archive...
python package_mac.py
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Packaging failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==================================================================
echo [OK] macOS distribution successfully generated!
echo Location: dist\RyzenQuietPro-v3.0-macOS.zip
echo ==================================================================
pause

