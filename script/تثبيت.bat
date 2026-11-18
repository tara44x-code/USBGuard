@echo off
chcp 65001 >nul
title USB Guard - التثبيت
cd /d "%~dp0"

:: طلب صلاحية المدير (Administrator)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo جاري طلب صلاحية المدير...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo ============================================
echo        تثبيت برنامج USB Guard
echo ============================================
echo.

set "DEST=%ProgramData%\USBGuard"
if not exist "%DEST%" mkdir "%DEST%"

copy /y "USBGuard.ps1" "%DEST%\USBGuard.ps1" >nul
echo [+] تم نسخ البرنامج الى %DEST%

:: انشاء مهمة تشتغل عند دخول ويندوز بصلاحية كاملة
schtasks /Create /TN "USBGuard" ^
 /TR "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%DEST%\USBGuard.ps1\"" ^
 /SC ONLOGON /RL HIGHEST /F >nul
echo [+] تم ضبط التشغيل التلقائي عند بدء ويندوز

:: تشغيل البرنامج الان بالخلفية
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%DEST%\USBGuard.ps1"
echo [+] البرنامج يعمل الان بالخلفية

echo.
echo ============================================
echo   تم التثبيت بنجاح. البرنامج فعّال الحين
echo   وراح يشتغل تلقائياً كل مرة تفتح ويندوز.
echo ============================================
echo.
pause
