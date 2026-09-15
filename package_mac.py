#!/usr/bin/env python3
"""
package_mac.py — Standalone macOS Distribution Packager for RyzenQuiet PRO
Builds native macOS Application Bundle (.app), Install.command, Run.command,
and creates a portable zip archive with UNIX 0755 execute permissions preserved.
Modeled after C:/_CODE/Utilites/DropFile.
"""

import os
import shutil
import stat
import sys
import zipfile
from pathlib import Path
from PIL import Image

VERSION = "3.0"
APP_NAME = "RyzenQuiet PRO"
BUNDLE_NAME = f"{APP_NAME}.app"

ROOT_DIR = Path(__file__).resolve().parent
BUILD_DIR = ROOT_DIR / "build"
DIST_DIR = ROOT_DIR / "dist"
MAC_STAGING = BUILD_DIR / "mac_staging"
APP_DIR = MAC_STAGING / BUNDLE_NAME


def generate_icns(output_path: Path):
    """Generates a native macOS AppIcon.icns from resources icon."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    icon_src = ROOT_DIR / "Resources" / "quiet.ico"
    if not icon_src.exists():
        icon_src = ROOT_DIR / "Resources" / "normal.ico"

    img = Image.open(icon_src)
    if img.mode != "RGBA":
        img = img.convert("RGBA")

    img.save(str(output_path), format="ICNS")
    print(f"[package_mac] Generated macOS icon: {output_path} ({output_path.stat().st_size} bytes)")


def create_info_plist() -> str:
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>{APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>{APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.saidauita.ryzenquietpro</string>
    <key>CFBundleVersion</key>
    <string>{VERSION}.0</string>
    <key>CFBundleShortVersionString</key>
    <string>{VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>RyzenQuietLauncher</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
"""


def create_launcher_script() -> str:
    return """#!/bin/bash
# RyzenQuiet PRO — macOS Application Launcher
DIR="$(cd "$(dirname "$0")" && pwd)"
xattr -cr "$DIR" 2>/dev/null || true

ARCH="$(uname -m)"
EXEC="$DIR/RyzenQuiet.Mac"

if [ "$ARCH" = "arm64" ] && [ -f "$DIR/RyzenQuiet.Mac-arm64" ]; then
    EXEC="$DIR/RyzenQuiet.Mac-arm64"
elif [ -f "$DIR/RyzenQuiet.Mac-x64" ]; then
    EXEC="$DIR/RyzenQuiet.Mac-x64"
fi

chmod +x "$EXEC" 2>/dev/null || true

# Activate Terminal and run Live HUD
osascript -e "tell application \\"Terminal\\"
    activate
    do script \\"\\\\\\"$EXEC\\\\\\"\\"
end tell"
"""


