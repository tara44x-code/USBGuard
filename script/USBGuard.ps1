#=====================================================================
#  USB Guard  -  حماية الحاسبة والفلاشة
#  يقضي على فايروس الاوتورن (Autorun) وفايروس الاختصار (Shortcut)
#  يشتغل عند بدء ويندوز ويفحص الفلاشة تلقائياً عند توصيلها
#=====================================================================

$ErrorActionPreference = 'SilentlyContinue'

$LogDir  = Join-Path $env:ProgramData 'USBGuard'
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir 'USBGuard.log'

function Write-Log($msg) {
    $line = ('{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg)
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

#--------------------------------------------------------------------
# 1) تعطيل الاوتورن على كل النظام ومنع قراءة ملف autorun.inf
#--------------------------------------------------------------------
function Disable-Autorun {
    $paths = @(
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
    )
    foreach ($p in $paths) {
        if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
        New-ItemProperty -Path $p -Name 'NoDriveTypeAutoRun' -Value 0xFF -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $p -Name 'NoAutorun'          -Value 1    -PropertyType DWord -Force | Out-Null
    }
    # خدعة الريجستري: يخلي ويندوز يتجاهل اي ملف autorun.inf نهائياً
    $ini = 'HKLM:\Software\Microsoft\Windows NT\CurrentVersion\IniFileMapping\Autorun.inf'
    if (-not (Test-Path $ini)) { New-Item -Path $ini -Force | Out-Null }
    Set-ItemProperty -Path $ini -Name '(default)' -Value '@SYS:DoesNotExist'
    Write-Log 'تم تعطيل الاوتورن على النظام ومنع ملف autorun.inf'
}

#--------------------------------------------------------------------
# 1-ب) تصليح الاعدادات التي تخربها الفايروسات (Sality / gamez وغيرها)
#   - منع الكتابة على الفلاشة (Write Protect)
#   - منع تسجيل/حرق CD
#   - اخفاء الملفات وقفل خيارات المجلد
#   - قفل مدير المهام والريجستري
#--------------------------------------------------------------------
function Repair-System {
    # رفع منع الكتابة على الفلاشات
    $sdp = 'HKLM:\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies'
    if (Test-Path $sdp) { Set-ItemProperty -Path $sdp -Name 'WriteProtect' -Value 0 -Type DWord }

    # رفع قفل كتابة الفلاشات والاقراص من سياسات ويندوز
    $rs = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices'
    if (Test-Path $rs) { Remove-Item -Path $rs -Recurse -Force; Write-Log '  [+] رفع قفل الكتابة على الفلاشات/الاقراص' }

    foreach ($root in @('HKLM:','HKCU:')) {
        $exp = "$root\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
        $sys = "$root\Software\Microsoft\Windows\CurrentVersion\Policies\System"
        # ارجاع تسجيل CD وخيارات المجلد
        foreach ($n in @('NoCDBurning','NoFolderOptions','NoDrives','NoViewOnDrive')) {
            Remove-ItemProperty -Path $exp -Name $n -Force
        }
        # ارجاع مدير المهام والريجستري
        foreach ($n in @('DisableTaskMgr','DisableRegistryTools','DisableCMD')) {
            Remove-ItemProperty -Path $sys -Name $n -Force
        }
    }
    Remove-ItemProperty -Path 'HKCU:\Software\Policies\Microsoft\Windows\System' -Name 'DisableCMD' -Force

    # ارجاع اظهار الملفات المخفية
    $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    Set-ItemProperty -Path $adv -Name 'Hidden' -Value 1 -Type DWord
    $hid = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Folder\Hidden\SHOWALL'
    if (Test-Path $hid) { Set-ItemProperty -Path $hid -Name 'CheckedValue' -Value 1 -Type DWord }

    Write-Log 'تم تصليح اعدادات النظام (الكتابة، CD، الملفات المخفية، مدير المهام)'
}

#--------------------------------------------------------------------
# فحص الفلاشة بمحرك Windows Defender (حتى يمسك Sality والفايروسات الحقيقية)
#--------------------------------------------------------------------
function Scan-WithDefender($root) {
    $mp = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
    if (Test-Path $mp) {
        Write-Log "  [*] فحص $root بـ Windows Defender..."
        & $mp -Scan -ScanType 3 -File $root | Out-Null
        Write-Log "  [*] انتهى فحص Defender (النتائج بسجل الحماية في Windows Security)"
    }
}

#--------------------------------------------------------------------
# 2) فحص وتنظيف قرص / فلاشة
#--------------------------------------------------------------------
function Clean-Drive($root) {
    if ([string]::IsNullOrEmpty($root) -or -not (Test-Path $root)) { return }
    Write-Log "بدء فحص القرص: $root"
    $removed = 0

    # --- حذف ملف autorun.inf من جذر الفلاشة ---
    $auto = Join-Path $root 'autorun.inf'
    if (Test-Path -LiteralPath $auto) {
        cmd.exe /c "attrib -r -h -s `"$auto`"" | Out-Null
        Remove-Item -LiteralPath $auto -Force
        Write-Log '  [+] حذف autorun.inf'
        $removed++
    }

    # --- اعادة اظهار كل الملفات والمجلدات التي خفاها الفايروس ---
    cmd.exe /c "attrib -s -h -r `"$root*`" /s /d" | Out-Null
    Write-Log '  [+] اعادة اظهار الملفات والمجلدات المخفية'

    # --- حذف اختصارات فايروس الشورتكت من جذر الفلاشة ---
    # الاختصار الخبيث يشير عادة الى cmd / wscript / powershell
    Get-ChildItem -LiteralPath $root -Filter *.lnk -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $target = ''; $arguments = ''
        try {
            $sh  = New-Object -ComObject WScript.Shell
            $lnk = $sh.CreateShortcut($_.FullName)
            $target    = $lnk.TargetPath
            $arguments = $lnk.Arguments
        } catch {}
        $full = "$target $arguments"
        if ($full -match 'cmd\.exe|wscript|cscript|powershell|rundll32|forfiles|mshta|\.vbs|\.bat|\.cmd') {
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Log "  [+] حذف اختصار مشبوه: $($_.Name)"
            $removed++
        }
    }

    # --- حذف الملفات التنفيذية المخفية+النظام من جذر الفلاشة (نمط الفايروس) ---
    Get-ChildItem -LiteralPath $root -Force -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -match '\.(exe|vbs|bat|cmd|scr|pif|com|js|jse|vbe|wsf)$' -and
            ($_.Attributes -band [IO.FileAttributes]::Hidden) -and
            ($_.Attributes -band [IO.FileAttributes]::System)
        } |
        ForEach-Object {
            cmd.exe /c "attrib -h -s -r `"$($_.FullName)`"" | Out-Null
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Log "  [+] حذف ملف تنفيذي مخفي مشبوه: $($_.Name)"
            $removed++
        }

    # --- الفلاشة تطلع فارغة: الفايروس ينقل الملفات لمجلد اسمه فراغ/رمز مخفي ---
    Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.Trim([char]0x00A0,[char]0x2007,[char]0x202F,[char]0x200B,' ','_') -eq '' } |
        ForEach-Object {
            Write-Log "  [+] لگيت مجلد مخفي بلا اسم (الفلاشة تطلع فارغة) - جاري ارجاع الملفات"
            Get-ChildItem -LiteralPath $_.FullName -Force | ForEach-Object {
                $dest = Join-Path $root $_.Name
                if (-not (Test-Path -LiteralPath $dest)) { Move-Item -LiteralPath $_.FullName -Destination $root -Force }
            }
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force)) { Remove-Item -LiteralPath $_.FullName -Force }
        }

    # --- اسماء فايروسات فلاشة معروفة (gamez وغيرها) ---
    $knownBad = @('gamez.exe','gamez.lnk','gamez.vbs','games.exe','gamez',
                  'Thumbs.exe','desktop.exe','autorun.exe','system.exe','svchost.exe',
                  'winlogon.exe','csrss.exe','lsass.exe','explorer.exe','DriveGuard.exe',
                  'Copy of Shortcut to.lnk','WindowsServices','IndexerVolumeGuid.vbs')
    foreach ($kb in $knownBad) {
        $kp = Join-Path $root $kb
        if (Test-Path -LiteralPath $kp) {
            cmd.exe /c "attrib -h -s -r `"$kp`"" | Out-Null
            Remove-Item -LiteralPath $kp -Recurse -Force
            Write-Log "  [+] حذف فايروس معروف: $kb"
            $removed++
        }
    }
    # اي ملف exe بجذر الفلاشة اسمه نفس اسم مجلد موجود (حيلة الفايروس: Photos.exe جنب مجلد Photos)
    Get-ChildItem -LiteralPath $root -Filter *.exe -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
        if (Test-Path -LiteralPath (Join-Path $root $_.BaseName) -PathType Container) {
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Log "  [+] حذف ملف exe منتحل اسم مجلد: $($_.Name)"
            $removed++
        }
    }

    # --- حذف مجلدات وهمية شائعة للفايروسات ---
    foreach ($bad in @('RECYCLER','RECYCLED','Autorun','New folder.exe')) {
        $bp = Join-Path $root $bad
        if (Test-Path -LiteralPath $bp) {
            cmd.exe /c "attrib -h -s -r `"$bp`"" | Out-Null
            Remove-Item -LiteralPath $bp -Recurse -Force
            Write-Log "  [+] حذف عنصر مشبوه: $bad"
            $removed++
        }
    }

    Scan-WithDefender $root
    Write-Log "  انتهى فحص $root  -  عدد العناصر المزالة: $removed"

    # تنبيه على الشاشة اذا لگى فايروس
    if ($removed -gt 0) {
        try {
            $w = New-Object -ComObject WScript.Shell
            $w.Popup("USB Guard: تم تنظيف الفلاشة $root`nتم حذف $removed عنصر مشبوه وارجاع ملفاتك.", 8, 'USB Guard', 64) | Out-Null
        } catch {}
    }
}

