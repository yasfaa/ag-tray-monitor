@echo off
start "" powershell.exe -Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0src\TrayApp.ps1"
exit