def create_install_command() -> str:
    return """#!/bin/bash
# ==============================================================================
# RyzenQuiet PRO — macOS Installer (10.15 Catalina ... Tahoe / Sequoia)
# Launch by double-clicking in Finder or running in Terminal.
# ==============================================================================

set -e

cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"

# 1. Strip Gatekeeper quarantine on the installer directory
xattr -cr "$SCRIPT_DIR" 2>/dev/null || true
chmod +x "$SCRIPT_DIR"/*.command 2>/dev/null || true
chmod +x "$SCRIPT_DIR"/RyzenQuiet.Mac* 2>/dev/null || true
if [ -d "$SCRIPT_DIR/RyzenQuiet PRO.app" ]; then
    chmod -R 755 "$SCRIPT_DIR/RyzenQuiet PRO.app" 2>/dev/null || true
    xattr -cr "$SCRIPT_DIR/RyzenQuiet PRO.app" 2>/dev/null || true
fi

echo "=================================================================="
echo "          ⚡ RyzenQuiet PRO (v3.0) — Installer for macOS         "
echo "=================================================================="
echo ""

ARCH="$(uname -m)"
echo "Detected hardware architecture: $ARCH"

# Select architecture binary
if [ "$ARCH" = "arm64" ] && [ -f "$SCRIPT_DIR/RyzenQuiet.Mac-arm64" ]; then
    echo "Using optimized native Apple Silicon (arm64) binary."
    cp -f "$SCRIPT_DIR/RyzenQuiet.Mac-arm64" "$SCRIPT_DIR/RyzenQuiet.Mac" 2>/dev/null || true
    if [ -d "$SCRIPT_DIR/RyzenQuiet PRO.app/Contents/MacOS" ]; then
        cp -f "$SCRIPT_DIR/RyzenQuiet.Mac-arm64" "$SCRIPT_DIR/RyzenQuiet PRO.app/Contents/MacOS/RyzenQuiet.Mac" 2>/dev/null || true
    fi
elif [ -f "$SCRIPT_DIR/RyzenQuiet.Mac-x64" ]; then
    echo "Using native Intel 64-bit (x86_64) binary."
    cp -f "$SCRIPT_DIR/RyzenQuiet.Mac-x64" "$SCRIPT_DIR/RyzenQuiet.Mac" 2>/dev/null || true
    if [ -d "$SCRIPT_DIR/RyzenQuiet PRO.app/Contents/MacOS" ]; then
        cp -f "$SCRIPT_DIR/RyzenQuiet.Mac-x64" "$SCRIPT_DIR/RyzenQuiet PRO.app/Contents/MacOS/RyzenQuiet.Mac" 2>/dev/null || true
    fi
fi

echo ""
echo "Select installation destination:"
echo "  1) /Applications (System Applications — Recommended)"
echo "  2) ~/Applications (User Applications: $HOME/Applications)"
echo "  3) Keep portable in current directory (Do not copy)"
echo ""
read -p "Install location [1]: " DEST_CHOICE
DEST_CHOICE="${DEST_CHOICE:-1}"

TARGET_DIR=""
if [ "$DEST_CHOICE" = "1" ]; then
    TARGET_DIR="/Applications"
elif [ "$DEST_CHOICE" = "2" ]; then
    TARGET_DIR="$HOME/Applications"
else
    echo "Keeping portable version in current folder. You can launch anytime via ./Run.command"
fi

if [ -n "$TARGET_DIR" ]; then
    echo ""
    echo "Installing RyzenQuiet PRO.app to $TARGET_DIR/..."
    mkdir -p "$TARGET_DIR" 2>/dev/null || true
    
    # Remove older version if present
    rm -rf "$TARGET_DIR/RyzenQuiet PRO.app" 2>/dev/null || sudo rm -rf "$TARGET_DIR/RyzenQuiet PRO.app" 2>/dev/null || true
    
    if cp -R "$SCRIPT_DIR/RyzenQuiet PRO.app" "$TARGET_DIR/" 2>/dev/null; then
        echo "   -> Copied successfully."
    else
        echo "   -> Administrative privileges required for $TARGET_DIR. Requesting sudo..."
        sudo cp -R "$SCRIPT_DIR/RyzenQuiet PRO.app" "$TARGET_DIR/"
    fi
    
    TARGET_APP="$TARGET_DIR/RyzenQuiet PRO.app"
    chmod -R 755 "$TARGET_APP" 2>/dev/null || sudo chmod -R 755 "$TARGET_APP" 2>/dev/null || true
    xattr -cr "$TARGET_APP" 2>/dev/null || sudo xattr -cr "$TARGET_APP" 2>/dev/null || true
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$TARGET_APP" 2>/dev/null || true
    
    echo ""
    echo "=================================================================="
    echo "🎉 Successfully installed to: $TARGET_APP"
    echo "=================================================================="
    echo ""
    read -p "Launch RyzenQuiet PRO right now? [Y/n]: " -n 1 -r
    echo ""
    if [[ $REPLY =~ ^[Yy]$ ]] || [[ -z $REPLY ]]; then
        echo "Launching RyzenQuiet PRO..."
        open "$TARGET_APP"
    fi
fi
"""


def create_run_command() -> str:
    return """#!/bin/bash
# ==============================================================================
# RyzenQuiet PRO — Direct Portable Launcher
# ==============================================================================

cd "$(dirname "$0")"

# Remove quarantine flags and ensure execute permissions
xattr -cr . 2>/dev/null || true
chmod +x ./*.command ./RyzenQuiet.Mac* 2>/dev/null || true

ARCH="$(uname -m)"
EXEC="./RyzenQuiet.Mac"

if [ "$ARCH" = "arm64" ] && [ -f "./RyzenQuiet.Mac-arm64" ]; then
    EXEC="./RyzenQuiet.Mac-arm64"
elif [ -f "./RyzenQuiet.Mac-x64" ]; then
    EXEC="./RyzenQuiet.Mac-x64"
fi

chmod +x "$EXEC" 2>/dev/null || true

clear
exec "$EXEC" "$@"
"""


