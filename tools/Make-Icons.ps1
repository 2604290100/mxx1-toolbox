#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Make-Icons.ps1 -- 给每个按钮画一个 16x16 PNG 图标

    思路：按"页签定底色 + 按钮名里的关键词定图形"批量生成，免得手画几十张图。
    图形全部用 GDI+ 基本图元画（方正、白描、无字体依赖），所以不存在"微软雅黑没这个字形"的问题。
    危险按钮一律用红色底 + 感叹号，一眼能认出来。

    产物: assets\icons\<id>.png（每个按钮一个，清单直接从 exe 的 list 读，所以先 build 再跑）
    用法: powershell -File tools\Make-Icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root 'bin\Mxx1Toolbox.exe'
$outDir = Join-Path $root 'assets\icons'

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Rect, [float]$Radius)
    $d = $Radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $p.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $p.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Get-Shape {
    param([string]$Id, [string]$Name)
    $key = ($Id + ' ' + $Name).ToLowerInvariant()
    # 顺序有意义：先匹配更具体的词
    $map = @(
        # 「右键增强」页签那 7 个按钮（v1.5.0）：装上 = 加号、撤掉 = 减号、
        # 解除占用 = 钥匙（解锁）、常用功能 = 清单、重建 = 刷新、说明 = 文档
        @('rightmenu.unlock.on',  'key'),
        @('rightmenu.unlock.off', 'minus'),
        # v1.5.4 加的第三个右键项：一键解除（不弹窗，查到占用就直接结束）—— 用闪电表示"一下就完"，
        # 撤掉还是一个减号。注意这两行必须在下面那些泛匹配之前。
        @('rightmenu.auto.on',    'bolt'),
        @('rightmenu.auto.off',   'minus'),
        @('rightmenu.common.on',  'list'),
        @('rightmenu.common.off', 'minus'),
        # 2026-10-06 晚五加的两项：复制文件路径 = 叠起来的两张纸，在此处打开终端 = 终端窗口 + 提示符
        # 晚六「复制」拆成两条平铺的菜单项，所以又多了「复制文件名」那一对；两张纸的图形两对共用
        # （同一个功能的两条，看菜单文字分得清），撤掉还是减号。
        @('rightmenu.copyname.on', 'copy'),
        @('rightmenu.copyname.off','minus'),
        @('rightmenu.copy.on',     'copy'),
        @('rightmenu.copy.off',    'minus'),
        @('rightmenu.terminal.on', 'terminal'),
        @('rightmenu.terminal.off','minus'),
        # 2026-10-15 加的两项：图片转换 = 循环箭头（"换一种格式"最直观）、图标提取 = 两张纸（把图标取出来存一份）
        @('rightmenu.convert.on',  'refresh'),
        @('rightmenu.convert.off', 'minus'),
        @('rightmenu.icon.on',     'copy'),
        @('rightmenu.icon.off',    'minus'),
        @('rightmenu.rebuild',    'refresh'),
        @('rightmenu.help',       'doc'),
        # 2026-10-19 加的六个窗口按钮（清理页「文件清单」段 + 系统页「进程 · 信息 · 链接」段）：
        # 无效快捷方式 = 断开的链子、空文件夹 = 空文件夹轮廓、重复文件 = 两张纸、
        # 批量重命名 = 铅笔、取色器 = 水滴、端口与进程 = 插头
        @('broken-links',   'linkbreak'),
        @('empty-folders',  'folder-empty'),
        @('dup-files',      'copy'),
        @('batch-rename',   'pencil'),
        @('color-picker',   'droplet'),
        @('port-view',      'plug'),
        @('update-cache',   'trash'),
        # 2026-10-20 加的八个「系统工具页入口」（A 组）：磁盘管理 / 优化驱动器 = 硬盘、
        # 计算机管理 / 系统配置 = 齿轮、网络连接 = 地球、Windows 功能 = 加号、
        # 用户账户 = 钥匙、字体 = 文档。
        # ⚠ 这几条必须排在下面那些泛匹配之前：`windows-features` 里含 "features"，
        # 撞上老那条 @('features','window') 就会长成应用页那张图（这个表是顺序匹配、先到先得）。
        # （原计划里的「Internet 选项 inetcpl.cpl」没做：这台机器的精简版 Windows 里
        #   连这个文件都没有，而"Win10/11 一个都不许缺"的看门狗会当场红 —— 宁可不做也不放个假入口。）
        @('windows-features', 'plus'),
        @('diskmgmt', 'disk'),
        @('dfrgui',   'disk'),
        @('compmgmt', 'gear'),
        @('msconfig', 'gear'),
        @('ncpa',     'globe'),
        @('netplwiz', 'key'),
        @('fonts',    'doc'),
        # 文件哈希校验（未发布这轮加的）：一个 "#" 号 —— 文件"指纹"最直观的写法
        @('hash',           'hash'),
        @('sfc',            'wrench'),
        @('dism',           'wrench'),
        @('disk-check',     'disk'),
        @('chkdsk',         'disk'),
        @('net-reset',      'globe'),
        @('time-sync',      'clock'),
        @('export-logs',    'doc'),
        @('restore-point',  'refresh'),
        @('backup',         'doc'),
        @('perfmon',        'chart'),
        @('eventvwr',       'list'),
        @('privacy-status',    'info'),
        @('privacy-optimize',  'check'),
        @('privacy-restore',   'refresh'),
        @('privacy-telemetry', 'eye'),
        @('privacy-errorreport','doc'),
        @('privacy-cortana',   'search'),
        @('privacy-bing',      'search'),
        @('privacy-speech',    'speaker'),
        @('privacy-typing',    'doc'),
        @('privacy-activity',  'list'),
        @('privacy-adid',      'eye'),
        @('privacy-ads',       'eye'),
        @('privacy-delivery',  'refresh'),
        @('privacy-feedback',  'doc'),
        @('privacy-camera',    'eye'),
        @('privacy-microphone','speaker'),
        @('apps-list',         'list'),
        @('apps-uninstall',    'minus'),
        @('apps-startup',      'clock'),
        @('features',          'window'),
        @('apps-default',      'window'),
        @('privacy-location',  'globe'),
        @('privacy-background','window'),
        @('privacy',           'eye'),
        # 「常用设置」里那批写注册表的按钮（builtin/sysreg）：查看改动 / 还原改动
        @('sysreg-status',     'info'),
        @('sysreg-restore',    'refresh'),
        # 2026-10-20 加的 B 组四对开关（文件扩展名 / 隐藏文件 / 小箭头 / 时钟秒）：
        # 扩展名 = 文档、隐藏文件 = 眼睛、去掉箭头 = 减号 / 恢复箭头 = 循环、时钟 = 时钟
        @('ext-show',          'doc'),
        @('ext-hide',          'doc'),
        @('hidden-show',       'eye'),
        @('hidden-hide',       'eye'),
        @('arrow-hide',        'minus'),
        @('arrow-restore',     'refresh'),
        @('clock-sec',         'clock'),
        @('clock-nosec',       'clock'),
        # 2026-10-20 第二批：C2 修复图标缓存 = 循环箭头；B5 小组件 = 图表（面板）、B6 Copilot = 放大镜、
        # B7 菜单延迟「更快」= 闪电 /「恢复」= 循环、B8 快速启动 = 电源。
        # ⚠ 必须排在下面那些泛匹配之前（`faststart` 里没有能撞的词，但 `icon-cache` 别被将来的 icon 规则吃掉）
        @('icon-cache',        'refresh'),
        @('widgets-hide',      'chart'),
        @('widgets-show',      'chart'),
        @('copilot-hide',      'search'),
        @('copilot-show',      'search'),
        @('menu-fast',         'bolt'),
        @('menu-default',      'refresh'),
        @('faststart-on',      'power'),
        @('faststart-off',     'power'),
        # 2026-10-20 第三批（用户点名的「隐藏未激活水印」）：藏起来 = 减号 / 恢复 = 循环箭头，
        # 跟「去掉 / 恢复小箭头」那一对同一个画法（都是"把某个提示藏掉 / 放回来"）。
        @('watermark-hide',    'minus'),
        @('watermark-restore', 'refresh'),
        @('appx',           'list'),
        @('uninstall-app',  'minus'),
        @('default-app',    'window'),
        @('permission',     'shield'),
        @('health',         'check'),
        @('taskbar',        'monitor'),
        @('startmenu',      'window'),
        @('desktop',        'monitor'),
        @('explorer',       'folder'),
        @('ctxmenu',        'window'),
        @('driver',         'gear'),
        @('core-isolation', 'shield'),
        @('tamper',         'shield'),
        @('defender',       'shield'),
        @('firewall',       'shield'),
        @('uac',            'shield'),
        @('smartscreen',    'shield'),
        @('activate',       'key'),
        @('bitlocker',      'key'),
        @('update',         'refresh'),
        @('metered',        'chart'),
        # 2026-10-19 加的「电源与笔记本」段（合盖与电源键动作 / 体检 / 电池 / 唤醒 / 桌面模式）——
        # ⚠ 必须排在下面那条 `@('power-','bolt')` **前面**，否则 power-* 全被它吃掉
        @('power-lid',      'power'),
        @('power-battery',  'battery'),
        @('power-fit',      'check'),
        @('power-wake',     'clock'),
        @('power-desk',     'monitor'),
        @('hibernate',      'power'),
        @('power-',         'bolt'),
        @('dns',            'globe'),
        @('hosts',          'globe'),
        @('permdel',        'trash'),
        @('install',        'plus'),
        @('uninstall',      'minus'),
        @('status',         'info'),
        @('verify',         'check'),
        @('disclaimer',     'doc'),
        @('enginelog',      'list'),
        @('about',          'info'),
        @('clean',          'trash'),
        @('recyclebin',     'trash'),
        @('storage',        'chart'),
        @('startup',        'clock'),
        @('big-file',       'search'),
        @('devmgmt',        'gear'),
        @('sound',          'speaker'),
        @('printers',       'doc'),
        @('scheduler',      'clock'),
        @('regedit',        'wrench'),
        @('services',       'gear'),
        @('gpedit',         'wrench'),
        @('appwiz',         'list'),
        @('taskmgr',        'chart'),
        @('sysinfo',        'info'),
        @('links',          'globe'),
        @('control-panel',  'wrench'),
        @('newtool',        'plus'),
        @('search',         'search'),
        @('restart',        'refresh')
    )
    foreach ($pair in $map) {
        if ($key.Contains($pair[0])) { return $pair[1] }
    }
    return 'dot'
}

