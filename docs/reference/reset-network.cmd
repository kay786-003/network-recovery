@echo off
setlocal
chcp 65001 >nul
@REM 參考資產，尚未整合進 EXE。決策見 docs/DECISIONS.md 的 D0013。
@REM 使用者提供的手動修復腳本，內容保持原樣，供後續整合時比對。
@REM 注意：以 SYSTEM 執行時 HKCU 指向 SYSTEM 帳戶，不是目前登入的使用者。
title Reset Windows Network

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Please right-click this file and select Run as administrator.
    pause
    exit /b 1
)

echo [1/5] Disabling proxy for the current Windows user...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v AutoConfigURL /f >nul 2>&1

echo [2/5] Resetting WinHTTP proxy...
netsh winhttp reset proxy

echo [3/5] Flushing DNS cache...
ipconfig /flushdns

echo [4/5] Resetting Winsock...
netsh winsock reset

echo [5/5] Resetting TCP/IP...
netsh int ip reset

echo.
echo Finished. Check the messages above for any errors.
echo Restart Windows to complete the reset.
echo Close and reopen your browsers after restarting.
echo Apps with their own proxy settings must be configured separately.
pause
endlocal
