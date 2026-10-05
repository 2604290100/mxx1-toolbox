#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
#
# build.ps1 -- build bin\Mxx1Toolbox.exe with the csc.exe that ships with .NET Framework 4.x.
# No .NET SDK required.
#
# This file is intentionally PURE ASCII: PowerShell 5.1 decodes a BOM-less .ps1 with the
# ANSI code page, which would mangle Chinese text. ASCII cannot be mangled, so the build
# still works even if a patch tool strips the BOM.
#
# It also (a) repairs the BOM of src\*.cs / tests\*.ps1 / tools\*.ps1 before compiling --
# csc and PS 5.1 read BOM-less sources as ANSI and silently mangle Chinese literals --
# and (b) strips the BOM from tools\*.json, which are parsed as UTF-8 text.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Package
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Clean

[CmdletBinding()]
param(
    [switch]$Package,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $root 'bin'
$out    = Join-Path $binDir 'Mxx1Toolbox.exe'

function Repair-Bom {
    param([string]$Path, [bool]$WantBom)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $has = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($has -eq $WantBom) { return $false }
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($WantBom)))
    return $true
}

if ($Clean -and (Test-Path $binDir)) { Remove-Item $binDir -Recurse -Force }

$csc = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw 'csc.exe not found (.NET Framework 4.x is required)' }

# ---- BOM repair: .cs and .ps1 must carry a BOM, .json must not ----
$withBom = @()
$withBom += @(Get-ChildItem (Join-Path $root 'src\*.cs')     -ErrorAction SilentlyContinue)
$withBom += @(Get-ChildItem (Join-Path $root 'tests\*.ps1')  -ErrorAction SilentlyContinue)
$withBom += @(Get-ChildItem (Join-Path $root 'tools\*.ps1')  -ErrorAction SilentlyContinue)
foreach ($f in $withBom) {
    if (Repair-Bom -Path $f.FullName -WantBom $true) { Write-Host ('  BOM added  : ' + $f.Name) }
}
foreach ($f in @(Get-ChildItem (Join-Path $root 'tools\*.json') -ErrorAction SilentlyContinue)) {
    if (Repair-Bom -Path $f.FullName -WantBom $false) { Write-Host ('  BOM removed: ' + $f.Name) }
}

$manifest = Join-Path $root 'assets\app.manifest'
if (-not (Test-Path $manifest)) { throw ('missing ' + $manifest) }

$jsonFiles = @(Get-ChildItem (Join-Path $root 'tools\*.json') | Sort-Object Name)
if ($jsonFiles.Count -eq 0) { throw 'tools\*.json not found -- the button list is missing' }

$sources = @(Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'src\*.cs not found' }

[void][System.IO.Directory]::CreateDirectory($binDir)

# A running copy locks the exe. Never kill it (the user may be using the tool): rename it aside
# instead -- renaming works while the process holds the file, and the process keeps running.
if (Test-Path $out) {
    $locked = $false
    try { $fs = [System.IO.File]::Open($out, 'Open', 'ReadWrite', 'None'); $fs.Close() }
    catch { $locked = $true }
    if ($locked) {
        $aside = $out + '.old-' + (Get-Date -Format 'HHmmss')
        Move-Item $out $aside -Force
        Write-Host ('note     : exe was in use, moved aside -> ' + (Split-Path -Leaf $aside))
    }
}

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/langversion:5'
    '/optimize+'
    '/warn:4'
    ('/out:' + $out)
    ('/win32manifest:' + $manifest)
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    '/reference:Microsoft.CSharp.dll'
)
$icon = Join-Path $root 'assets\app.ico'
if (Test-Path $icon) {
    $cscArgs += ('/win32icon:' + $icon)
    Write-Host ('icon     : ' + $icon + '  (' + (Get-Item $icon).Length + ' bytes)')
} else {
    # 2026-10-04 用户报「编译好的 exe 没有图标」：就是这一行在喊，但当时没人看 —— 现在它会响。
    Write-Warning 'assets\app.ico 不存在：编出来的 exe 没有图标（跑 tools\Make-AppIcon.ps1 生成后重新编译）'
}
foreach ($j in $jsonFiles) { $cscArgs += ('/resource:' + $j.FullName + ',tools.' + $j.Name) }