function Draw-Shape {
    param([System.Drawing.Graphics]$G, [string]$Shape, [System.Drawing.Brush]$Brush, [System.Drawing.Pen]$Pen)
    $cx = 8.0; $cy = 8.0
    switch ($Shape) {
        'monitor' {
            $G.DrawRectangle($Pen, 3.5, 4.5, 9, 6)
            $G.DrawLine($Pen, 8, 10.5, 8, 12)
            $G.DrawLine($Pen, 5.5, 12.5, 10.5, 12.5)
        }
        'window' {
            $G.DrawRectangle($Pen, 3.5, 4.5, 9, 7)
            $G.DrawLine($Pen, 3.5, 6.8, 12.5, 6.8)
            $G.FillRectangle($Brush, 4.6, 5.4, 1.4, 1)
            $G.DrawLine($Pen, 5.5, 9, 10.5, 9)
        }
        'folder' {
            $pts = @(
                (New-Object System.Drawing.PointF(3, 6)),
                (New-Object System.Drawing.PointF(7, 6)),
                (New-Object System.Drawing.PointF(8.2, 7.4)),
                (New-Object System.Drawing.PointF(13, 7.4)),
                (New-Object System.Drawing.PointF(13, 11.6)),
                (New-Object System.Drawing.PointF(3, 11.6))
            )
            $G.FillPolygon($Brush, $pts)
        }
        'gear' {
            $G.FillEllipse($Brush, 5.5, 5.5, 5, 5)
            foreach ($a in @(0, 45, 90, 135, 180, 225, 270, 315)) {
                $rad = $a * [Math]::PI / 180
                $x = $cx + [Math]::Cos($rad) * 5.2
                $y = $cy + [Math]::Sin($rad) * 5.2
                $G.FillRectangle($Brush, [float]($x - 0.9), [float]($y - 0.9), 1.8, 1.8)
            }
        }
        'shield' {
            $pts = @(
                (New-Object System.Drawing.PointF(8, 3.5)),
                (New-Object System.Drawing.PointF(12.5, 5.2)),
                (New-Object System.Drawing.PointF(12.5, 8.4)),
                (New-Object System.Drawing.PointF(8, 12.5)),
                (New-Object System.Drawing.PointF(3.5, 8.4)),
                (New-Object System.Drawing.PointF(3.5, 5.2))
            )
            $G.FillPolygon($Brush, $pts)
        }
        'key' {
            $G.DrawEllipse($Pen, 4, 4.5, 4.5, 4.5)
            $G.DrawLine($Pen, 9, 7, 12.5, 10.5)
            $G.DrawLine($Pen, 11, 9, 12.6, 7.4)
            $G.DrawLine($Pen, 12.4, 10.4, 13.4, 9.4)
        }
        'refresh' {
            $G.DrawArc($Pen, 3.5, 3.5, 9, 9, 40, 280)
            $G.FillPolygon($Brush, @(
                (New-Object System.Drawing.PointF(11.6, 2.6)),
                (New-Object System.Drawing.PointF(12.8, 6.4)),
                (New-Object System.Drawing.PointF(9.0, 5.2))
            ))
        }
        'power' {
            $G.DrawArc($Pen, 3.5, 4.5, 9, 8.5, 300, 300)
            $G.DrawLine($Pen, 8, 3, 8, 8)
        }
        'battery' {
            # 「电池健康报告」：一节电池（外框 + 右边那个正极 + 里面两格电）
            $G.DrawRectangle($Pen, 2.6, 5.0, 9.4, 6.4)
            $G.DrawRectangle($Pen, 12.4, 6.9, 1.6, 2.6)
            $G.DrawLine($Pen, 4.8, 6.9, 4.8, 9.5)
            $G.DrawLine($Pen, 7.2, 6.9, 7.2, 9.5)
        }
        'bolt' {
            $G.FillPolygon($Brush, @(
                (New-Object System.Drawing.PointF(9.2, 2.8)),
                (New-Object System.Drawing.PointF(4.6, 8.8)),
                (New-Object System.Drawing.PointF(7.6, 8.8)),
                (New-Object System.Drawing.PointF(6.6, 13.2)),
                (New-Object System.Drawing.PointF(11.4, 7.0)),
                (New-Object System.Drawing.PointF(8.4, 7.0))
            ))
        }
        'globe' {
            $G.DrawEllipse($Pen, 3, 3, 10, 10)
            $G.DrawEllipse($Pen, 6.2, 3, 3.6, 10)
            $G.DrawLine($Pen, 3, 8, 13, 8)
        }
        'copy' {
            # 「复制文件路径」：两张错开叠在一起的纸（最直白的"复制"图形）
            $G.DrawRectangle($Pen, 3, 3, 7.6, 8.4)
            $G.DrawLine($Pen, 6.4, 11.4, 6.4, 12.6)
            $G.DrawLine($Pen, 6.4, 12.6, 13, 12.6)
            $G.DrawLine($Pen, 13, 12.6, 13, 5)
            $G.DrawLine($Pen, 13, 5, 11.6, 5)
        }
        'terminal' {
            # 「在此处打开终端」：一个终端窗口 + 里面的命令提示符（> 加一条下划线光标）
            $G.DrawRectangle($Pen, 2.5, 3.5, 11, 9)
            $G.DrawLine($Pen, 2.5, 5.8, 13.5, 5.8)
            $G.DrawLines($Pen, @(
                (New-Object System.Drawing.PointF(4.6, 8.0)),
                (New-Object System.Drawing.PointF(6.4, 9.8)),
                (New-Object System.Drawing.PointF(4.6, 11.2))
            ))
            $G.DrawLine($Pen, 7.6, 11.4, 11.2, 11.4)
        }
        'plus' {
            $G.FillRectangle($Brush, 7.1, 4, 1.8, 8)
            $G.FillRectangle($Brush, 4, 7.1, 8, 1.8)
        }
        'minus' {
            $G.FillRectangle($Brush, 4, 7.1, 8, 1.8)
        }
        'check' {
            $G.DrawLines($Pen, @(
                (New-Object System.Drawing.PointF(3.8, 8.4)),
                (New-Object System.Drawing.PointF(6.8, 11.4)),
                (New-Object System.Drawing.PointF(12.2, 4.8))
            ))
        }
        'doc' {
            $G.DrawRectangle($Pen, 4, 3, 8, 10)
            $G.DrawLine($Pen, 6, 6, 10, 6)
            $G.DrawLine($Pen, 6, 8.2, 10, 8.2)
            $G.DrawLine($Pen, 6, 10.4, 9, 10.4)
        }
        'list' {
            foreach ($y in @(5, 8, 11)) {
                $G.FillRectangle($Brush, 4, ($y - 0.9), 1.8, 1.8)
                $G.DrawLine($Pen, 7, $y, 12.2, $y)
            }
        }
        'trash' {
            $G.FillRectangle($Brush, 4, 3.4, 8, 1.4)
            $G.FillRectangle($Brush, 6.6, 2.2, 2.8, 1)
            $G.DrawRectangle($Pen, 5, 5.2, 6, 7.4)
            $G.DrawLine($Pen, 7, 6.8, 7, 11)
            $G.DrawLine($Pen, 9, 6.8, 9, 11)
        }
        'clock' {
            $G.DrawEllipse($Pen, 3.2, 3.2, 9.6, 9.6)
            $G.DrawLine($Pen, 8, 5.2, 8, 8)
            $G.DrawLine($Pen, 8, 8, 10.4, 9.4)
        }
        'wrench' {
            $G.DrawEllipse($Pen, 3.2, 3.2, 4.6, 4.6)
            $G.DrawLine($Pen, 6.8, 6.8, 12.4, 12.4)
            $G.DrawLine($Pen, 4.4, 7.2, 8.8, 11.6)
        }
        'chart' {
            $G.FillRectangle($Brush, 3.6, 8.4, 2.2, 4.2)
            $G.FillRectangle($Brush, 6.9, 5.6, 2.2, 7.0)
            $G.FillRectangle($Brush, 10.2, 3.2, 2.2, 9.4)
        }
        'speaker' {
            $G.FillPolygon($Brush, @(
                (New-Object System.Drawing.PointF(3.4, 6.4)),
                (New-Object System.Drawing.PointF(6.0, 6.4)),
                (New-Object System.Drawing.PointF(8.4, 3.8)),
                (New-Object System.Drawing.PointF(8.4, 12.2)),
                (New-Object System.Drawing.PointF(6.0, 9.6)),
                (New-Object System.Drawing.PointF(3.4, 9.6))
            ))
            $G.DrawArc($Pen, 7.4, 5.2, 4.4, 5.6, -60, 120)
        }
        'exclamation' {
            $G.FillRectangle($Brush, 7.0, 3.4, 2.0, 6.2)
            $G.FillRectangle($Brush, 7.0, 11.0, 2.0, 2.0)
        }
        'search' {
            $G.DrawEllipse($Pen, 3.4, 3.4, 6.6, 6.6)
            $G.DrawLine($Pen, 9.6, 9.6, 13.2, 13.2)
        }
        'info' {
            $G.FillRectangle($Brush, 7.0, 3.6, 2.0, 2.0)
            $G.FillRectangle($Brush, 7.0, 6.8, 2.0, 5.8)
        }
        'eye' {
            # 隐私类：一只眼睛（画两段弧 + 瞳孔）
            $G.DrawArc($Pen, 2.6, 5.0, 10.8, 7.0, 200, 140)
            $G.DrawArc($Pen, 2.6, 4.0, 10.8, 7.0, 20, 140)
            $G.FillEllipse($Brush, 6.6, 6.6, 2.8, 2.8)
        }
        'disk' {
            # 磁盘检测：两个同心圆 = 盘片 + 中间的轴
            $G.DrawEllipse($Pen, 3.0, 3.0, 10.0, 10.0)
            $G.DrawEllipse($Pen, 6.0, 6.0, 4.0, 4.0)
            $G.FillEllipse($Brush, 7.2, 7.2, 1.6, 1.6)
        }
        'hash' {
            # 文件哈希校验：一个 "#" 号（两条竖 + 两条横），像指纹一样代表"就是这个文件"
            $G.DrawLine($Pen, 6.2, 3.4, 5.0, 12.6)
            $G.DrawLine($Pen, 10.2, 3.4, 9.0, 12.6)
            $G.DrawLine($Pen, 4.0, 6.2, 12.0, 6.2)
            $G.DrawLine($Pen, 3.6, 9.8, 11.6, 9.8)
        }
        'linkbreak' {
            # 无效快捷方式：一段链子（两个椭圆 + 中间连着）在右边断开
            $G.DrawEllipse($Pen, 2.4, 6.0, 5.0, 4.0)
            $G.DrawEllipse($Pen, 8.6, 6.0, 5.0, 4.0)
            $G.DrawLine($Pen, 6.4, 8, 7.4, 8)
            # 断口：一道斜杠 + 两个小断点
            $G.DrawLine($Pen, 7.8, 3.2, 10.6, 5.4)
            $G.DrawLine($Pen, 11.6, 7.4, 13.2, 9.6)
        }
        'folder-empty' {
            # 空文件夹：只画文件夹轮廓（不填实），里面什么都没有
            $G.DrawLines($Pen, @(
                (New-Object System.Drawing.PointF(3.0, 5.4)),
                (New-Object System.Drawing.PointF(6.8, 5.4)),
                (New-Object System.Drawing.PointF(8.0, 6.8)),
                (New-Object System.Drawing.PointF(13.0, 6.8)),
                (New-Object System.Drawing.PointF(13.0, 11.8)),
                (New-Object System.Drawing.PointF(3.0, 11.8)),
                (New-Object System.Drawing.PointF(3.0, 5.4))
            ))
        }
        'pencil' {
            # 批量重命名：一支斜着写的铅笔 + 底下的笔画线
            $G.DrawLine($Pen, 4.2, 11.8, 11.2, 4.8)
            $G.DrawLine($Pen, 5.6, 13.2, 12.6, 6.2)
            $G.DrawLine($Pen, 4.2, 11.8, 5.6, 13.2)
            $G.FillPolygon($Brush, @(
                (New-Object System.Drawing.PointF(11.8, 4.2)),
                (New-Object System.Drawing.PointF(13.2, 2.8)),
                (New-Object System.Drawing.PointF(13.8, 4.4)),
                (New-Object System.Drawing.PointF(12.4, 5.8))
            ))
            $G.DrawLine($Pen, 2.6, 13.6, 8.0, 13.6)
        }
        'droplet' {
            # 屏幕取色器：一滴颜料
            $G.FillPolygon($Brush, @(
                (New-Object System.Drawing.PointF(8.0, 2.6)),
                (New-Object System.Drawing.PointF(10.6, 6.8)),
                (New-Object System.Drawing.PointF(12.2, 9.4)),
                (New-Object System.Drawing.PointF(8.0, 13.2)),
                (New-Object System.Drawing.PointF(3.8, 9.4)),
                (New-Object System.Drawing.PointF(5.4, 6.8))
            ))
        }
        'plug' {
            # 端口与进程：一个插头（两根针 + 插头体 + 一段线）
            $G.DrawLine($Pen, 5.2, 3.0, 5.2, 5.6)
            $G.DrawLine($Pen, 10.8, 3.0, 10.8, 5.6)
            $G.DrawRectangle($Pen, 4.2, 5.6, 7.6, 4.2)
            $G.DrawLine($Pen, 8.0, 9.8, 8.0, 12.2)
            $G.DrawArc($Pen, 5.6, 11.0, 4.8, 3.0, 0, 180)
        }
        default {
            $G.FillEllipse($Brush, 5.6, 5.6, 4.8, 4.8)
        }
    }
}

