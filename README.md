# 屏幕日文翻译器（ScreenTransJa）

框选屏幕上的日文 → 多模态大模型识别 → 翻成中文 → 悬浮窗显示。

一个仿「团子翻译器」思路的轻量替代品：**单个 exe、不依赖 Python/Node、Win7 也能跑**。

玩windows老日文游戏的老友们有福了，vc了一晚上终于完成了！希望友友们在此基础上继续扩充功能！

---

## 功能特点

### 截屏翻译

- **`F8` 框选**：屏幕冻结成截图，拖拽框选日文区域，松开立刻识别翻译
- **自动跟随**：框选一次后持续监听，画面里的**文字**变化才重新翻译
  - 本地检测，不联网：灰度 + Sobel 边缘 + 帧差，只认文字笔画变化
  - 背景渐变、粒子动画、光标闪烁都不会误触发
- **重选**：底栏「重选」按钮，不用再按热键

### 模型

- **OpenAI 兼容接口**：通义千问 DashScope、智谱、OpenAI 或任意兼容服务
- **单模型 / 双模型**：
  - 单模型：一次请求同时返回日文原文 + 中文译文
  - 双模型：视觉模型只识别日文，纯文本模型负责翻译（更省 token）
- **Qwen-MT 专用翻译模型**：支持 `translation_options`（源/目标语言）
- **模型列表**：点「获取列表」从 `/models` 拉取，自动筛出视觉模型，**列表内显示参考价**
- 自动过滤语音合成/识别、实时语音翻译、图像生成等不能做文本翻译的模型

### 译文框

- 可拖动、右下角拖拽缩放、尺寸记忆
- 原文/译文均可鼠标选中、`Ctrl+C` 复制
- 原文默认折叠，点「▶ 原文」展开
- 字号 `A-` / `A+` 实时调整
- 底栏切换模型，选完立刻用新模型重译
- 顶栏「暂停 / 继续」自动跟随（`F9`）
- 顶栏「隐藏」一键收起译文框 + 识别框
- 加载时显示进度条 + 骨架动画，不闪文字

### 其它

- **识别框外轮廓**：青色细边标出识别范围，鼠标穿透，不挡操作
- **全局热键**：`F8` 框选 / `F9` 暂停跟随 / `F10` 重译
- **格式保留**：逐行翻译、行首符号（`「」『』`、`・`、`※`、编号）尽量与原文一致
- 托盘常驻、单实例、配置持久化
- 命令行：`--rect x,y,w,h` 直译固定区域，`--settings` 直接打开设置

---

## 支持的操作系统

| 系统 | 支持 | 说明 |
|---|---|---|
| Windows 10 / 11 x64 | ✅ 推荐 | .NET 4.8 通常已自带 |
| Windows 8 / 8.1 x64 | ✅ | 需 .NET 4.8 |
| Windows 7 SP1 x64 | ✅ | 需手动安装 .NET 4.8；窗口半透明不可用 |

- 仅 **64 位**（编译为 AnyCPU，64 位系统运行）
- 依赖 **.NET Framework 4.8**，不需要 Python / Node / 任何运行时安装包

> Win7 实测：分层窗口（Opacity）在虚拟机显卡下失效，所以译文框固定为不透明深色，避免「窗口存在但完全看不见」。

---

## 前置条件

1. 一台 Windows x64 电脑（Win7 SP1 及以上）
2. 已安装 **.NET Framework 4.8**
   - Win10 1809+ / Win11 自带
   - Win7/8 需从微软官网下载安装
3. 一个**支持图片输入的大模型 API Key**（二选一或都配）

   推荐（阿里云 DashScope 兼容模式）：

   | 用途 | 模型 | 说明 |
   |---|---|---|
   | 识别 | `qwen3-vl-plus`、`qwen3.8-omni-flash` | 看图读日文 |
   | 翻译 | `qwen-mt-plus`、`deepseek-v4-pro` | 纯文本翻译质量更好 |

   也支持智谱 `glm-4v-flash`、OpenAI `gpt-4o-mini` 等 OpenAI 兼容服务。

---

## 快速开始

1. 下载 Release 里的 `ScreenTransJa.exe`（或自行构建，见下）
2. 双击运行，首次会弹出设置窗口
3. 填「接口地址」「API Key」「模型」，点「测试连接」确认可用
4. 按 **`F8`**，拖拽框选日文区域，松开即翻译
5. 想持续跟随剧情：保持「自动跟随」开启（绿点表示运行中）

> 设置页「获取列表」可以直接从服务端拉取可用模型，并显示参考价格。

---

## 构建

需要系统自带的 .NET 编译器（Win7+ 自带 `csc.exe`），无需 Visual Studio：

```bat
build.bat
```

产物：`dist\ScreenTransJa.exe`

