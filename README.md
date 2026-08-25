# CopyRecord

> 一款轻量、无依赖、注重隐私的 Windows 剪贴板历史管理器。

CopyRecord 常驻系统托盘，自动记录你在任意应用中复制的文本、图片和文件列表，并通过全局快捷键（默认 `Ctrl+Shift+V`）随时呼出搜索与粘贴。它完全离线运行，所有数据只保存在本机，**不联网、无遥测、无第三方运行时依赖**——单文件绿色运行。

---

## 目录

- [功能特性](#功能特性)
- [系统要求](#系统要求)
- [快速开始](#快速开始)
- [使用指南](#使用指南)
- [设置项说明](#设置项说明)
- [数据存储与隐私](#数据存储与隐私)
- [命令行参数](#命令行参数)
- [从源码构建](#从源码构建)
- [项目结构](#项目结构)
- [技术架构](#技术架构)
- [常见问题（FAQ）](#常见问题faq)
- [路线图](#路线图)
- [贡献指南](#贡献指南)
- [许可证](#许可证)

---

## 功能特性

### 剪贴板历史记录

| 类型 | 说明 |
| --- | --- |
| 文本 | 记录纯文本，同时保留 RTF / HTML 富文本格式，粘贴时原样还原 |
| 图片 | 自动保存为 PNG 文件；对 QQ / 微信截图等 alpha 通道异常的位图做兼容处理，保证缩略图可见 |
| 文件 / 文件夹 | 记录文件列表，支持一键打开、在资源管理器中显示所在位置 |

- 图片通过 SHA-256 哈希去重，同一张图反复复制不会产生冗余存储。
- 每条记录自动附带来源应用名、窗口标题与复制时间。

### 快速呼出

- 全局快捷键呼出主面板，默认 `Ctrl+Shift+V`，可在设置中改为 `Ctrl+Alt` / `Alt+Shift` 组合加任意字母或数字键。
- 面板始终置顶，跟随光标所在屏幕贴右下角弹出，不抢占焦点（无需再次点击即可继续打字）。

### 即时搜索

- 基于 SQLite FTS5 全文索引，中文按连续块分词，支持多关键词（空格分隔，取交集）。
- 无 FTS 索引时自动回退到 `LIKE` 模糊匹配，保证任何场景都能搜。

### 智能过滤

- 类型过滤：全部 / 文本 / 图片 / 文件 / 链接 / 代码。
- 来源过滤：只看某个应用（如 Excel、浏览器）复制过的内容，或只看收藏内容。

### 收藏夹

- 将常用条目一键收藏（`Ctrl+S` 或点星标），收藏内容在自动清理时**永不删除**。

### 连续复制队列

- 把多个条目（文本、图片、文件均可）加入"连续复制队列"。
- 选择"合并粘贴"时，多条文本按顺序合并成一条一次性粘贴；也可以选择逐条依次粘贴。
- 队列状态实时显示在主面板顶部，可随时从托盘菜单取消。

### 多行文本转表格

- 对多行文本自动检测分隔符（制表符 / 逗号 / 中文逗号 / 分号 / 竖线 / 连续空格 / 每行一列），并校验各行列数一致。
- 表格预览窗口确认后，以 **HTML 表格 + TSV 制表符文本双格式**写入剪贴板，粘贴到 Excel / WPS 即成为真实表格。
- 可开启"数字保留文本格式"，避免长数字（如身份证号、订单号）被科学计数法破坏。

### 失效文件重新定位

- 文件或文件夹被移动 / 删除后，条目会标记为"失效"。
- 右键选择"重新定位失效路径"，浏览选中新位置后立即恢复可用。

### 隐私保护

- 可开启"不记录疑似验证码、银行卡号和密码的单行文本"：
  - 纯数字（4–24 位，可含空格、`-`、`+`）自动跳过；
  - 长度 8–64、无空白、且同时包含大小写字母 / 数字 / 特殊字符中三类以上的高熵字符串自动跳过。
- 可配置排除列表，指定应用复制的内容一律不记录。
- 支持一键"暂停记录"，需要时随时恢复。
- 所有数据仅存本机，程序完全离线运行。

### 数据自管理

- 容量控制：最大历史条数（50–50000）、图片保留天数、图片空间上限（MB），三项均可配置。
- 空闲 30 分钟后自动清理过期数据；收藏内容与当前剪贴板内容受保护。

### 系统集成

- 可选开机自启（写入当前用户的注册表 Run 键）。
- 单实例运行，重复启动自动唤起已有实例。
- 托盘菜单：打开面板 / 暂停记录 / 设置 / 取消队列 / 清空未收藏历史 / 退出。
- Per-Monitor V2 DPI 感知、Windows 长路径支持、Win10 兼容清单。

---

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1803 及以上（含 Windows 11） |
| 运行时 | .NET Framework 4.8（Windows 10 / 11 已内置，无需安装） |
| 依赖 | 无任何第三方库；SQLite 通过系统内置的 `winsqlite3.dll` 驱动 |
| 构建环境（仅构建时需要） | PowerShell 5.1+、.NET Framework SDK 编译器（系统自带 `csc.exe`）；可选安装 .NET SDK 以启用 Roslyn 编译器回退 |

---

## 快速开始

### 方式一：直接使用发布版本

1. 在 [Releases](https://github.com/your-username/CopyRecord/releases) 页面下载 `CopyRecord.exe`。
2. 双击运行（也可放到任意目录，双击 `start-copyrecord.cmd` 启动）。
3. 按下 `Ctrl+Shift+V` 呼出面板，即可搜索 / 粘贴历史记录。

> 提示：程序为绿色单文件，无需安装，不写注册表（开机自启选项除外）。

### 方式二：从源码构建

见 [从源码构建](#从源码构建)。

---

## 使用指南

### 主面板快捷键

| 按键 | 功能 |
| --- | --- |
| `Ctrl+Shift+V`（可自定义） | 全局呼出 / 隐藏主面板 |
| `↑` / `↓` | 上下选择历史条目 |
| `双击条目` | 粘贴选中条目到之前的应用 |
| `Ctrl+Enter` | 打开表格预览（多行文本） |
| `Ctrl+S` | 收藏 / 取消收藏当前条目 |
| `Delete` | 删除当前条目 |
| `Esc` | 关闭面板 |

### 条目操作

每条记录右侧提供快捷按钮，不同类型略有差异：

- **文本**：`▦` 表格预览（仅多行文本显示）、`+` 加入连续复制队列、`删` 删除、`星标` 收藏。
- **文件**：右键菜单提供「打开」「打开所在位置」「重新定位失效路径…」「加入连续复制队列」「收藏」「删除」。
- **图片**：右键菜单提供「加入连续复制队列」「收藏」「删除」；缩略图即点即用。

### 搜索与过滤

1. 点击面板顶部搜索框直接输入关键词；多个关键词用空格分隔，取同时匹配的结果。
2. 使用「类型」下拉框筛选：全部 / 文本 / 图片 / 文件 / 链接 / 代码。
3. 使用「来源」下拉框查看特定应用复制过的内容；选择「收藏内容」只看收藏夹。

### 连续复制队列示例

1. 在文档 A 复制一段文字，在文档 B 复制一张截图，在文档 C 复制一个文件。
2. 呼出面板，分别点击这三条记录的 `+`，将它们加入队列。
3. 点击面板底部的「开始粘贴」：
   - 合并模式：三条文本按顺序合并成一条，一次性粘贴到目标位置；
   - 逐条模式：依次模拟粘贴每次复制的内容。
4. 随时可从托盘菜单「取消连续复制队列」中止。

### 表格粘贴示例

1. 复制一段带分隔符的多行文本（如 `Ctrl+C` 复制 Excel 中的多行多列数据）。
2. 呼出面板，选中该条，按 `Ctrl+Enter` 或点击 `▦`。
3. 在表格预览中核对行列，勾选"数字保留文本格式"（如需），点击「粘贴」。
4. 回到 Excel / WPS 直接 `Ctrl+V`，即粘贴为真实表格。

---

## 设置项说明

通过托盘菜单「设置…」或命令行参数 `--settings` 打开设置窗口。

| 分组 | 设置项 | 说明 | 默认值 |
| --- | --- | --- | --- |
| 常规 | 开机自动启动 | 写入当前用户注册表 Run 键 | 关 |
| 呼出快捷键 | 组合键 + 主键 | 组合键可选 `Ctrl+Shift` / `Ctrl+Alt` / `Alt+Shift`，主键为单个字母或数字；保存后立即生效 | `Ctrl+Shift+V` |
| 容量 | 最大历史条数 | 50 / 200 / 500 / 1000 / 5000 / 10000 / 50000，超出后自动清理最早的未收藏记录 | 5000 |
| 容量 | 图片保留天数 | 填 `0` 表示不按天数清理；收藏图片永不删除 | 0 |
| 容量 | 图片空间上限 | 单位 MB，超出后自动清理最早的未收藏图片 | 500 |
| 隐私 | 不记录敏感文本 | 忽略疑似验证码、银行卡号、密码的单行文本 | 关 |
| 隐私 | 不记录这些应用 | 进程名列表，逗号 / 分号 / 换行分隔，无需写 `.exe`，大小写不敏感 | 空 |

---

## 数据存储与隐私

### 数据目录

所有数据默认存放于：

```
%LOCALAPPDATA%\CopyRecord\
├── copyrecord.db     # 历史记录主库（SQLite，WAL 模式 + FTS5 全文索引）
├── images\           # 复制的图片文件（PNG）
└── settings.xml      # 程序设置
```

- 数据库使用 **WAL（Write-Ahead Logging）** 模式，崩溃恢复能力强、并发读写安全。
- 支持通过环境变量 `COPYRECORD_DATA_DIR` 将数据目录重定向到其他位置（便携模式）。
- 首次运行会自动迁移旧版 `history.xml` 数据。

### 隐私承诺

- 完全离线：不含任何网络功能，不收集、不上传任何数据。
- 敏感内容保护：可开启敏感文本过滤与应用排除。
- 数据仅归属于当前 Windows 用户目录，卸载 / 删除程序后可在 `%LOCALAPPDATA%\CopyRecord` 手动清除残留数据。

---

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--show` | 显示主面板（配合开机自启 / 快捷方式使用） |
| `--settings` | 直接打开设置窗口 |

示例：`CopyRecord.exe --show`、`CopyRecord.exe --settings`。

---

## 从源码构建

### 环境准备

- Windows 10 / 11。
- PowerShell（Windows 自带）。
- .NET Framework 4.x 的 C# 编译器（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，Windows 自带）。
- （推荐）安装 [.NET SDK](https://dotnet.microsoft.com/download)，用于在系统编译器异常时自动回退到 Roslyn。

### 构建步骤

```powershell
# 在仓库根目录执行
.\build.ps1

# 构建并立即运行
.\build.ps1 -Run
```

构建产物输出到 `dist\CopyRecord.exe`。

### 构建说明

- 纯命令行编译，**不依赖 NuGet 包**，所有源码直接交给 `csc.exe` 编译。
- 编译器优先使用系统 `csc.exe`；若不可用则自动回退到 .NET SDK 自带的 Roslyn（`dotnet csc.dll`）。
- 通过响应文件（`.rsp`）传参，避免命令行长度限制。
- 应用图标以 Base64 形式内嵌于 `AppIcon.cs`，构建时自动提取并作为 `/win32icon` 写入 exe。
- 编译参数：`/target:winexe`、`/platform:anycpu`、`/optimize+`。

---

## 项目结构

```
CopyRecord/
├── build.ps1                      # 构建脚本（含 Roslyn 回退、图标内嵌）
├── start-copyrecord.cmd           # 一键启动脚本
├── README.md
├── LICENSE                         # 开源许可证（发布前请添加）
├── src/
│   └── CopyRecord/                # 全部源码（C#，纯代码构建 UI，无 XAML）
│       ├── Program.cs             # 入口：单实例、命令行参数、全局异常处理
│       ├── MainWindow.cs          # 主面板 / 托盘 / 剪贴板监听 / 快捷键（核心）
│       ├── ClipboardItem.cs       # 剪贴板记录数据模型
│       ├── ClipboardCapturePolicy.cs # 隐私捕获策略（敏感文本 / 排除应用）
│       ├── HistoryStore.cs        # 历史存储：SQLite + FTS5、容量清理、旧数据迁移
│       ├── SqliteDatabase.cs      # SQLite P/Invoke 封装（调用系统 winsqlite3.dll）
│       ├── AppSettings.cs         # 设置模型与 SettingsStore（含开机自启）
│       ├── SettingsWindow.cs      # 设置窗口
│       ├── PasteQueue.cs          # 连续复制队列
│       ├── StorageWorkQueue.cs    # 后台异步存储工作队列
│       ├── TableTextConverter.cs  # 表格文本分隔符检测与转换
│       ├── TablePreviewWindow.cs  # 表格预览与粘贴窗口
│       ├── NativeMethods.cs       # P/Invoke 声明（Win32 API）
│       ├── AppIcon.cs             # 应用图标（Base64 内嵌，随 exe 分发）
│       └── app.manifest           # 清单：PerMonitorV2 DPI / 长路径 / Win10 兼容
└── dist/
    └── CopyRecord.exe             # 构建产物
```

---

## 技术架构

```
                 ┌─────────────────────────────┐
                 │   Windows 剪贴板 (Clipboard)  │
                 └──────────────┬──────────────┘
                                │ AddClipboardFormatListener 监听
                 ┌──────────────▼──────────────┐
                 │   ClipboardCapturePolicy    │  隐私过滤：排除应用 / 敏感文本
                 └──────────────┬──────────────┘
                 ┌──────────────▼──────────────┐
                 │      StorageWorkQueue       │  后台线程异步写入，不阻塞 UI
                 └──────────────┬──────────────┘
                 ┌──────────────▼──────────────┐
                 │         HistoryStore        │  SQLite (WAL) + FTS5 全文索引
                 │  copyrecord.db / images/    │  容量管理 / 图片 SHA-256 去重
                 └──────────────┬──────────────┘
                                │
                 ┌──────────────▼──────────────┐
                 │   MainWindow（WPF 主面板）    │  搜索 / 过滤 / 收藏 / 队列 / 表格
                 │   NotifyIcon（托盘）          │  全局快捷键 RegisterHotKey
                 └─────────────────────────────┘
```

### 设计要点

- **零第三方依赖**：SQLite 直接通过 `winsqlite3.dll`（Windows 10 1803+ 内置）以 P/Invoke 调用，无需打包原生库；UI 使用 WPF 纯代码构建（无 XAML），编译产物为单个 exe。
- **流畅不阻塞**：剪贴板捕获、图片存储、数据库写入均通过 `StorageWorkQueue` 放入后台队列，保证主界面始终响应。
- **搜索与容量兼顾**：FTS5 提供毫秒级全文检索；容量策略按"条数 / 天数 / 空间"三维控制，收藏内容始终豁免清理。
- **粘贴还原保真**：文本条目保存原始 RTF / HTML，粘贴时优先还原格式；也支持"复制为纯文本"。
- **异常兼容**：针对 QQ / 微信截图 alpha 通道损坏、大图缩略等实际场景做了专门处理。

---

## 常见问题（FAQ）

**Q：按快捷键没有反应？**
A：先确认程序已在托盘运行；再检查设置中的快捷键是否与其他软件冲突（如输入法、截图工具），可改为 `Ctrl+Alt` / `Alt+Shift` 组合。

**Q：为什么某些验证码 / 密码没有被记录？**
A：这是隐私过滤生效的表现。若你希望记录这类内容，请在设置中关闭"不记录疑似验证码、银行卡号和密码的单行文本"。

**Q：数据库 / 图片保存在哪里？**
A：默认在 `%LOCALAPPDATA%\CopyRecord\`。如需迁移到其他位置，可设置环境变量 `COPYRECORD_DATA_DIR` 指向新目录。

**Q：历史记录什么时候会被清理？**
A：程序空闲 30 分钟后触发自动清理，按你设置的最大条数、图片保留天数、图片空间上限执行；收藏的内容不会被清理。

**Q：复制的内容在其它电脑上能看到吗？**
A：不能。所有数据仅保存在本机，程序没有任何联网能力。

**Q：粘贴后为什么还原了格式？**
A：CopyRecord 保留了复制时的富文本格式（RTF/HTML）。如需纯文本，请使用"复制为纯文本"相关操作。

**Q：表格粘贴到 Excel 变成了一行？**
A：请确认原始文本确实存在一致的分隔符（如 Tab），并在表格预览中核对列数。若数据本身无分隔符，无法自动分列。

---

## 路线图

- [x] 剪贴板历史（文本 / 图片 / 文件）
- [x] 全局快捷键呼出
- [x] FTS5 全文搜索与类型 / 来源过滤
- [x] 收藏与容量自动管理
- [x] 连续复制队列
- [x] 多行文本转表格粘贴
- [x] 隐私过滤（敏感文本 / 排除应用）
- [ ] 历史记录导入 / 导出
- [ ] 多语言界面（英文等）
- [ ] 自定义界面主题

> 欢迎提交 issue / PR 补充更多想法。

---

## 贡献指南

欢迎任何形式的贡献：报告 Bug、提出新功能、改进文档、提交代码。

1. Fork 本仓库并创建特性分支。
2. 遵循现有代码风格（C#、代码托管 WPF、无第三方依赖）。
3. 提交前请确保 `.\build.ps1` 构建通过。
4. 通过 Pull Request 提交，并简要说明改动内容。

> 开发约定：本项目刻意不引入 NuGet 依赖与 XAML，以保持"零依赖、单文件"的绿色属性。新增功能请优先使用 .NET BCL 与系统组件实现。

---

## 许可证

本项目使用 [MIT License](LICENSE) 开源。

```
MIT License

Copyright (c) 2026 CopyRecord contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

**CopyRecord** · 记录你复制过的每一段内容。
