#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Make-AppIcon.ps1 -- 生成 assets\app.ico（工具箱自己那个 exe 的图标）

    为什么单独一个脚本：`build.ps1` 里有 `/win32icon:assets\app.ico`，但 2026-10-04 之前
    **这个文件压根不存在** —— 于是那行等于没写，编出来的 Mxx1Toolbox.exe 是 .NET 默认图标
    （资源管理器 / 任务栏 / 标题栏全是那个空白纸片），用户报的「编译好的 exe 没有图标」就是它。

    图形：圆角蓝底 + 2x2 四个白色圆角方块 = 「一墙按钮」，和程序内图标（Make-Icons.ps1）同一套画法。
    每个尺寸**单独画一遍**（不是把大图缩下来），所以 16px 也是清楚的。

    产物: assets\app.ico（16/20/24/32/48/64/128/256 八个尺寸，都是 32 位 DIB）
          预览图写到 %TEMP%（不入仓），方便肉眼看一眼。
    用法: powershell -File tools\Make-AppIcon.ps1
          改完图标要重新 build.ps1，图标才会进 exe。
#>
[CmdletBinding()]
param(
    [switch]$NoPreview
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outFile = Join-Path $root 'assets\app.ico'

Add-Type -AssemblyName System.Drawing

# 底色用「常用」页签那个蓝（Make-Icons.ps1 的 tabColor.common），程序内图标一眼同源。
$inkColor = '#2E74B5'
$markColor = '#FFFFFF'

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $d = $R * 2
    if ($d -gt $W) { $d = $W }
    if ($d -gt $H) { $d = $H }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc(($X + $W - $d), $Y, $d, $d, 270, 90)
    $p.AddArc(($X + $W - $d), ($Y + $H - $d), $d, $d, 0, 90)
    $p.AddArc($X, ($Y + $H - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# 在 16x16 的设计坐标系里画，再按 $Size/16 放大 —— 尺寸换算是浮点，16px 也不会走样。
function New-AppBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $Size / 16.0

    # 底板：0.5 .. 15.5（和按钮图标一样留半像素留给描边）
    $bgPath = New-RoundedPath -X (0.5 * $s) -Y (0.5 * $s) -W (15.0 * $s) -H (15.0 * $s) -R (3.6 * $s)
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($inkColor))
    $g.FillPath($bg, $bgPath)

    # 四个方块：边长 4.6、间隔 1.5，整体居中（2.65 / 8.75）
    $mark = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($markColor))
    foreach ($y in @(2.65, 8.75)) {
        foreach ($x in @(2.65, 8.75)) {
            $q = New-RoundedPath -X ($x * $s) -Y ($y * $s) -W (4.6 * $s) -H (4.6 * $s) -R (1.0 * $s)
            $g.FillPath($mark, $q)
            $q.Dispose()
        }
    }

    $mark.Dispose(); $bg.Dispose(); $bgPath.Dispose(); $g.Dispose()
    return $bmp
}

# 32 位 DIB（BITMAPINFOHEADER + 自下而上的 BGRA + 全 0 的 AND 掩码）。
# 故意不写 PNG 压缩的那一套：.NET 的 Icon 读不了 PNG 帧，csc 打包时也更容易出岔子。
function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bmp)

    $w = $Bmp.Width
    $h = $Bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $Bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40)                  # biSize
    $bw.Write([int32]$w)                   # biWidth
    $bw.Write([int32]($h * 2))             # biHeight（XOR + AND 两张）
    $bw.Write([uint16]1)                   # biPlanes
    $bw.Write([uint16]32)                  # biBitCount
    $bw.Write([uint32]0)                   # biCompression = BI_RGB
    $bw.Write([uint32]($w * $h * 4))       # biSizeImage
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($buf, ($y * $stride), ($w * 4)) }
    $maskRow = [int]([Math]::Ceiling(($w / 8.0) / 4) * 4)
    $mask = New-Object byte[] ($maskRow * $h)
    $bw.Write($mask, 0, $mask.Length)
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return $bytes
}

function Save-Ico {
    param([string]$Path, [int[]]$Sizes)

    $frames = @()
    foreach ($sz in $Sizes) {
        $bmp = New-AppBitmap -Size $sz
        $frames += [pscustomobject]@{ Size = $sz; Data = (Get-DibBytes -Bmp $bmp) }
        $bmp.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)                       # reserved
    $bw.Write([uint16]1)                       # type = icon
    $bw.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $dim = $f.Size
        if ($dim -ge 256) { $dim = 0 }         # 256 在目录里写 0
        $bw.Write([byte]$dim); $bw.Write([byte]$dim)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$f.Data.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Data.Length
    }
    foreach ($f in $frames) { $bw.Write($f.Data, 0, $f.Data.Length) }
    $bw.Flush()

    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
    return $bytes.Length
}

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$len = Save-Ico -Path $outFile -Sizes $sizes
Write-Host ('已生成 ' + $outFile + '  (' + $len + ' 字节，' + ($sizes -join '/') + ' 八个尺寸)')
Write-Host '改完记得重新跑 build.ps1，/win32icon 才会把它打进 exe。'

if (-not $NoPreview) {
    # 肉眼看一眼用：256 那张缩到 160，后面按真实像素排 48/32/24/16（贴在同一张图上）。
    $sheet = New-Object System.Drawing.Bitmap(400, 180, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
    $big = New-AppBitmap -Size 256
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($big, (New-Object System.Drawing.Rectangle(10, 10, 160, 160)))
    $big.Dispose()
    $x = 190
    foreach ($sz in @(48, 32, 24, 16)) {
        $b = New-AppBitmap -Size $sz
        $g.DrawImage($b, $x, 10, $sz, $sz)
        $b.Dispose()
        $x += $sz + 12
    }
    $g.Dispose()
    $preview = Join-Path $env:TEMP 'mxx1-appicon-preview.png'
    $sheet.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host ('预览图: ' + $preview)
}