def create_web_run_command() -> str:
    return """#!/bin/bash
# ==============================================================================
# RyzenQuiet PRO — Launch Web HUD (Safari / http://localhost:5050)
# ==============================================================================

cd "$(dirname "$0")"

xattr -cr . 2>/dev/null || true
chmod +x ./*.command ./RyzenQuiet.Mac* 2>/dev/null || true

ARCH="$(uname -m)"
EXEC="./RyzenQuiet.Mac"

if [ "$ARCH" = "arm64" ] && [ -f "./RyzenQuiet.Mac-arm64" ]; then
    EXEC="./RyzenQuiet.Mac-arm64"
elif [ -f "./RyzenQuiet.Mac-x64" ]; then
    EXEC="./RyzenQuiet.Mac-x64"
fi

chmod +x "$EXEC" 2>/dev/null || true

clear
echo "=================================================================="
echo " Starting RyzenQuiet PRO with Safari Web HUD (http://localhost:5050)"
echo "=================================================================="
echo ""

exec "$EXEC" --web "$@"
"""


def create_uninstall_command() -> str:
    return """#!/bin/bash
# ==============================================================================
# RyzenQuiet PRO — Uninstaller for macOS
# ==============================================================================

echo "=================================================================="
echo "          ⚡ RyzenQuiet PRO — Uninstaller for macOS              "
echo "=================================================================="
echo ""

REMOVED=0

if [ -d "/Applications/RyzenQuiet PRO.app" ]; then
    echo "Removing /Applications/RyzenQuiet PRO.app..."
    rm -rf "/Applications/RyzenQuiet PRO.app" 2>/dev/null || sudo rm -rf "/Applications/RyzenQuiet PRO.app"
    REMOVED=1
fi

if [ -d "$HOME/Applications/RyzenQuiet PRO.app" ]; then
    echo "Removing $HOME/Applications/RyzenQuiet PRO.app..."
    rm -rf "$HOME/Applications/RyzenQuiet PRO.app"
    REMOVED=1
fi

if [ $REMOVED -eq 1 ]; then
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "/Applications/RyzenQuiet PRO.app" 2>/dev/null || true
    echo ""
    echo "🎉 RyzenQuiet PRO has been cleanly uninstalled."
else
    echo "No installed RyzenQuiet PRO.app was found in Applications."
fi

echo ""
read -n 1 -s -r -p "Press any key to exit..."
echo ""
"""


def create_readme() -> str:
    return """==================================================================
            ⚡ RyzenQuiet PRO (v3.0 macOS Edition)
   ThinkPad x230 Hackintosh, AMD/Intel Hackintosh & Virtual Machines
==================================================================

1. УСТАНОВКА В СИСТЕМУ (Рекомендуется):
   - Дважды кликните по файлу "Install.command".
   - Выберите [1] (/Applications) или [2] (~/Applications).
   - Скрипт автоматически снимет блокировки Gatekeeper, настроит
     права доступа и зарегистрирует "RyzenQuiet PRO.app" в Программах.

2. БЫСТРЫЙ ПОРТАТИВНЫЙ ЗАПУСК БЕЗ УСТАНОВКИ:
   - "Run.command"        -> запускает интерактивный терминальный HUD.
   - "Run-WebHUD.command" -> запускает HUD и открывает Safari на http://localhost:5050

3. ЕСЛИ MACOS БЛОКИРУЕТ ЗАПУСК ("Не удается открыть"):
   - Нажмите правой кнопкой мыши (или Control + клик) по "Install.command"
     или "Run.command" и выберите "Открыть" -> затем подтвердите "Открыть".
   - Либо откройте Terminal в этой папке и выполните:
     chmod +x *.command && xattr -cr .

4. ГОРЯЧИЕ КЛАВИШИ В ТЕРМИНАЛЕ:
   [S] или [Q] - Включить Тихий режим (Silent Mode 99%)
   [B]         - Включить Режим Boost (Turbo Mode 100%)
   [F]         - Переключить вид кулеров (График <-> Иконки)
   [D]         - Включить Демо-режим кулеров (для виртуальных машин)
   [X] / [Esc] - Выход

5. ВЕБ-ВИДЖЕТ SAFARI:
   При работе приложения в фоне активен локальный веб-сервер:
   http://localhost:5050
==================================================================
"""


