#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Make-Package.ps1 -- 打「发布包」zip（默认 bin\Mxx1Toolbox-package.zip）

    平时由 build.ps1 -Package 调用；单独跑也行（前提是 bin\Mxx1Toolbox.exe 已经编出来了）：
        powershell -ExecutionPolicy Bypass -File tools\Make-Package.ps1
        powershell -ExecutionPolicy Bypass -File tools\Make-Package.ps1 -StageDir <临时目录> -ZipPath <临时.zip>

    为什么单独抽成一个脚本（用户 2026-10-05 报的）：
      「bin-tools 里面只有 PermanentDeleteSetup.exe 进压缩包了，memreduct 没有进」。
      原来这十行写在 build.ps1 里，只挑隔壁那一个安装器复制，用户自己放进 bin-tools\ 的工具
      （工具文件夹 / 单个 exe）一个都没进包。同一个毛病还让 assets\icons\ 那一百多张按钮图标
      进了包却是**空目录** —— Copy-Item 拷目录不带 -Recurse 时只建目录、不拷里面的文件，
      而通配符看着"拷过了"，谁也不会去翻 zip 里到底有几个文件。
    所以现在：① 要打什么**逐条列出来**，不依赖通配符的隐式行为；② 整个目录树都拷；
      ③ 打完包**回读 zip 自检**，少一个文件就让打包失败（和 build.ps1 查内嵌资源一个套路）。

    打进去的 bin-tools\ 就是**工具箱 exe 旁边那个目录**（src\AppPaths.cs 里的 PayloadDir）：
    在哪台机器上打包，包里带的就是那台机器上真的在用的工具。只把缓存 / 临时文件 /
    说明.txt 排除在外（说明.txt 是工具箱第一次打开界面时自己写的那份，包里的永远是最新的）。

    参数:
      -Root      工具箱根目录（默认：本脚本的上一层）
      -StageDir  暂存目录（默认 bin\Mxx1Toolbox-package；测试传临时目录进来）
      -ZipPath   输出 zip（默认 bin\Mxx1Toolbox-package.zip）

    退出码: 0 = 打好包且自检通过, 1 = 失败
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$StageDir = '',
    [string]$ZipPath = ''
)

$ErrorActionPreference = 'Stop'

$plan = New-Object System.Collections.ArrayList

# 中文按两个字符宽算，控制台上那几列才对得齐（PadRight 只数字符个数）
function Format-Cell {
    param([string]$Text, [int]$Width)
    $w = 0
    foreach ($ch in $Text.ToCharArray()) {
        if ([int]$ch -gt 0x2E80) { $w += 2 } else { $w += 1 }
    }
    if ($w -ge $Width) { return $Text }
    return $Text + (' ' * ($Width - $w))
}

function Add-PackItem {
    param([string]$Source, [string]$Target, [string]$Group)
    [void]$plan.Add((New-Object PSObject -Property @{ Source = $Source; Target = $Target; Group = $Group }))
}

