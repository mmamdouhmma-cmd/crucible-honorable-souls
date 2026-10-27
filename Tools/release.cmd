@echo off
rem Zips Builds\CrucibleLite-Win64 and publishes it as a GitHub release (tag = Player Settings version).
rem Bump the version and build (Crucible > Build > Windows app) before running this.
cd /d "%~dp0.."
set /p NOTES=What changed in this version? (Enter to skip): 
node Tools\release.js "%NOTES%"
pause
