# 萌新工具箱（Mxx1Toolbox）

一个免安装的单文件 Windows 程序：**主界面是多行多列的小按钮墙，点一下按钮就启动一个已经做好的程序、脚本或功能。**

![界面](docs/gui-shot.png)

深色主题：

![深色主题](docs/dark-shot.png)

- 单文件 `Mxx1Toolbox.exe`（约 106 KB，含 61 个内嵌图标），不需要 .NET SDK，不写注册表、不加开机启动
- 五个页签：**常用设置 / 右键增强 / 清理优化 / 系统工具 / 我的工具**，一共 61 个按钮
- 按钮全部由 `tools\*.json` 定义 —— **加按钮不用重新编译**
- 图标：61 个 16×16 PNG（按页签配色 + 按名字选图形，`tools\Make-Icons.ps1` 一键重生成），
  编译时内嵌进 exe；`assets\icons\<id>.png` 或清单里的 `icon` 字段可以覆盖
- 浅色 / 深色 / 跟随系统三种主题，标题栏也跟着变
- 「右键增强」页签是真功能：调用隔壁的 [永久删除（不进回收站）](../permanent-delete-menu) 安装器，零改动集成
- 危险按钮深红文字 + 二次确认；占位按钮点了会有反馈（状态栏 + 日志），不会"点了没反应"

## 快速开始

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1     # 编译，产物 bin\Mxx1Toolbox.exe
.\bin\Mxx1Toolbox.exe                                   # 不带参数 = 打开界面
```

## 界面怎么用

| 操作 | 结果 |
| --- | --- |
| 左键单击按钮 | 执行（占位按钮显示「功能待接入（P1）」并写日志，按钮灰 0.6 秒表示点到了；**运行中按钮文字不变**，只有图标变成转圈） |
| 按住 Shift 单击 / 右键「以管理员身份运行」 | 提权执行（UAC） |
| 右键按钮 | 运行 / 以管理员身份运行 / 打开所在文件夹 / 复制启动命令 / 查看按钮定义 |
| 底部 `[搜索]`、`[日志]` | 展开搜索框 / 运行日志面板（日志**最新的在最上面**） |
| `Ctrl+F` / `Ctrl+L` / `F5` / `Ctrl+,` | 搜索 / 日志 / 刷新按钮清单 / 设置 |
| 方向键 + `Enter` | 键盘走格子并执行；`Alt+1..9` 直接执行本页前九个 |

## 页签与按钮

| 页签 | 数量 | 性质 |
| --- | --- | --- |
| 常用设置 | 32 | 占位（任务栏 / 开始菜单 / 资源管理器 / 激活 / Defender / 更新 / UAC / 电源 / hosts…） |
| 右键增强 | 8 | **真功能**，调隔壁 `PermanentDeleteSetup.exe` |
| 清理优化 | 8 | 占位（一键清理垃圾 / 清空回收站 / 磁盘清理…） |
| 系统工具 | 12 | 占位（设备管理器 / 注册表 / 服务 / 组策略 / 任务管理器…） |
| 我的工具 | 1 | `[+ 新建按钮]`（图形化新建在 P1） |

**P0 的边界**：除了「右键增强」，其它按钮目前只做外观 + 占位反馈，**不会改动系统**。
清单的正本在 [`docs/DESIGN.md`](docs/DESIGN.md) §4。

## 加一个按钮

1. 编辑 `%LOCALAPPDATA%\mxx1-toolbox\tools.json`（用户层，升级不冲掉；同 `id` 覆盖内置），
   或者往仓库的 `tools\*.json` 里加（内置按钮，要重新编译才会内嵌进 exe）；
2. 编码必须是 **UTF-8 无 BOM**；
3. 五种按钮类型：

```json
{
  "tools": [
    { "id": "mytool.notepad", "tab": "mine", "segment": 1, "order": 10,
      "name": "记事本", "kind": "exe",
      "path": "%SystemRoot%\\system32\\notepad.exe", "args": "" },

    { "id": "mytool.folder", "tab": "mine", "segment": 1, "order": 20,
      "name": "打开下载目录", "kind": "open", "target": "%USERPROFILE%\\Downloads" },

    { "id": "mytool.script", "tab": "mine", "segment": 1, "order": 30,
      "name": "刷新 DNS", "kind": "script", "shell": "cmd",
      "inline": "ipconfig /flushdns", "runAsAdmin": true },

    { "id": "mytool.pending", "tab": "mine", "segment": 1, "order": 40,
      "name": "还没做的功能", "kind": "builtin", "placeholder": true, "hint": "P1" }
  ]
}
```

`tab` 可写 id（`common` / `rightmenu` / `cleanup` / `system` / `mine`）或中文页签名；
`segment` 决定它落在第几段（段与段之间画一条分隔线）；`order` 是段内顺序；`danger: true` 让文字变深红并强制二次确认。

## 命令行

```powershell
bin\Mxx1Toolbox.exe list [--tab rightmenu]   # 列出按钮（tab 分隔：id / 页签 / 名称 / 类型）
bin\Mxx1Toolbox.exe run permdel.status       # 执行一个按钮（和界面同一条路径）
bin\Mxx1Toolbox.exe status                   # key=value 状态（版本 / 按钮数 / 主题 / 日志路径 / 隔壁 exe 路径）
bin\Mxx1Toolbox.exe checkupdate              # 只读版本号，不下载不替换
bin\Mxx1Toolbox.exe help
```

## 测试

```powershell
powershell -File tools\Test-Encoding.ps1     # 编码红线体检（BOM / 纯 ASCII / 硬编码本机路径）
powershell -File tests\Test-All.ps1          # 全部（无桌面时加 -SkipGui）
powershell -File tests\Test-Cli.ps1          # 命令行回归 26 项
powershell -File tests\Test-Gui.ps1          # 界面回归 47 项（要交互式桌面，无桌面返回 3 = 跳过）
powershell -File tools\Make-Screenshots.ps1  # 重新拍 docs 里的截图
powershell -File tools\Make-Icons.ps1        # 重新生成 61 个 16x16 PNG 图标
```

界面回归不看截图：用 Win32 枚举子窗口矩形判"按钮/标签有没有压在一起"、读 `GWL_STYLE` 判标题栏、
`PostMessage(BM_CLICK)` 真点按钮、`WM_GETTEXT` 跨进程读文字；按钮清单从 `list` 里读，两边必须一致。
"文字有没有被裁 / 图标有没有上下居中"这类只有渲染结果能判断的问题，用 `PrintWindow` 抓像素、
按行数墨迹判定（底栏文字 10 行不能少，图标中心与按钮中心之差 ≤ 1px）。

## 文件位置

| 位置 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\mxx1-toolbox\settings.ini` | 主题 / 启动方式 / 日志保留 / 二次确认 / 永久删除安装器路径 |
| `%LOCALAPPDATA%\mxx1-toolbox\tools.json` | 你自己加的按钮 |
| `%LOCALAPPDATA%\mxx1-toolbox\logs\toolbox-YYYY-MM-DD.log` | 运行日志 |

## 作者与许可

作者 **mxx1** · [mxx1.cn](https://mxx1.cn)　许可证 **GPL-3.0-or-later**（见 [`LICENSE`](LICENSE)）。
按钮配置格式与界面规格见 [`docs/DESIGN.md`](docs/DESIGN.md)，改动记录见 [`CHANGELOG.md`](CHANGELOG.md)。
给 AI 助手看的开发约定在 [`skill/mxx1-toolbox/SKILL.md`](skill/mxx1-toolbox/SKILL.md)。