手动命令：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /warn:0 /codepage:65001 ^
  /target:winexe /win32icon:app.ico /out:dist\ScreenTransJa.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:System.Net.Http.dll /r:System.Core.dll ^
  src\Config.cs src\Translator.cs src\OverlayForm.cs src\SelectionFrame.cs ^
  src\ResultForm.cs src\SettingsForm.cs src\ChangeDetector.cs src\Pricing.cs src\Program.cs
```

> `csc` 在中文系统上只要有警告输出就可能崩溃，所以必须带 `/warn:0`。

---

## 使用说明

| 操作 | 说明 |
|---|---|
| `F8`（可改） | 框选翻译 |
| `F9` | 暂停 / 继续自动跟随 |
| `F10` | 重译上次区域 |
| 托盘双击 | 同 `F8` |
| 托盘右键 | 开始翻译 / 自动跟随 / 重译 / 显示译文框 / 显示识别框 / 设置 / 退出 |

### 译文框按钮

| 位置 | 功能 |
|---|---|
| 左上角圆点 | 绿色呼吸 = 运行中，橙色静止 = 暂停 |
| 「暂停 F9 / 继续 F9」 | 切换自动跟随 |
| 「复制」 | 复制选中文本；无选中则复制全文 |
| 「重译 F10」 | 重新截取同一区域翻译 |
| 「隐藏」 | 同时收起译文框和识别框 |
| 「设置」/「×」 | 打开设置 / 隐藏译文框 |
| 底栏模型下拉 | 切换模型，立即重译 |
| 底栏「重选」 | 重新框选区域 |
| 底栏「A- / A+」 | 调整译文字号 |
| 右下角斜纹 | 拖拽缩放窗口 |

### 单模型 vs 双模型

| | 单模型 | 双模型 |
|---|---|---|
| 请求 | 1 次（图 → 原文 + 译文） | 2 次（图 → 日文 → 中文） |
| 优点 | 快、有画面语境 | 翻译走低价文本模型，高频跟随更省 |
| 缺点 | 翻译也按视觉模型计价 | 多一次往返 |

在设置里勾选「分两步：视觉模型只识别日文 → 文本模型翻译」即可切换。

---

## 配置

配置文件：`%APPDATA%\ScreenTransJa\config.json`

常用字段：

| 字段 | 说明 |
|---|---|
| `ApiBase` / `ApiKey` | 接口地址与密钥 |
| `Model` | 单模型模式下的模型 / 双模型的识别回退 |
| `SplitModel` | 是否启用双模型 |
| `OcrModel` / `TransModel` | 双模型时的识别、翻译模型 |
| `Hotkey` | 框选热键（F7~F12 或 Ctrl+Alt+X） |
| `AutoMode` / `AutoIntervalSec` | 自动跟随开关与间隔 |
| `ChangeSensitivity` | 变化灵敏度 0 低 / 1 中 / 2 高 |
| `Prompt` | 单模型模式的提示词 |

删掉该文件即恢复默认。

---

## 已知限制

- **Win7 无半透明**：分层窗口在部分虚拟机显卡下失效，译文框固定不透明
- **Qwen-MT 会合并多行**：它是段落级翻译模型；看重逐行对应请用 `deepseek-v4-pro`
- **依赖远程模型**：识别/翻译都需要联网，没有本地 OCR 离线模式
- **识别精度受截图影响**：框选边缘切掉半个字会认错，程序已自动外扩 3px，框选时仍建议留余量
- 仅 Windows，未做跨平台

---

## 目录结构

```
.
├── build.bat              # 一键构建
├── app.ico                # 程序图标
├── README.md
├── LICENSE
└── src/
    ├── Program.cs         # 入口、热键、托盘、截图调度
    ├── OverlayForm.cs     # 冻结屏幕 + 拖拽框选
    ├── SelectionFrame.cs  # 识别区域外轮廓（鼠标穿透）
    ├── ResultForm.cs      # 悬浮译文框
    ├── SettingsForm.cs    # 设置界面（模型列表 + 价格）
    ├── Translator.cs      # OpenAI 兼容调用、单/双模型、Qwen-MT
    ├── ChangeDetector.cs  # 本地文字变化检测（边缘 + 帧差）
    ├── Pricing.cs         # 模型参考价
    └── Config.cs          # 配置与预设
```

---

## 技术说明

- 纯 **C# / WinForms**，无第三方依赖，编译产物约 80 KB
- 变化检测：160×90 灰度 → Sobel 边缘 → 相邻帧差 → 变化像素占比阈值
- 多模态请求走 OpenAI 兼容 `/chat/completions`，图片以 base64 `data:` URL 传入
- Qwen-MT 走专用 `translation_options`，且不附加任何指令（否则会被当正文翻译）

---

## 许可证

[MIT](LICENSE)
