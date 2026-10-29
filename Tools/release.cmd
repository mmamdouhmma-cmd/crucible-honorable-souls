@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
rem NOTES below are this version's notes; Enter at the prompt keeps them, typing replaces them.
rem Keep them free of the characters cmd treats specially: ampersand, pipe, angle brackets, quotes.
cd /d "%~dp0.."
set "NOTES=v0.4.0 - Sharper visuals and a new fighter to test. Visuals: sharper shadows (4096 over 45 m, 4 cascades), 4x MSAA anti-aliasing, stronger ambient occlusion, a softer ACES grade on every arena (no chromatic aberration, lighter film grain), live reflections captured per arena, and 4K fighter and weapon textures. New fighter, in testing: the Sensei, an old master with a wooden bokken - one-handed strikes, a two-handed overhead, low health. He is a planned DLC character and may change."
echo.
echo Notes for this release:
echo %NOTES%
echo.
set /p NOTES=Press Enter to keep these notes, or type new ones:
node Tools\release.js "%NOTES%"
pause
