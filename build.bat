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

echo [*] [1/4] Compiling ProjectXO.exe (Vector GUI Shield for Windows with Luxury Icon)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:app_icon.ico,app_icon.ico /out:"dist\ProjectXO.exe" "src\ProjectXO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile ProjectXO.exe
    exit /b %errorlevel%
)
echo [+] ProjectXO.exe built successfully.

echo.
echo [*] [2/4] Compiling Setup_Project_XO.exe (Standalone Installer with Embedded Payload and Icon)...
"%CSC%" /target:winexe /optimize+ /win32icon:"app_icon.ico" /resource:dist\ProjectXO.exe,ProjectXO.exe /resource:app_icon.ico,app_icon.ico /out:"dist\Setup_Project_XO.exe" "src\Setup_Project_XO.cs"
if %errorlevel% neq 0 (
    echo [!] Failed to compile Setup_Project_XO.exe
    exit /b %errorlevel%
)
echo [+] Setup_Project_XO.exe built successfully.

echo.
echo [*] [3/4] Building ProjectXO.apk (Native Android Application with Luxury Icon)...
python build_apk.py
if %errorlevel% neq 0 (
    echo [!] Failed to build Android APK
    exit /b %errorlevel%
)
echo [+] ProjectXO.apk built successfully.

echo.
echo [*] [4/4] Packaging Project_XO_Android.zip...
copy /y "dist\ProjectXO.apk" "android\" >nul
copy /y "app_icon.png" "android\" >nul
powershell -NoProfile -Command "Compress-Archive -Path 'android\*' -DestinationPath 'dist\Project_XO_Android.zip' -Force"
if %errorlevel% neq 0 (
    echo [!] Failed to package Android zip
    exit /b %errorlevel%
)
echo [+] Project_XO_Android.zip packaged successfully.

echo.
echo ================================================================
echo [+] ALL PROJECT XO ARTIFACTS (WINDOWS + ANDROID) READY IN dist\
echo ================================================================