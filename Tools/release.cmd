@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.5 - Game Settings before every fight: duel time up to 15 minutes, plus damage, health and stamina percentages. A fighter who dies mid-swing can no longer land the hit. On Android: touch controls for character and arena select, a new fight layout with eight dash buttons, adjustable button sizes, bigger scrolling menus, and updates from inside the game. Install this Android version by hand once; later ones install from the main menu. Online players need the same version."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
