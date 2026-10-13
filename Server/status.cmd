@echo off
rem Shows whether the relay and the tunnel are running on this PC, and the current public link.
setlocal
echo --- relay (node relay.js on port 8080):
netstat -an | findstr /c:":8080 " | findstr LISTENING >nul && (echo   RUNNING, http://localhost:8080/) || (echo   NOT running)
echo --- tunnel (cloudflared):
tasklist /fi "imagename eq cloudflared.exe" | findstr /i cloudflared >nul && (echo   RUNNING) || (echo   NOT running)
echo --- public link, valid only while the tunnel is RUNNING:
powershell -NoProfile -Command "Get-Content -ErrorAction SilentlyContinue '%~dp0cf.log' | Select-String -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -Last 1 | ForEach-Object { '  ' + $_.Matches[0].Value }"
pause
