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
if (Test-Path $icon) { $cscArgs += ('/win32icon:' + $icon) }
foreach ($j in $jsonFiles) { $cscArgs += ('/resource:' + $j.FullName + ',tools.' + $j.Name) }

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
    $stage = Join-Path $binDir 'Mxx1Toolbox-package'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    foreach ($d in @('', 'src', 'assets', 'tools', 'tests', 'docs')) {
        [void][System.IO.Directory]::CreateDirectory((Join-Path $stage $d))
    }
    Copy-Item $out $stage -Force
    Copy-Item (Join-Path $root 'build.ps1') $stage -Force
    Copy-Item (Join-Path $root 'README.md') $stage -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'CHANGELOG.md') $stage -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'LICENSE') $stage -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'src\*.cs') (Join-Path $stage 'src') -Force
    Copy-Item (Join-Path $root 'assets\*') (Join-Path $stage 'assets') -Force
    Copy-Item (Join-Path $root 'tools\*') (Join-Path $stage 'tools') -Force
    Copy-Item (Join-Path $root 'tests\*') (Join-Path $stage 'tests') -Force
    Copy-Item (Join-Path $root 'docs\*') (Join-Path $stage 'docs') -Force -ErrorAction SilentlyContinue
    $zip = Join-Path $binDir 'Mxx1Toolbox-package.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host ('packed   : ' + $zip + '  (' + (Get-Item $zip).Length + ' bytes)')
}
