@echo off
setlocal
chcp 65001 >nul
title ClaudeBarrier Official Telegram Bot // @ClaudeBarrierBot
cd /d "%~dp0"
echo ================================================================
echo       CLAUDEBARRIER TELEGRAM BOT // @ClaudeBarrierBot
echo ================================================================
echo.

where python >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Python is not installed or not in PATH.
    pause
    exit /b 1
)

echo [*] Starting bot polling service...
python bot.py
pause
