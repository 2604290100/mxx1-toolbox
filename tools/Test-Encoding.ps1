#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
#
# Test-Encoding.ps1 -- encoding red-line check for this repository.
#
# Rules enforced here (all of them are "silently destroys the feature" class):
#   * .cs / .ps1        -> UTF-8 WITH BOM    (csc and PS 5.1 read BOM-less files as ANSI)
#   * .vbs              -> pure ASCII        (wscript reads ANSI; UTF-8 can swallow newlines)
#   * .md/.yml/.json/... -> UTF-8 WITHOUT BOM (a BOM would show up as text in json parsers)
#   * no hard-coded absolute path of a developer machine in code / config
#
# This file is deliberately PURE ASCII so it also runs correctly when its own BOM is lost.
#
# Usage:
#   powershell -File tools\Test-Encoding.ps1
#   powershell -File tools\Test-Encoding.ps1 -Fix     # repair BOMs in place
#
# Exit codes: 0 = clean, 1 = problems found, 2 = problems found and fixed (re-run to verify)

[CmdletBinding()]
param([switch]$Fix)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$skip = @('bin', '.git', '.vs', '.idea', 'node_modules', 'local')
$wantBom = @('.cs', '.ps1')
$noBom   = @('.md', '.json', '.yml', '.yaml', '.manifest', '.editorconfig', '.gitignore', '.gitattributes')
$asciiOnly = @('.vbs')
$codeFiles = @('.cs', '.ps1', '.json')

function Test-HasBom {
    param([string]$Path)
    $b = [System.IO.File]::ReadAllBytes($Path)
    return ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
}

function Set-Bom {
    param([string]$Path, [bool]$Want)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($Want)))
}

$problems = 0
$fixed = 0
$checked = 0

$files = Get-ChildItem $root -Recurse -File | Where-Object {
    $rel = $_.FullName.Substring($root.Length).TrimStart('\')
    $parts = $rel.Split('\')
    $skipIt = $false
    foreach ($p in $parts) { if ($skip -contains $p) { $skipIt = $true } }
    -not $skipIt
}

foreach ($f in $files) {
    $ext = $f.Extension.ToLowerInvariant()
    if ($ext -eq '.log') { continue }
    $checked++

    if ($wantBom -contains $ext) {
        if (-not (Test-HasBom $f.FullName)) {
            if ($Fix) { Set-Bom $f.FullName $true; $fixed++; Write-Host ('  fixed  BOM added   : ' + $f.Name) }
            else { $problems++; Write-Host ('  FAIL   missing BOM  : ' + $f.Name + '   (csc / PS 5.1 would read it as ANSI)') }
        }
    }
    elseif ($noBom -contains $ext) {
        if (Test-HasBom $f.FullName) {
            if ($Fix) { Set-Bom $f.FullName $false; $fixed++; Write-Host ('  fixed  BOM removed : ' + $f.Name) }
            else { $problems++; Write-Host ('  FAIL   stray BOM    : ' + $f.Name + '   (this file type must be UTF-8 without BOM)') }
        }
    }

    if ($asciiOnly -contains $ext) {
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        $max = ($b | Measure-Object -Maximum).Maximum
        if ($max -gt 127) {
            $problems++
            Write-Host ('  FAIL   non-ASCII    : ' + $f.Name + '   (max byte ' + $max + '; wscript reads ANSI)')
        }
    }

    if ($codeFiles -contains $ext) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            $lineNo++
            if ($line -match 'allow-abs-path') { continue }
            if ($line -match '(^|[^A-Za-z0-9_\\])[A-Za-z]:\\[A-Za-z0-9_]') {
                $problems++
                Write-Host ('  FAIL   abs path     : ' + $f.Name + ':' + $lineNo + '   ' + $line.Trim())
            }
        }
    }
}

Write-Host ''
if ($problems -eq 0 -and $fixed -eq 0) {
    Write-Host ('[PASS] encoding clean -- ' + $checked + ' files checked')
    exit 0
}
if ($Fix) {
    Write-Host ('[FIXED] ' + $fixed + ' file(s) rewritten; ' + $problems + ' problem(s) left. Re-run without -Fix.')
    exit 2
}
Write-Host ('[FAIL] ' + $problems + ' problem(s) in ' + $checked + ' files -- re-run with -Fix to repair BOMs')
exit 1
