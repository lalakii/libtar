@echo off
cd /d "%~dp0"
RD /S /Q obj >NUL 2>&1
RD /S /Q bin >NUL 2>&1
RD /S /Q nupkgs >NUL 2>&1
dotnet pack --output nupkgs