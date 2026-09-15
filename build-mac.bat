@echo off
setlocal
set "HTTPS_PROXY="
set "HTTP_PROXY="
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo ==========================================
echo Building RyzenQuiet PRO for macOS (osx-x64)
echo Target OS: macOS 10.15 Catalina+ (Intel x64)
echo ==========================================

dotnet publish RyzenQuiet.Mac\RyzenQuiet.Mac.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o build\mac

if exist "build\mac\RyzenQuiet.Mac" (
    echo.
    echo ------------------------------------------
    echo macOS standalone binary successfully built!
    echo Binary: build\mac\RyzenQuiet.Mac
    echo ------------------------------------------
) else (
    echo.
    echo Build failed!
)
pause
