# 更新记录

本文件记录每个版本改了什么。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [1.0.0] - 2026-10-04

第一个版本：多行多列按钮墙 + 「右键增强」真功能。

### 新增

- **主界面**：顶部五个页签（常用设置 / 右键增强 / 清理优化 / 系统工具 / 我的工具），
  四列自适应按钮网格，段与段之间画分隔线；窗口默认按最长按钮名算宽度（当前 640×700），可缩放。
- **61 个按钮**，全部由内嵌的 `tools\*.json` 驱动；用户层 `%LOCALAPPDATA%\mxx1-toolbox\tools.json`
  可以按 `id` 覆盖内置按钮，加按钮不用重新编译。
- **五种按钮类型**：`builtin`（内置功能）/ `exe` / `script`（ps1、cmd，可内联）/ `open`（网址、文件夹、
  `ms-settings:`）/ `macro`（P2）。
- **「右键增强」8 个真按钮**：安装 / 卸载 / 查看状态 / 测试一下 / 使用条款 / 引擎日志 / 版本与更新 / 关于作者，
  全部调用隔壁 `permanent-delete-menu` 的 `PermanentDeleteSetup.exe`，零改动集成
  （状态按钮能读到引擎真实的 `installed=` / `fileVisible=` 字段）。
- **主题**：浅色 / 深色 / 跟随系统，含 `DwmSetWindowAttribute` 深色标题栏与深色滚动条，
  跟着系统改主题会自动切（`WM_SETTINGCHANGE`）。
- **交互**：单击启动（可改成双击）、按住 Shift 或右键菜单提权、按钮右键菜单
  （运行 / 以管理员身份运行 / 打开所在文件夹 / 复制启动命令 / 查看按钮定义）、
  键盘走格子（方向键 + Enter、`Alt+1..9`）、`Ctrl+F` 搜索、`Ctrl+L` 日志、`F5` 刷新。
- **运行日志面板**（默认收起，**最新在最上面**）+ 独立日志窗口 + 编码感知读取（`FileShare.ReadWrite`）。
- **占位按钮反馈**：还没接功能的按钮点下去会在状态栏显示「功能待接入（P1）」并写一条日志，
  按钮灰 0.6 秒 —— 不会出现"点了没反应"。
- **危险按钮**：深红文字 + 执行前二次确认（关实时防护、关 UAC、Defender 开关、关内核隔离、
  禁用 SmartScreen、一键清理垃圾、清空回收站）。
- **命令行**：`list` / `run <id>` / `status` / `checkupdate` / `help`，退出码 0/1/2 明确，
  重定向输出按 UTF-8（中文不乱码）。
- **安全**：工具箱自身不联网（`checkupdate` 只读版本号）；界面不做任何改动系统的动作（P0 全部是占位）。

### 工程

- `build.ps1`：用系统自带 `csc.exe` 编译，自动给 `src\*.cs` / `tests\*.ps1` 补 UTF-8 BOM、
  去掉 `tools\*.json` 的 BOM，编译后自检五个 `tools.*.json` 与 61 个 `icons.*.png` 资源是否真的内嵌。
- `tools\Test-Encoding.ps1`：编码红线体检（`.cs`/`.ps1` 必须带 BOM、`.vbs` 纯 ASCII、
  `.md`/`.json` 不带 BOM、代码里不许硬编码本机绝对路径），`-Fix` 可自动修 BOM。
- `tools\Make-Screenshots.ps1`：用 `PrintWindow` 拍浅色 / 深色两张文档截图。
- `tools\Make-Icons.ps1`：按"页签定底色 + 按钮名里的关键词定图形"批量画 61 个 16×16 PNG 图标
  （纯 GDI+ 图元，不依赖字体），危险按钮统一红底感叹号；`build.ps1` 把它们以 `icons.<id>.png` 内嵌进 exe。
- 测试 **63 项**：命令行回归 26 + 界面回归 37（外加编码体检）。界面回归用 Win32 探针
  （枚举子窗口矩形判重叠、读 `GWL_STYLE`、`BM_CLICK` 真点、`WM_GETTEXT` 读文字），
  按钮清单从 `list` 读，不写死。

### 修掉的两个界面 bug（都被界面回归抓住）

- **每一行第 4 个按钮比同排宽 28px**：`Dock=Top` 的 `TableLayoutPanel` 会把多出来的宽度全给最后一列，
  改成末尾加一个占 100% 的空列吃掉余量。
- **底栏按钮下边缘被裁掉 5px**：状态栏那一行没设 `RowStyles`，行按内容撑到 30px 而容器只有 24px，
  改成行 `Percent 100` + 按钮固定高 20px。

### 已知限制 / 下一步

- 除「右键增强」外的按钮都还没接真功能（P1）；图形化「新建按钮」、拖拽新增、按钮排序、多步 `macro` 也在 P1。
- 工具箱自身的更新检查、插件目录、按钮包导入导出在 P2。
- 按钮图标：61 个由 `tools\Make-Icons.ps1` 生成的 16×16 PNG 已内嵌进 exe；
  想换成自己的图，把 PNG 放 `assets\icons\<id>.png`（或清单里写 `icon`）即可，代码优先用它们，
  都没有时才退回实时绘制的占位图标。
