@echo off
rem Shows whether the relay and the tunnel are running on this PC, and the current public link.
echo --- relay (node relay.js on port 8080):
netstat -an | findstr /c:":8080 " | findstr LISTENING >nul && (echo   RUNNING, http://localhost:8080/) || (echo   NOT running)
echo --- tunnel (cloudflared):
tasklist /fi "imagename eq cloudflared.exe" | findstr /i cloudflared >nul && (echo   RUNNING) || (echo   NOT running)
if exist "%~dp0cf.log" (
  for /f "tokens=*" %%u in ('findstr /r "https://[a-z0-9-]*\.trycloudflare\.com" "%~dp0cf.log"') do set "LINE=%%u"
  echo --- last public link seen in cf.log (only valid while the tunnel is RUNNING):
  echo   %LINE%
)
pause
