@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title ClaudeBarrier Build Pipeline
echo ================================================================
echo             CLAUDEBARRIER BUILD PIPELINE v6.0.3
echo ================================================================
echo.

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [!] ERROR: csc.exe compiler not found at %CSC%
    exit /b 1
)

if not exist "dist" mkdir "dist"

echo [*] [1/4] Compiling Uninstall_ClaudeBarrier.exe (System Uninstaller Tool)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:app_icon.ico,app_icon.ico /resource:app_icon.png,app_icon.png /out:"dist\Uninstall_ClaudeBarrier.exe" "src\Uninstall_Project_XO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile Uninstall_ClaudeBarrier.exe
    exit /b %errorlevel%
)
copy /y "dist\Uninstall_ClaudeBarrier.exe" "dist\Uninstall_Project_XO.exe" >nul
echo [+] Uninstall_ClaudeBarrier.exe built successfully.

echo.
echo [*] [2/4] Compiling ClaudeBarrier.exe (Main WFP Shield with WARP Dark UI)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:app_icon.ico,app_icon.ico /resource:app_icon.png,app_icon.png /resource:tools\zapret_bundle.zip,zapret_bundle.zip /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Security.dll /out:"dist\ClaudeBarrier.exe" "src\ProjectXO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile ClaudeBarrier.exe
    exit /b %errorlevel%
)
copy /y "dist\ClaudeBarrier.exe" "dist\ProjectXO.exe" >nul 2>&1
echo [+] ClaudeBarrier.exe built successfully.

echo.
echo [*] [3/4] Compiling ClaudeBarrier-Setup-x64.exe (Standalone Installer)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:dist\ClaudeBarrier.exe,ClaudeBarrier.exe /resource:dist\ClaudeBarrier.exe,ProjectXO.exe /resource:dist\Uninstall_ClaudeBarrier.exe,Uninstall_ClaudeBarrier.exe /resource:dist\Uninstall_ClaudeBarrier.exe,Uninstall_Project_XO.exe /resource:app_icon.ico,app_icon.ico /resource:app_icon.png,app_icon.png /out:"dist\ClaudeBarrier-Setup-x64.exe" "src\Setup_Project_XO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile ClaudeBarrier-Setup-x64.exe
    exit /b %errorlevel%
)
copy /y "dist\ClaudeBarrier-Setup-x64.exe" "dist\Setup_Project_XO.exe" >nul
echo [+] ClaudeBarrier-Setup-x64.exe built successfully.

echo.
echo [*] [4/4] Packaging ClaudeBarrier-Windows-Portable-x64.zip...
python -c "import os, zipfile; base = os.getcwd(); zip_out = os.path.join(base, 'dist', 'ClaudeBarrier-Windows-Portable-x64.zip'); z = zipfile.ZipFile(zip_out, 'w', zipfile.ZIP_DEFLATED); z.write(os.path.join(base, 'dist', 'ClaudeBarrier.exe'), 'ClaudeBarrier.exe'); z.write(os.path.join(base, 'dist', 'Uninstall_ClaudeBarrier.exe'), 'Uninstall_ClaudeBarrier.exe'); z.write(os.path.join(base, 'app_icon.ico'), 'app_icon.ico'); z.write(os.path.join(base, 'README.md'), 'README.md'); z.close(); print('[+] Portable zip created successfully.')"

echo.
if exist "%USERPROFILE%\Desktop" (
    echo [*] Deploying release artifacts to Desktop...
    copy /y "dist\ClaudeBarrier-Setup-x64.exe" "%USERPROFILE%\Desktop\" >nul
    copy /y "dist\ClaudeBarrier.exe" "%USERPROFILE%\Desktop\" >nul 2>&1
    copy /y "dist\ClaudeBarrier-Windows-Portable-x64.zip" "%USERPROFILE%\Desktop\" >nul
    echo [+] Release packages copied to %USERPROFILE%\Desktop\
)

echo.
echo ================================================================
echo [+] ALL RELEASE ARTIFACTS (SETUP, PORTABLE ZIP) v6.0.3 READY!
echo ================================================================