#--------------------------------------------------------------------
# 3) التشغيل: تعطيل الاوتورن + فحص المتصل حالياً + مراقبة اي فلاشة جديدة
#--------------------------------------------------------------------
Write-Log '================ تشغيل USB Guard ================'
Disable-Autorun
Repair-System

$known = @{}
# فحص اولي لكل الاقراص القابلة للازالة المتصلة الان
Get-WmiObject Win32_LogicalDisk -Filter 'DriveType=2' -ErrorAction SilentlyContinue | ForEach-Object {
    $known[$_.DeviceID] = $true
    Clean-Drive ($_.DeviceID + '\')
}

# حلقة مراقبة مستمرة (كل 3 ثواني) للكشف عن توصيل فلاشة جديدة
while ($true) {
    Start-Sleep -Seconds 3
    $current = @{}
    Get-WmiObject Win32_LogicalDisk -Filter 'DriveType=2' -ErrorAction SilentlyContinue | ForEach-Object {
        $current[$_.DeviceID] = $true
        if (-not $known.ContainsKey($_.DeviceID)) {
            Write-Log "تم توصيل فلاشة جديدة: $($_.DeviceID)"
            Start-Sleep -Milliseconds 800   # انتظار حتى يجهز القرص
            Clean-Drive ($_.DeviceID + '\')
        }
    }
    $known = $current
}