# 每个页签一个底色；危险按钮用红色
$tabColor = @{
    'common'    = '#2E74B5'
    'rightmenu' = '#1B9E74'
    'cleanup'   = '#D18A2A'
    'system'    = '#8E6FB0'
    'privacy'   = '#5348B0'
    'apps'      = '#5F8C2A'
    'mine'      = '#2A8C8C'
}
$dangerColor = '#C0504D'

# 按钮清单直接从 exe 里读，免得两处维护
# GUI subsystem exe: capture stdout through a redirected process, "& $exe list" yields nothing.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = 'list'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$proc = [System.Diagnostics.Process]::Start($psi)
$outTask = $proc.StandardOutput.ReadToEndAsync()
$proc.WaitForExit()
$listOut = $outTask.Result -split "`r?`n"

$items = @()
foreach ($line in $listOut) {
    if ($line -notmatch "`t") { continue }
    $parts = $line -split "`t"
    $danger = $false
    foreach ($p in $parts) { if ($p -eq 'danger') { $danger = $true } }
    $tab = ''
    foreach ($k in $tabColor.Keys) { if ($parts[1] -eq $k) { $tab = $k } }
    # list 的第二列是中文页签名，这里做个反查
    if ($tab -eq '') {
        $cn = @{ '常用设置' = 'common'; '右键增强' = 'rightmenu'; '清理优化' = 'cleanup'; '系统工具' = 'system'; '隐私设置' = 'privacy'; '应用管理' = 'apps'; '我的工具' = 'mine' }
        if ($cn.ContainsKey($parts[1])) { $tab = $cn[$parts[1]] }
    }
    $items += [pscustomobject]@{ Id = $parts[0]; Tab = $tab; Name = $parts[2]; Danger = $danger }
}

