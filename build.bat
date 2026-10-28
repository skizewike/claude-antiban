@echo off
setlocal
chcp 65001 >nul
title Project XO Build // BOGDAN ZHUKOV
echo ================================================================
echo       PROJECT XO BUILD PIPELINE // BOGDAN ZHUKOV
echo ================================================================
echo.

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [!] ERROR: csc.exe compiler not found at %CSC%
    exit /b 1
)

if not exist "dist" mkdir "dist"

echo [*] [1/5] Compiling Uninstall_Project_XO.exe (System Uninstaller Tool)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:app_icon.ico,app_icon.ico /resource:app_icon.png,app_icon.png /out:"dist\Uninstall_Project_XO.exe" "src\Uninstall_Project_XO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile Uninstall_Project_XO.exe
    exit /b %errorlevel%
)
echo [+] Uninstall_Project_XO.exe built successfully.

echo.
echo [*] [2/5] Compiling ProjectXO.exe (Vector GUI Shield with Embedded Zapret, Tray Guard, DoH and Live Logs)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:app_icon.ico,app_icon.ico /resource:app_icon.png,app_icon.png /resource:tools\zapret_bundle.zip,zapret_bundle.zip /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:"dist\ProjectXO.exe" "src\ProjectXO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile ProjectXO.exe
    exit /b %errorlevel%
)
echo [+] ProjectXO.exe built successfully.

echo.
echo [*] [3/5] Compiling Setup_Project_XO.exe (Standalone Installer with Embedded Payload)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:dist\ProjectXO.exe,ProjectXO.exe /resource:dist\Uninstall_Project_XO.exe,Uninstall_Project_XO.exe /resource:app_icon.ico,app_icon.ico /out:"dist\Setup_Project_XO.exe" "src\Setup_Project_XO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile Setup_Project_XO.exe
    exit /b %errorlevel%
)
echo [+] Setup_Project_XO.exe built successfully.

echo.
echo [*] [4/5] Building ProjectXO.apk (Native Android App with Floating Guard and DoH)...
python build_apk.py
if %errorlevel% neq 0 (
    echo [!] Failed to build Android APK
    exit /b %errorlevel%
)
echo [+] ProjectXO.apk built successfully.

echo.
echo [*] [5/5] Packaging Project_XO_Android.zip...
copy /y "dist\ProjectXO.apk" "android\" >nul
copy /y "app_icon.png" "android\" >nul
python -c "import shutil, os, zipfile; base = r'C:\Users\bbara\.gemini\antigravity\scratch\project_xo'; zip_out = os.path.join(base, 'dist', 'Project_XO_Android.zip'); z = zipfile.ZipFile(zip_out, 'w', zipfile.ZIP_DEFLATED); [z.write(os.path.join(base, 'android', f), f) for f in ['ProjectXO.apk', 'app_icon.png', 'README_ANDROID.md']]; z.close(); print('[+] Android Zip packaged successfully.')"
if %errorlevel% neq 0 (
    echo [!] Failed to package Android zip
    exit /b %errorlevel%
)

echo.
echo ================================================================
echo [+] ALL PROJECT XO ARTIFACTS (WINDOWS + ANDROID) READY IN dist\
echo ================================================================
