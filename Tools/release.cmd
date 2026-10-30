@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.4 - Combo rules. The same straight strike (thrust or overhead) can be thrown at most twice in a row. Once a combo swing is dodged, the rest of that combo stops turning, stepping and aiming toward the opponent: follow-ups go straight ahead, so a spin can still catch a sidestep but a straight strike cannot. Miyamoto light is a cut now and cannot be doubled."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
