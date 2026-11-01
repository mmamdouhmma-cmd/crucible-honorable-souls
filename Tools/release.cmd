@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.8 - New keyboard controls: every dash direction with the arrows or Space, an alternative one-player layout, a two-player keyboard layout with a review screen before each game. Holding the forward dash runs. The camera now swings round in slow motion when fighters swap sides, the dash keeps going, and the fight resets the distance. Walls and objects the camera passes through turn see-through. The Village Plaza fight zone is about 2.5 times bigger. Fighter styles on character select and the Move List, a new How to Fight guide, and the Sensei hand shake is fixed. Online players need the same version."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
