@echo off
rem Starts the CRUCIBLE relay (game host on :8080) and a Cloudflare tunnel in two visible windows.
rem Close either window to stop it. The tunnel window prints the public link (https://....trycloudflare.com).
cd /d "%~dp0"
start "CRUCIBLE relay (close to stop)" cmd /k node relay.js --port 8080 --dir ../Builds/WebGL
timeout /t 2 >nul
start "CRUCIBLE tunnel (close to stop)" cmd /k ""C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel --url http://localhost:8080 --no-autoupdate"
echo Two windows opened. The public link is printed in the tunnel window (look for trycloudflare.com).