# 一整棵目录树进包。目录名 / 文件名 / 扩展名三张黑名单，都是小写比较。
function Add-PackTree {
    param(
        [string]$Dir,
        [string]$TargetPrefix,
        [string]$Group,
        [string[]]$SkipDirs = @(),
        [string[]]$SkipNames = @(),
        [string[]]$SkipExts = @()
    )
    if (-not (Test-Path -LiteralPath $Dir)) { return }
    foreach ($f in @(Get-ChildItem -LiteralPath $Dir -Recurse -File -Force | Sort-Object FullName)) {
        $rel = $f.FullName.Substring($Dir.Length).TrimStart('\')
        $parts = $rel.Split('\')
        $skipIt = $false
        $dirCount = $parts.Count - 1
        for ($i = 0; $i -lt $dirCount; $i++) {
            if ($SkipDirs -contains $parts[$i].ToLowerInvariant()) { $skipIt = $true }
        }
        if ($SkipNames -contains $f.Name.ToLowerInvariant()) { $skipIt = $true }
        if ($SkipExts -contains $f.Extension.ToLowerInvariant()) { $skipIt = $true }
        if ($skipIt) { continue }
        $target = $rel
        if ($TargetPrefix) { $target = $TargetPrefix + '\' + $rel }
        Add-PackItem $f.FullName $target $Group
    }
}

try {
    if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
    $Root = (Resolve-Path -LiteralPath $Root).Path
    $binDir = Join-Path $Root 'bin'
    if (-not $StageDir) { $StageDir = Join-Path $binDir 'Mxx1Toolbox-package' }
    if (-not $ZipPath) { $ZipPath = Join-Path $binDir 'Mxx1Toolbox-package.zip' }

    $exe = Join-Path $binDir 'Mxx1Toolbox.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw ('先编译再打包：找不到 ' + $exe + '（先跑 build.ps1）') }

    Write-Host ('packing  : ' + $Root)

    # ---- 要打进去的东西：一条一条列出来 -----------------------------------------------
    Add-PackItem $exe 'Mxx1Toolbox.exe' '程序'
    foreach ($one in @('build.ps1', 'README.md', 'CHANGELOG.md', 'LICENSE')) {
        $p = Join-Path $Root $one
        if (Test-Path -LiteralPath $p) { Add-PackItem $p $one '根目录' }
    }
    Add-PackTree (Join-Path $Root 'src')    'src'    '源码'
    Add-PackTree (Join-Path $Root 'assets') 'assets' '图标清单'
    Add-PackTree (Join-Path $Root 'tools')  'tools'  '工具脚本'
    Add-PackTree (Join-Path $Root 'tests')  'tests'  '测试'
    Add-PackTree (Join-Path $Root 'docs')   'docs'   '文档'

    # ---- bin-tools\：用户自己放进去的工具，整个目录跟着走 -----------------------------
    $payloadRoot = Join-Path $binDir 'bin-tools'
    $payloadCountBefore = $plan.Count
    if (Test-Path -LiteralPath $payloadRoot) {
        Add-PackTree $payloadRoot 'bin-tools' '外部工具' `
            -SkipDirs @('cache', 'temp', 'tmp', '.git') `
            -SkipNames @('说明.txt') `
            -SkipExts @('.tmp', '.log', '.bak')
    }
    $payloadCount = $plan.Count - $payloadCountBefore

    # 隔壁那个安装器没在 bin-tools\ 里的话，从隔壁仓库的 bin\ 补一份进来（原来的老行为，保留）
    $sibling = Join-Path (Split-Path -Parent $Root) 'permanent-delete-menu\bin\PermanentDeleteSetup.exe'
    $havePermdel = @($plan | Where-Object { $_.Target -eq 'bin-tools\PermanentDeleteSetup.exe' }).Count -gt 0
    if ($havePermdel) {
        Write-Host 'tools    : bin-tools\PermanentDeleteSetup.exe 来自工具箱自己的工具目录'
    } elseif (Test-Path -LiteralPath $sibling) {
        Add-PackItem $sibling 'bin-tools\PermanentDeleteSetup.exe' '外部工具'
        $payloadCount++
        Write-Host 'tools    : bin-tools\PermanentDeleteSetup.exe 从隔壁仓库的 bin\ 补进来'
    } else {
        Write-Host 'tools    : bin-tools\ 里没有 PermanentDeleteSetup.exe（没找到隔壁的，也没在工具目录里）'
    }

    # ---- 拷进暂存目录 ----------------------------------------------------------------
    if (Test-Path -LiteralPath $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
    [void][System.IO.Directory]::CreateDirectory($StageDir)
    foreach ($item in $plan) {
        $dst = Join-Path $StageDir $item.Target
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstDir)) { [void][System.IO.Directory]::CreateDirectory($dstDir) }
        Copy-Item -LiteralPath $item.Source -Destination $dst -Force
    }

    # ---- 压缩 ------------------------------------------------------------------------
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    try {
        Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $ZipPath -Force
    } catch {
        throw ('压缩失败（zip 可能正被别的程序打开着？）：' + $_.Exception.Message)
    }

    # ---- 打包自检：把 zip 读回来，一个一个对 -----------------------------------------
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $zipRead = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($e in $zipRead.Entries) { [void]$names.Add($e.FullName.Replace('/', '\')) }
    } finally { $zipRead.Dispose() }

    $missing = @($plan | Where-Object { -not $names.Contains($_.Target) })
    if ($missing.Count -gt 0) {
        Write-Host ''
        foreach ($m in @($missing | Select-Object -First 10)) { Write-Host ('  missing  : ' + $m.Target) }
        throw ('打包自检没过：zip 里少了 ' + $missing.Count + ' 个文件（上面列了前几个）')
    }

    Write-Host ''
    foreach ($g in @($plan | Group-Object Group | Sort-Object Name)) {
        Write-Host ('  ' + (Format-Cell $g.Name 10) + ': ' + $g.Count + ' 个文件')
    }

    # bin-tools 里的每个工具文件夹念一句：打包前最后一眼能看出"这个文件夹工具箱认不认"
    $toolFolders = @()
    foreach ($t in @($plan | Where-Object { $_.Target -like 'bin-tools\*' })) {
        $parts = $t.Target.Split('\')
        if ($parts.Count -ge 3) { $toolFolders += $parts[1] }
    }
    $toolFolders = @($toolFolders | Sort-Object -Unique)
    if ($toolFolders.Count -gt 0) {
        Write-Host ''
        Write-Host 'bin-tools 里的工具文件夹（回家打开工具箱会各长一个按钮）：'
        foreach ($fd in $toolFolders) {
            $files = @($plan | Where-Object { $_.Target -like ('bin-tools\' + $fd + '\*') })
            $hasJson = @($files | Where-Object { $_.Target -eq ('bin-tools\' + $fd + '\tool.json') }).Count -gt 0
            $hasExe = @($files | Where-Object { $_.Target -like '*.exe' }).Count -gt 0
            $warn = ''
            if (-not $hasJson -and -not $hasExe) { $warn = '   ← 既没有 tool.json 也没有 exe，工具箱不会给它长按钮' }
            Write-Host ('  ' + (Format-Cell $fd 18) + $files.Count.ToString() + ' 个文件' + $warn)
        }
    }

    $zipItem = Get-Item -LiteralPath $ZipPath
    Write-Host ''
    Write-Host ('packed   : ' + $zipItem.FullName)
    Write-Host ('size     : {0:N0} bytes, {1} 个文件（已回读 zip 逐个核对）' -f $zipItem.Length, $plan.Count)
}
catch {
    Write-Host ''
    Write-Host ('[FAIL] 打包失败：' + $_.Exception.Message)
    exit 1
}
exit 0
