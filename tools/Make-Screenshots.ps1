#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Make-Screenshots.ps1 -- 给文档拍界面截图（浅色 + 深色）

    做法：起界面 → PrintWindow 抓窗口位图 → 存成 PNG。抓的是窗口自己的绘制结果，
    不要求窗口在最前面，也不会把桌面别的东西拍进去。
    "系统工具"那张会先按 BM_CLICK 点一下页签按钮，用来对比"真功能按钮"和"灰色占位按钮"。

    用法: powershell -File tools\Make-Screenshots.ps1
    产物: docs\gui-shot.png（浅色）、docs\dark-shot.png（深色）、docs\system-shot.png（系统工具页签）
    注意: 会临时改写 settings.ini 里的 Theme，跑完按原样复原。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root 'bin\Mxx1Toolbox.exe'
$docs = Join-Path $root 'docs'
$settingsFile = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\settings.ini'

if (-not (Test-Path -LiteralPath $exe)) { throw ('找不到 exe（先跑 build.ps1）: ' + $exe) }
if (-not [Environment]::UserInteractive) { Write-Host '没有交互式桌面，跳过。'; exit 3 }
[void][System.IO.Directory]::CreateDirectory($docs)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class Shot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr wp, StringBuilder lp, uint flags, uint timeout, out IntPtr res);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);

    public static IntPtr[] Children(IntPtr parent) {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list.ToArray();
    }

    // WM_GETTEXT across processes (GetWindowText does not work for controls of another process)
    public static string Text(IntPtr h) {
        StringBuilder sb = new StringBuilder(1024);
        IntPtr res;
        SendMessageTimeoutW(h, 0x000D, (IntPtr)sb.Capacity, sb, 0x0002, 5000, out res);
        return sb.ToString();
    }

    public static bool Click(IntPtr h) { return PostMessageW(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
}
'@

function Save-Shot {
    param([IntPtr]$Handle, [string]$Path)
    $r = New-Object Shot+RECT
    [void][Shot]::GetWindowRect($Handle, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # 0x2 = PW_RENDERFULLCONTENT（Win8.1+），能抓到 DWM 合成的标题栏
    [void][Shot]::PrintWindow($Handle, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ('  已保存 ' + $Path + '  (' + $w + 'x' + $h + ')')
}

# 按文字找一个子按钮并点它（点页签用）
function Click-ChildButton {
    param([IntPtr]$Handle, [string]$Text)
    foreach ($h in [Shot]::Children($Handle)) {
        if ([Shot]::Text($h) -eq $Text) { [void][Shot]::Click($h); return $true }
    }
    return $false
}

function Start-Shot {
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $exe
    $si.UseShellExecute = $false
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $si
    [void]$p.Start()
    for ($i = 0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        if ($p.HasExited) { return $p }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    Start-Sleep -Milliseconds 1200
    return $p
}

$before = $null
$existed = Test-Path -LiteralPath $settingsFile
if ($existed) { $before = [System.IO.File]::ReadAllText($settingsFile, [System.Text.Encoding]::UTF8) }

$utf8 = New-Object System.Text.UTF8Encoding($false)
[void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $settingsFile))

Write-Host '浅色主题...'
[System.IO.File]::WriteAllText($settingsFile, "Theme=light`r`nClickMode=single`r`nConfirmDangerous=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`n", $utf8)
$p1 = Start-Shot
if (-not $p1.HasExited -and $p1.MainWindowHandle -ne [IntPtr]::Zero) {
    [void][Shot]::SetForegroundWindow($p1.MainWindowHandle)
    Start-Sleep -Milliseconds 500
    Save-Shot -Handle $p1.MainWindowHandle -Path (Join-Path $docs 'gui-shot.png')

    # 再拍一张「系统工具」：那一页全是真功能按钮（彩色图标 + 近黑文字），
    # 正好和常用设置那一堆灰色占位按钮形成对照。
    if (Click-ChildButton -Handle $p1.MainWindowHandle -Text '系统工具') {
        Start-Sleep -Milliseconds 900
        Save-Shot -Handle $p1.MainWindowHandle -Path (Join-Path $docs 'system-shot.png')
    } else {
        Write-Host '  没找到「系统工具」页签按钮，跳过 system-shot.png'
    }
    try { $p1.Kill() } catch { }
}
Start-Sleep -Milliseconds 800

Write-Host '深色主题...'
[System.IO.File]::WriteAllText($settingsFile, "Theme=dark`r`nClickMode=single`r`nConfirmDangerous=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`n", $utf8)
$p2 = Start-Shot
if (-not $p2.HasExited -and $p2.MainWindowHandle -ne [IntPtr]::Zero) {
    [void][Shot]::SetForegroundWindow($p2.MainWindowHandle)
    Start-Sleep -Milliseconds 500
    Save-Shot -Handle $p2.MainWindowHandle -Path (Join-Path $docs 'dark-shot.png')
    try { $p2.Kill() } catch { }
}

if ($existed) { [System.IO.File]::WriteAllText($settingsFile, $before, $utf8) }
else { Remove-Item -LiteralPath $settingsFile -Force -ErrorAction SilentlyContinue }
Write-Host '设置已复原。'
