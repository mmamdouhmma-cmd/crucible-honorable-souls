@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.3 - Buwaka has a new SP+Heavy. Great Impale is a leaping overhead plunge that lands from further away than any of his other moves."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
