@echo off
setlocal
chcp 65001 >nul
title Project XO - GitHub Publisher // BOGDAN ZHUKOV
echo ================================================================
echo       PROJECT XO GITHUB PUBLISHER // BOGDAN ZHUKOV
echo ================================================================
echo.

where.exe git >nul 2>&1
if %errorlevel% neq 0 (
    if exist "%LOCALAPPDATA%\GitHubDesktop\app-3.5.8\resources\app\git\cmd\git.exe" (
        set "PATH=%LOCALAPPDATA%\GitHubDesktop\app-3.5.8\resources\app\git\cmd;%PATH%"
    ) else if exist "%LOCALAPPDATA%\GitHubDesktop\app-3.5.5\resources\app\git\cmd\git.exe" (
        set "PATH=%LOCALAPPDATA%\GitHubDesktop\app-3.5.5\resources\app\git\cmd;%PATH%"
    ) else if exist "%ProgramFiles%\Git\cmd\git.exe" (
        set "PATH=%ProgramFiles%\Git\cmd;%PATH%"
    )
)

where.exe git >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Git CLI is not installed or not found in system PATH.
    echo.
    echo HOW TO PUBLISH TO GITHUB:
    echo -------------------------------------------------------------
    echo OPTION 1 (Recommended - No installation required):
    echo   1. Open https://github.com/new in your browser.
    echo   2. Create a new repository named: project-xo
    echo   3. Click 'uploading an existing file'.
    echo   4. Drag and drop all files from this folder directly into GitHub!
    echo.
    echo OPTION 2 (Using Git):
    echo   1. Install Git: https://git-scm.com/download/win
    echo   2. Run this script again.
    echo -------------------------------------------------------------
    echo.
    pause
    exit /b
)

echo [*] Initializing Git repository...
if not exist ".git" (
    git init
    git config user.name "Bogdan Zhukov"
    git config user.email "bogdan@project-xo.local"
)

echo [*] Staging all files...
git add .
git commit -m "Release: Project XO v4.1 Luxury BZ/XO Edition // BOGDAN ZHUKOV"
git branch -M main

echo.
echo Enter your GitHub repository URL (e.g. https://github.com/username/project-xo.git):
set /p REPO_URL="Repo URL: "

if "%REPO_URL%"=="" (
    echo [!] No URL entered. Repository initialized locally only.
    pause
    exit /b
)

git remote remove origin >nul 2>&1
git remote add origin %REPO_URL%
echo.
echo [*] Pushing to GitHub...
git push -u origin main

if %errorlevel% equ 0 (
    echo.
    echo ================================================================
    echo [+] REPOSITORY SUCCESSFULLY PUBLISHED TO GITHUB!
    echo ================================================================
) else (
    echo.
    echo [!] Push failed. Please verify credentials (gh auth login / personal access token).
)

pause
