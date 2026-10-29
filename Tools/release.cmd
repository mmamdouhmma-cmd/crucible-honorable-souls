@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.2 - Buwaka reworked, and a window mode. Buwaka: his Heavy (Iklwa Cleave) is now an overhead cut, his SP+Medium (Leopard Wheel) a spinning cut and his SP+Heavy (Great Whirlwind) a spinning overhead. Settings: a new Display row switches between fullscreen and windowed."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
