@echo off
rem Starts the CRUCIBLE online server: relay + Cloudflare tunnel, and publishes the address for the game.
rem Keep this window open while people play. Close it to stop the server.
title CRUCIBLE online server (close to stop)
cd /d "%~dp0"
if not exist node_modules\ws call npm install --silent
node online.js
pause
