@echo off
set SCRIPT_NAME=%~n0
set EXEDIR=%~dp0
cd /d %EXEDIR%
powershell -ExecutionPolicy Bypass -File .\%SCRIPT_NAME%.ps1
powershell -ExecutionPolicy Bypass -File .\%SCRIPT_NAME%.ps1 -User 01PLM02@plm-lab.local
pause