if ($items.Count -eq 0) { throw '没有从 exe 里读到按钮清单（先跑 build.ps1）' }
[void][System.IO.Directory]::CreateDirectory($outDir)

# 用户自己建的按钮（%LOCALAPPDATA%\mxx1-toolbox\tools.json）不该被画进仓库的 assets\icons\：
# 那是别人机器上的东西，提交进来只会污染仓库。先把它们的 id 读出来跳过。
#
# 同理还有 **bin-tools\ 里自动长出来的按钮**（src\ToolFolders.cs）：那是我这台机器上放了
# 什么工具就长什么按钮，id 还是从文件夹名推出来的 —— 2026-10-05 实测漏过一次：跑了
# Make-Icons 之后 assets\icons\mine.memreduct.png 被生成出来（那个 memreduct 文件夹是
# 我自己放在 bin-tools 里的）。所以这里连它们一起跳过（status 的 autoButton= 行报的就是）。
$userIds = @{}
$userJson = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\tools.json'
if (Test-Path -LiteralPath $userJson) {
    try {
        $u = Get-Content -LiteralPath $userJson -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($e in @($u)) { if ($e.id) { $userIds[[string]$e.id] = $true } }
        if ($u.tools) { foreach ($e in @($u.tools)) { if ($e.id) { $userIds[[string]$e.id] = $true } } }
    } catch {
        Write-Host ('note     : 读不了用户 tools.json（跳过）: ' + $_.Exception.Message)
    }
}
$autoIds = @{}
# 注意：Mxx1Toolbox.exe 是 winexe，PowerShell **不等待**它、`& $exe status` 也拿不到输出
# （docs\DESIGN.md 坑 20 就是这一条）—— 必须 Start-Process -Wait -RedirectStandardOutput。
$statusTmp = Join-Path $env:TEMP ('mxx1-icons-status-' + [guid]::NewGuid().ToString('N') + '.txt')
try {
    Start-Process -FilePath $exe -ArgumentList 'status' -Wait -WindowStyle Hidden -RedirectStandardOutput $statusTmp | Out-Null
    foreach ($line in @(Get-Content -LiteralPath $statusTmp -Encoding UTF8)) {
        if ($line -match '^autoButton=') {
            $f = ($line -split "`t")
            if ($f.Count -ge 2 -and $f[1].Trim().Length -gt 0) { $autoIds[$f[1].Trim()] = $true }
        }
    }
} catch {
    Write-Host ('note     : 读不了 status（跳不过 bin-tools 自动按钮）: ' + $_.Exception.Message)
} finally {
    if (Test-Path -LiteralPath $statusTmp) { Remove-Item -LiteralPath $statusTmp -Force -ErrorAction SilentlyContinue }
}
foreach ($k in $autoIds.Keys) { $userIds[$k] = $true }
if ($userIds.Count -gt 0) {
    Write-Host ('note     : 跳过 ' + $userIds.Count + ' 个用户 / bin-tools 自建按钮：' + (($userIds.Keys | Sort-Object) -join ' '))
}

