@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.1 - Free-form combos and fairer hits on slopes. Combos: no more fixed strings - any attack, a special included, cancels into the next one as long as the two swings flow (never the same swing twice, except two overheads or two thrusts in a row; a spin follows an overhead only on the same diagonal), up to three hits. Combo-only finishers stay. The Move List strings page is gone. Hits: overheads, low cuts and uppercuts now land whenever the target is in reach, even on uneven ground; mid and high slashes still have to physically connect. Several strikes now dodge and react from the side their animation actually swings from."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
