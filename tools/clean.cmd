@echo off
REM SelfishNet v2 - remove regenerable build artifacts.
REM Usage:
REM   tools\clean.cmd                     (asks for confirmation)
REM   tools\clean.cmd -Force              (no prompt)
REM   tools\clean.cmd -IncludeExes        (also delete the root portable exes)
REM
REM No Administrator rights are required: everything is inside this project folder.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean.ps1" %*