$made = 0
$skipped = 0
foreach ($it in $items) {
    if ($userIds.ContainsKey($it.Id)) { $skipped++; continue }
    $colorHex = $tabColor[$it.Tab]
    if ($it.Danger) { $colorHex = $dangerColor }
    $color = [System.Drawing.ColorTranslator]::FromHtml($colorHex)
    $shape = Get-Shape -Id $it.Id -Name $it.Name
    if ($it.Danger) { $shape = 'exclamation' }

    $bmp = New-Object System.Drawing.Bitmap(16, 16)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $path = New-RoundedPath -Rect (New-Object System.Drawing.RectangleF(0.5, 0.5, 15, 15)) -Radius 4
    $bg = New-Object System.Drawing.SolidBrush($color)
    $g.FillPath($bg, $path)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 1.4)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    Draw-Shape -G $g -Shape $shape -Brush $white -Pen $pen

    $target = Join-Path $outDir ($it.Id + '.png')
    $bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)

    $pen.Dispose(); $white.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    $made++
}

Write-Host ('已生成 ' + $made + ' 个图标 → ' + $outDir + $(if ($skipped -gt 0) { '（跳过 ' + $skipped + ' 个用户按钮）' } else { '' }))
$byShape = @{}
foreach ($it in $items) {
    $s = Get-Shape -Id $it.Id -Name $it.Name
    if ($it.Danger) { $s = 'exclamation' }
    if (-not $byShape.ContainsKey($s)) { $byShape[$s] = 0 }
    $byShape[$s]++
}
Write-Host ('图形分布: ' + (($byShape.Keys | Sort-Object | ForEach-Object { $_ + '=' + $byShape[$_] }) -join '  '))