# The terms of service ship INSIDE the exe: the window that shows them and the fingerprint the
# consent gate stores must come from one and the same file (docs\DISCLAIMER.md). Editing that
# file therefore changes the fingerprint and everybody is asked to agree again -- by design.
$disclaimer = Join-Path $root 'docs\DISCLAIMER.md'
if (-not (Test-Path $disclaimer)) { throw ('missing ' + $disclaimer + ' (the terms window reads it from the exe)') }
$cscArgs += ('/resource:' + $disclaimer + ',Disclaimer.md')

# Button icons (tools\Make-Icons.ps1 output) are embedded too, so the exe stays a single file.
$iconFiles = @(Get-ChildItem (Join-Path $root 'assets\icons\*.png') -ErrorAction SilentlyContinue)
foreach ($i in $iconFiles) { $cscArgs += ('/resource:' + $i.FullName + ',icons.' + $i.Name) }

Write-Host ('compiler : ' + $csc)
& $csc @cscArgs $sources
if ($LASTEXITCODE -ne 0) { throw ('compile failed (exit ' + $LASTEXITCODE + ')') }

$exe = Get-Item $out
Write-Host ''
Write-Host ('built    : ' + $exe.FullName)
Write-Host ('size     : {0:N0} bytes' -f $exe.Length)

# ---- self check: are the button manifests really embedded? ----
$asm = [System.Reflection.Assembly]::LoadFile($out)
$names = @($asm.GetManifestResourceNames())
Write-Host ('resources: ' + ($names -join ', '))
foreach ($j in $jsonFiles) {
    $need = 'tools.' + $j.Name
    if (-not ($names -contains $need)) { throw ('embedded resource missing: ' + $need) }
}
$embeddedIcons = @($names | Where-Object { $_ -like 'icons.*.png' })
if ($iconFiles.Count -gt 0 -and $embeddedIcons.Count -ne $iconFiles.Count) {
    throw ('embedded icons: ' + $embeddedIcons.Count + ' of ' + $iconFiles.Count + ' -- run tools\Make-Icons.ps1 again')
}
if ($embeddedIcons.Count -gt 0) { Write-Host ('icons    : ' + $embeddedIcons.Count + ' embedded') }

# The terms window reads Disclaimer.md out of these resources. If the name ever drifts the window
# would silently fall back to its short built-in text, so fail the build instead.
if (-not ($names -contains 'Disclaimer.md')) { throw 'embedded resource missing: Disclaimer.md' }
Write-Host ('terms    : docs\DISCLAIMER.md embedded (' + (Get-Item $disclaimer).Length + ' bytes)')

# ---- companion skill (kept in the repo under skill\, synced to the workspace .dsh copy) ----
$skillName = 'mxx1-toolbox'
$skillCandidates = @(
    (Join-Path $root ('skill\' + $skillName)),
    $env:MXX1_TOOLBOX_SKILL_DIR,
    (Join-Path (Split-Path -Parent $root) ('.dsh\skills\' + $skillName)),
    (Join-Path $env:USERPROFILE ('.dsh\skills\' + $skillName))
) | Where-Object { $_ -and (Test-Path (Join-Path $_ 'SKILL.md')) }
if ($skillCandidates) { Write-Host ('skill    : ' + ($skillCandidates | Select-Object -First 1)) }

if ($Package) {
    # Packaging lives in tools\Make-Package.ps1 (this file has to stay PURE ASCII, and the packaging
    # itself needs verifying, which is easier to do -- and to test -- in its own script).
    #
    # Why it changed (user report, 2026-10-05): "bin-tools 里面只有 PermanentDeleteSetup.exe 进压缩包了，
    # memreduct 没有进". The old code here copied exactly one file (the sibling installer) and used
    # Copy-Item with wildcards, which also turned assets\icons\ into an EMPTY folder inside the zip.
    # The new script lists every file it packs, copies whole trees, and reads the zip back to check.
    #
    # Run it as a child process (same pattern as tests\Test-All.ps1) so the exit code is reliable.
    $pkg = Join-Path $root 'tools\Make-Package.ps1'
    if (-not (Test-Path $pkg)) { throw ('missing ' + $pkg) }
    $hostExe = (Get-Process -Id $PID).Path
    & $hostExe -NoProfile -ExecutionPolicy Bypass -File $pkg -Root $root
    if ($LASTEXITCODE -ne 0) { throw ('packaging failed (exit ' + $LASTEXITCODE + ')') }
}
