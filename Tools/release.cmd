@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.9 - The camera now circles with the fighters, like UFC games: you stay on your side of the screen, with no more slow-motion swaps. Fighter cards show an overall and five ratings - vitality, defense, attack, speed and stamina - under rewritten play styles. Buwaka is bigger with a longer spear and lunge, and now has the longest reach. Genghis attacks about 15 percent faster and has a slightly wider parry window. Richard and Genghis walk about 10 percent faster. Online players need the same version."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