def package():
    print("==================================================================")
    print(f"       Packaging {APP_NAME} v{VERSION} for macOS")
    print("==================================================================")

    DIST_DIR.mkdir(parents=True, exist_ok=True)
    if MAC_STAGING.exists():
        shutil.rmtree(MAC_STAGING, ignore_errors=True)
    MAC_STAGING.mkdir(parents=True, exist_ok=True)

    # 1. Create .app bundle structure
    contents_dir = APP_DIR / "Contents"
    macos_dir = contents_dir / "MacOS"
    resources_dir = contents_dir / "Resources"
    macos_dir.mkdir(parents=True, exist_ok=True)
    resources_dir.mkdir(parents=True, exist_ok=True)

    # Info.plist
    (contents_dir / "Info.plist").write_text(create_info_plist(), encoding="utf-8")

    # AppIcon.icns
    icns_path = resources_dir / "AppIcon.icns"
    generate_icns(icns_path)

    # Launcher script
    (macos_dir / "RyzenQuietLauncher").write_text(create_launcher_script(), encoding="utf-8", newline="\n")

    # 2. Copy binaries
    bin_x64 = BUILD_DIR / "mac" / "RyzenQuiet.Mac"
    bin_arm64 = BUILD_DIR / "mac_arm64" / "RyzenQuiet.Mac"

    has_x64 = bin_x64.exists()
    has_arm64 = bin_arm64.exists()

    if not has_x64 and not has_arm64:
        print("[ERROR] Neither osx-x64 nor osx-arm64 binaries found in build/! Run dotnet publish first.")
        sys.exit(1)

    if has_x64:
        print(f"[package_mac] Including Intel x64 binary ({bin_x64.stat().st_size} bytes)")
        shutil.copy2(bin_x64, MAC_STAGING / "RyzenQuiet.Mac-x64")
        shutil.copy2(bin_x64, macos_dir / "RyzenQuiet.Mac-x64")
        shutil.copy2(bin_x64, MAC_STAGING / "RyzenQuiet.Mac")
        shutil.copy2(bin_x64, macos_dir / "RyzenQuiet.Mac")

    if has_arm64:
        print(f"[package_mac] Including Apple Silicon arm64 binary ({bin_arm64.stat().st_size} bytes)")
        shutil.copy2(bin_arm64, MAC_STAGING / "RyzenQuiet.Mac-arm64")
        shutil.copy2(bin_arm64, macos_dir / "RyzenQuiet.Mac-arm64")

    # 3. Create command scripts in root staging
    (MAC_STAGING / "Install.command").write_text(create_install_command(), encoding="utf-8", newline="\n")
    (MAC_STAGING / "Run.command").write_text(create_run_command(), encoding="utf-8", newline="\n")
    (MAC_STAGING / "Run-WebHUD.command").write_text(create_web_run_command(), encoding="utf-8", newline="\n")
    (MAC_STAGING / "Uninstall.command").write_text(create_uninstall_command(), encoding="utf-8", newline="\n")
    (MAC_STAGING / "README_MAC.txt").write_text(create_readme(), encoding="utf-8", newline="\n")

    # Also include the icns in the root folder
    shutil.copy2(icns_path, MAC_STAGING / "AppIcon.icns")

    # 4. Pack into zip archive preserving UNIX 0755 permissions
    zip_name = f"RyzenQuietPro-v{VERSION}-macOS.zip"
    zip_path = DIST_DIR / zip_name
    print(f"[package_mac] Creating zip archive: {zip_path}")

    if zip_path.exists():
        zip_path.unlink()

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        for root, dirs, files in os.walk(MAC_STAGING):
            for d in dirs:
                full_dir = Path(root) / d
                rel_dir = full_dir.relative_to(MAC_STAGING).as_posix() + "/"
                zinfo = zipfile.ZipInfo(rel_dir)
                zinfo.create_system = 3
                zinfo.external_attr = 0o755 << 16 | 0o040000 << 16
                zf.writestr(zinfo, "")

            for f in files:
                full_path = Path(root) / f
                rel_path = full_path.relative_to(MAC_STAGING).as_posix()
                zinfo = zipfile.ZipInfo.from_file(full_path, rel_path)
                zinfo.create_system = 3

                # Executable permissions for commands, shell scripts, and binaries
                if f.endswith((".command", ".sh")) or "RyzenQuiet" in f or rel_path.endswith("RyzenQuietLauncher"):
                    zinfo.external_attr = 0o755 << 16
                else:
                    zinfo.external_attr = 0o644 << 16

                with open(full_path, "rb") as fp:
                    zf.writestr(zinfo, fp.read())

    alias_zip = DIST_DIR / "RyzenQuietPro-macOS.zip"
    shutil.copy2(zip_path, alias_zip)

    print("==================================================================")
    print("[OK] Build Complete! Output archives:")
    print(f"   - {zip_path} ({zip_path.stat().st_size} bytes)")
    print(f"   - {alias_zip}")
    print(f"   - Portable folder: {MAC_STAGING}")
    print("==================================================================")


if __name__ == "__main__":
    package()
