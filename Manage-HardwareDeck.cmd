@echo off
setlocal
set "action=%~1"
if "%action%"=="" set "action=Check"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Manage-Startup.ps1" -Action "%action%"
set "deckExit=%errorlevel%"
if "%~1"=="" pause
exit /b %deckExit%
