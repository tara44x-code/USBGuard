@echo off
chcp 65001 >nul
title USB Guard - الغاء التثبيت
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo جاري ايقاف وحذف البرنامج...

:: ايقاف المهمة المجدولة
schtasks /Delete /TN "USBGuard" /F >nul 2>&1

:: ايقاف اي نسخة شغالة من البرنامج
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*USBGuard.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }" >nul 2>&1

:: حذف ملفات البرنامج
rmdir /s /q "%ProgramData%\USBGuard" >nul 2>&1

echo.
echo تم حذف البرنامج بالكامل.
echo ملاحظة: اعدادات تعطيل الاوتورن تبقى فعّالة لحماية جهازك.
echo.
pause
