<p align="center">
<img src="\Assets\Square44x44Logo.altform-lightunplated_targetsize-256.png" width="150" alt="Logo"/>
</p>

<div align="center">

# XAML Sub

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/releases)
[![GitHub Release Date](https://img.shields.io/github/release-date/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/releases)
[![GitHub All Releases](https://img.shields.io/github/downloads/MsintX/XamlSub/total?style=flat)](https://github.com/MsintX/XamlSub/releases)
[![GitHub stars](https://img.shields.io/github/stars/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/network/members)
[![GitHub issues](https://img.shields.io/github/issues/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/issues)
[![GitHub license](https://img.shields.io/github/license/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/blob/main/LICENSE)
[![GitHub last commit](https://img.shields.io/github/last-commit/MsintX/XamlSub?style=flat)](https://github.com/MsintX/XamlSub/commits)

`XAML Sub` 是一个独立于 Visual Studio 的轻量级 WinUI XAML 可视化编辑器。

</div>

---

## 目录

- [最新版本](#最新版本)
- [这是什么](#这是什么)
- [截图预览](#截图预览)
- [功能特性](#功能特性)
- [实验性：真实 WinUI 预览](#实验性真实-winui-预览)
- [系统要求](#系统要求)
- [安装与构建](#安装与构建)
- [快速上手](#快速上手)
- [界面说明](#界面说明)
- [快捷键与鼠标操作](#快捷键与鼠标操作)
- [XAML 支持边界](#xaml-支持边界)
- [C# 事件生成规则](#c-事件生成规则)
- [已知限制](#已知限制)
- [常见问题](#常见问题)
- [许可证](#许可证)
- [贡献与反馈](#贡献与反馈)

---

## 最新版本

当前最新版本为 **Release 2.0.0**。这一版的重点是让高保真 WinUI 预览成为默认设计预览，并继续完善编辑器的布局、控件缩放和常见交互。

- 高保真预览默认启用，取代原来的自绘预览路径。
- 更新应用图标与 Logo。
- 修复 FreedomPanel 中控件尺寸不随缩放变化的问题。
- 打开 `App.xaml` 时会提示并拒绝将其作为普通设计文档打开。
- 修复控件列表滚动、属性 ComboBox 默认选中等界面问题。

查看完整的 [更新日志](UpdateLog.md) 或前往 [GitHub Releases](https://github.com/MsintX/XamlSub/releases)。

---

## 这是什么

XAML Sub 是一个**独立运行的 WinUI 桌面应用**，用来可视化编辑磁盘上的 XAML 文件。

它不依赖 Visual Studio，也不是 VSIX 插件，而是通过“外部文件已修改”这一机制与 Visual Studio 协作：

```text
XAML Sub 保存到 .xaml / .cs
        ↓
Visual Studio 检测到外部修改
        ↓
选择“重新加载”即可同步
```

核心设计取舍：

· 从 Release 2.0.0 起，设计视图默认使用真实 WinUI 控件树进行高保真预览，替代原来的自绘预览。  
· 真实 WinUI 控件树负责布局与显示，独立 Overlay 负责选中、拖动、辅助线和放置预览，避免预览控件抢占设计器交互。  
· 以打开时的原始 XAML 为基线，尽量只修改需要修改的节点和属性。  
· 拖拽过程中主要操作内存模型；完成编辑后再按保存/自动保存策略写回磁盘。  
· 不支持的复杂 XAML 结构尽量保留，不静默删除或修改。  

---

## 截图预览
<p>主页</p>  
<p align="center">
<img src="\Assets\readme\MainPage.png" alt="MainPage"/>
</p>

<p>设置页</p>
<p align="center">
<img src="\Assets\readme\SetPage.png" alt="SetPage"/>
</p>
---

## 功能特性

### 设计画布

· Grid 网格绘制，单元格按 `*` 均分。  
· 默认使用真实 WinUI 控件树进行预览；编辑器交互由独立 Overlay 负责。  
· 控件选中、移动、删除。  
· Grid.Row / Grid.Column 落点计算，RowSpan / ColumnSpan 支持。  
· 拖拽时目标单元格高亮。  
· 单击空白区域选中 Window（窗口），单击控件选中控件。  
· Ctrl + 滚轮缩放设计视图（25% – 300%）。  
· 窗口标题栏预览，跟随 `Title` 属性实时更新。  
· 设计器 Overlay 与预览控件树分离，避免真实控件的命中测试抢占编辑操作。  

### 控件列表

· 30+ 常用 WinUI 控件。  
· 原生拖放：按住工具箱控件直接拖到画布，无需先点击再点击。  
· 覆盖控件包括：

```text
Button / HyperlinkButton / ToggleButton / RepeatButton
CheckBox / RadioButton / ToggleSwitch
TextBlock / TextBox / PasswordBox / AutoSuggestBox / NumberBox / RichEditBox
ComboBox / ListView / ListBox / TreeView / ItemsRepeater
Slider / ProgressBar / ProgressRing
Image / Icon / FontIcon
DatePicker / CalendarDatePicker / TimePicker / CalendarView
Expander / InfoBar / RatingControl / NavigationView / Pivot / TabView
ScrollViewer / CommandBar
```

### 属性面板

· 所有可编辑框均列出，根据当前选中控件类型动态启用/禁用。  
· 支持编辑 Window（窗口）属性与控件属性。  
· 属性面板在可用时会优先读取真实 WinUI 元素的当前值。  
· 覆盖属性（部分）：

```text
x:Name / Content / Text
Grid.Row / Grid.Column / RowSpan / ColumnSpan
Width / Height / MinWidth / MaxWidth / MinHeight / MaxHeight
Margin / Padding
HorizontalAlignment / VerticalAlignment
HorizontalContentAlignment / VerticalContentAlignment
FontSize / FontWeight / FontFamily / FontStyle / TextAlignment / CharacterSpacing
Foreground / Background / BorderBrush / BorderThickness / CornerRadius
Opacity / Visibility
IsEnabled / IsTabStop / IsHitTestVisible
ToolTip / PlaceholderText / Header
Source / Stretch / TextWrapping / MaxLength
SelectedIndex / Minimum / Maximum / Value
Orientation / IsChecked / IsOn
OnContent / OffContent / GroupName
IsEditable / AcceptsReturn / IsReadOnly
Command / CommandParameter
IsDefault / IsCancel / ClickMode
IsIndeterminate
DateFormat / NumberFormat / Language / Tag
```

· 单行输入框按 Enter 提交，焦点离开时也提交。  
· 多行输入框（如 TextBox 的 Content）保留 Enter 换行行为。  
· 属性术语后附带中文解释。  

### 文件与保存

· 打开单个 `.xaml` 文件。  
· 打开 `.csproj` 项目并从项目中选择 XAML（自动排除 App.xaml、bin/、obj/）。  
· 空白 XAML 文件自动提示创建默认 1 × 1 Grid。  
· 增量式 XAML 写回：复用已有节点，只修改变化部分。  
· 双文件事务写回（`.xaml` + `.cs`），失败自动回滚。  
· 当找不到明确关联的 code-behind 时，可选择**只保存当前 XAML**，避免被迫修改错误的 C# 文件。  
· 撤销 / 重做（最多 100 步）。  
· 自动保存开关位于设置页，并持久化保存。  
· Ctrl + S 先提交当前焦点编辑器，再执行完整保存。  

### 定位策略

设计器支持以下定位方式，并在设置中按策略显示对应工具：

```text
相对定位 → 自定义 Panel（FreedomPanel）
相对定位 → DockPanel
相对定位 → Grid
绝对坐标 → Canvas
绝对坐标 → Grid（Margin / 精确偏移）
```

FreedomPanel 支持自由相对定位，以及靠左/中/右、靠上/中/下等辅助操作。Release 1.2.0 起其定位逻辑调整为真正的相对定位；Release 2.0.0 进一步修复了控件尺寸不随画布缩放变化的问题。

### C# 事件生成

· 基于 Roslyn 修改 `.cs` 文件。  
· 修改 `x:Name` 时自动派生事件处理器。  
· Button 等控件生成 `Click`，无 Click 能力的控件生成 `Tapped`。  
· Window 根内容元素在保存过程中会确保存在 `Loaded` 处理器。  
· 用户手写的事件保持不变，不被自动改名覆盖。  

---

## 实验性：真实 WinUI 预览

从 **Release 2.0.0** 起，“高保真预览（编辑器覆盖层）”默认启用，并取代原来的自绘预览。该功能仍有一些设计器与目标项目运行时之间的能力边界。

设计画布采用两层结构：

```text
┌───────────────────────────────────────┐
│ NativePreviewHost                     │
│  └─ 真实 WinUI 控件树                 │
│                                       │
│ EditorOverlayCanvas                   │
│  └─ 选择框 / 拖动 / 辅助线 / 放置预览   │
└───────────────────────────────────────┘
```

真实控件树目前会按 XAML Sub 的设计模型创建常用 WinUI 控件，并使用实际 WinUI Measure/Arrange 与常用属性参与预览。编辑器自身的选中、拖动、辅助线等交互仍由 Overlay 负责；真实控件树不直接接管设计器命中测试。

这里的“真实控件”不等同于运行目标项目中的完整 XAML 运行时：对于 `ComboBox`、`ListView`、`TreeView`、`NavigationView`、`Pivot`、`TabView`、`CommandBar` 等复合/集合类控件，预览内容可能是设计器生成的占位项，而不是目标项目实际的模板、数据源或资源。

高保真预览适合检查真实 WinUI 控件的尺寸、排列与常见属性视觉效果；它并不等同于目标项目的完整运行时，正式开发或重要工程编辑仍应以项目自身运行结果和 Visual Studio 为准。

---

## 系统要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10 1809（17763）或更高 / Windows 11 |
| 运行时 | .NET 8 |
| UI 框架 | WinUI / Windows App SDK |
| 打包方式 | Single-project MSIX / Packaged（`EnableMsixTooling=true`） |
| 开发工具 | Visual Studio 2022/2026（含 Windows App SDK 工作负载） |
| 平台 | x64 |

当前项目目标为 `net8.0-windows10.0.19041.0`，并使用 Windows App SDK 2.5.1 与 Roslyn 5.9.0。

---

## 安装与构建

### 从源码构建

1. 克隆仓库：
   ```bash
   git clone https://github.com/MsintX/XamlSub.git
   cd XamlSub
   ```
2. 用 Visual Studio 打开 `XamlSub.sln`。  
3. 确认已安装 Windows App SDK 工作负载与 .NET 8 SDK。  
4. 选择 `x64` 平台配置。  
5. 生成解决方案：
   ```text
   Ctrl + Shift + B
   ```

项目启用了单项目 MSIX 打包。若进行正式打包/签名，请确保项目配置中的开发证书（PFX）在本机可用！

### 从安装包安装

1. 前往 [Release 界面](https://github.com/MsintX/XamlSub/releases)。  
2. 选择目标版本并打开附件列表。  
3. 仅按该版本 Release 附件说明处理签名证书。维护者在 2.0.0 发布说明中提到，历史 Release ZIP 曾出现误包含 `.pfx` 证书文件的问题；不要安装来源或用途未经确认的证书，并以 Releases 后续公告为准。  
4. 安装 MSIX 包。  

---

## 快速上手

### 场景一：从空白 XAML 开始

1. 在设置中选择一个合适的定位方式。  
2. 新建一个文本文档，重命名为任意合法名 `.xaml`（内容为空）。  
3. 点击工具栏“打开文件”，选择该文件。  
4. 弹出“XAML 文件为空”对话框后，点击“创建默认”布局。  
5. 设计器自动写入标准命名空间与布局，并进入设计视图。  

### 场景二：编辑现有 XAML

1. 点击“打开文件”选择 `.xaml`，或点击“打开项目”选择 `.csproj` 后从列表中选择 XAML。  
2. 左侧工具箱找到目标控件，按住直接拖到画布目标位置。  
3. 松手后控件被放置，并自动生成唯一 `x:Name`（如 `Button1`）。  
4. 单击控件选中，在右侧属性面板修改属性。  
5. 修改 `x:Name` 后，自动生成事件处理器并写入同名 `.xaml.cs`（如存在关联 code-behind）。  
6. 点击“保存”或按 `Ctrl + S`。  
7. 若找到可靠的 code-behind 关联，会执行 `.xaml` + `.cs` 事务写回；若无法建立关联，可改为只保存 XAML。  
8. 回到 Visual Studio，按 VS 提示重新加载外部修改。  

### 场景三：调整 Grid 布局

1. 左侧“网格（Grid）”区域使用 ＋行 / －行 / ＋列 / －列。  
2. 删除行/列前，设计器会检查是否有控件位于或跨越目标区域，若有则阻止并提示。  
3. 若 Grid 使用 Auto 或固定尺寸，行列编辑会被禁用，仅提供预览与控件编辑，避免静默改变原布局。  

### 场景四：使用高保真 WinUI 预览

1. Release 2.0.0 起，高保真预览默认启用。  
2. 打开 XAML 文件后，设计画布会使用真实 WinUI 控件树展示常见控件的布局与属性效果。  
3. 继续使用编辑器原有的选择、拖动、辅助线和属性编辑操作；这些交互由 Overlay 处理。  
4. 对复杂模板、绑定、资源或数据源，请以目标项目实际运行结果为准。  

---

## 界面说明

```text
┌──────────────────────────────────────────────────────────────────┐
│ 工具栏：打开文件 / 打开项目 / 撤销 / 重做 / 保存 / 重载 / 设置     │
├────────────┬─────────────────────────────────────┬────────────────┤
│ 工具箱      │ 设计画布                             │ 属性面板        │
│ Tip 提示    │ ┌───────────────────────────────┐ │ Window 属性     │
│ 控件列表    │ │ Native Preview（默认）        │ │ 控件属性        │
│             │ │ + Editor Overlay              │ │ 控件专属属性    │
│ 定位工具    │ │ + 辅助线 / 放置预览           │ │ 事件             │
│ 设计说明    │ └───────────────────────────────┘ │ 保存 / 删除      │
├────────────┴─────────────────────────────────────┴────────────────┤
│ 状态栏：文件状态 / Grid 尺寸 / 缩放 / 当前预览模式               │
└──────────────────────────────────────────────────────────────────┘
```

· 工具栏：文件操作、撤销重做、保存、重载、设置等。  
· 左侧：Tip 提示、控件列表（可拖拽）、定位工具与设计说明。  
· 中间：设计画布；当前默认由真实 WinUI 预览树与 Overlay 共同组成。  
· 右侧：属性面板，按 Window / 控件 / 专属属性 / 更多属性 / 事件分组。  
· 底部：状态栏，显示当前文件、Grid 尺寸、缩放比例及当前预览模式。  

---

## 快捷键与鼠标操作

| 操作 | 说明 |
|---|---|
| Ctrl + S | 提交当前编辑器并写回 XAML / C#；无可靠 code-behind 关联时可降级为只保存 XAML |
| Ctrl + 滚轮 | 缩放设计视图（25% – 300%） |
| Enter（单行输入框） | 提交当前属性 |
| Enter（多行输入框） | 换行 |
| Delete | 删除选中控件 |
| Esc | 取消放置 / 拖动 |
| 单击控件 | 选中控件 |
| 单击空白区域 | 选中 Window（窗口） |
| 右键画布 | 取消放置 / 拖动 |
| 工具箱按住拖动 | 原生拖放控件到画布 |

Window 的 `Loaded` 事件不再依赖“双击画布空白区域”的临时手势；在保存 Window 文档时，写回逻辑会根据根内容元素自动确保对应处理器存在。

提示（Tip）区域每 1 分钟随机切换一条使用建议。

---

## XAML 支持边界

### 明确支持 / 重点支持

· Grid（优先相对布局，单元格按 `*` 均分）。  
· 大多数相对定位和绝对坐标定位方法。  
· 常用 WinUI 控件（见控件列表）。  
· StackPanel 的有限处理。  
· 常用属性。  
· `x:Name`。  
· `Click` / `Tapped` / `Loaded` 事件。  
· `Grid.Row` / `Grid.Column` / `RowSpan` / `ColumnSpan`。  
· 增删行列。  
· Undo / Redo。  
· XAML / C# 写回。  
· `Grid.RowDefinitions` / `Grid.ColumnDefinitions` 属性元素解析。  
· 实验性真实 WinUI 预览与 Overlay 设计交互。  

### 不考虑 / 暂不完全支持

· 任意 XAML 语法。  
· 复杂 Binding。  
· Template。  
· Style。  
· ResourceDictionary。  
· 设计时属性。  
· 完整 Auto / 固定尺寸布局编辑。  
· 完整目标项目运行时语义。  
· VS 内部无缝同步。  

复杂 XAML 结构（例如模板、资源、部分嵌套容器及设计器未建模的结构）通常会被保留在原 XAML 中，但不进入当前可编辑视觉模型。真实 WinUI 预览也不会加载目标项目的完整资源、Binding、模板与运行时数据。

---

## C# 事件生成规则

| 控件类型 | 派生事件 |
|---|---|
| Button / HyperlinkButton / ToggleButton / RepeatButton | Click |
| 其他无 Click 能力的控件 | Tapped |
| Window 根内容元素 | Loaded |

规则说明：

· 修改 `x:Name` 时，若当前事件为自动生成，则同步更新事件处理器名称。  
· 若事件为用户手写，则保留不变，避免覆盖用户代码。  
· 清空 `x:Name` 时，自动生成的事件会被一并清除。  
· 当文档缺少明确 `x:Class` 但启用了“宽泛的保存检查”时，可尝试使用同目录同名 `.xaml.cs` 建立保存关联。  

---

## 已知限制

· XAML 写回仍使用 XML DOM 参与增量写回：
  · 原节点可尽量复用  
  · 注释、空白和无关结构会尽量保留  
  · 但不能保证所有词法细节逐字不变，属性引号、部分格式可能发生变化   
· 复杂 XAML 结构不进入视觉模型，仅保留  
· 真实 WinUI 预览是实验性实现，不是目标项目的完整运行时预览  
· 复合/集合型控件在高保真预览中可能使用设计器生成的示例内容，而不是实际 ItemsSource、模板或资源   
· 自动保存/手动保存仍依赖磁盘文件可写权限；XAML-only 兜底保存不会自动同步 C# 逻辑文件  
· 与 Visual Studio 的同步依赖 VS 自身的“外部文件已修改”提示，非无缝集成  

---

## 常见问题

**Q：为什么打开复杂 XAML 后，有些控件看不见？**  
A：设计器只把当前支持的控件和结构放入视觉模型。部分容器、模板、资源和绑定结构会保留在原 XAML 中，但不参与可视化编辑，避免误改。

**Q：为什么高保真预览里的列表内容和我的项目不一样？**  
A：实验预览创建的是编辑器控制的真实 WinUI 元素。集合类控件可能使用示例项，不加载目标项目真实数据源与运行时资源，因此它用于布局/属性检查，不等同于项目运行结果。

**Q：为什么 Grid 行列编辑按钮是灰的？**  
A：当前 Grid 使用了 Auto 或固定尺寸。设计器保持原布局，仅提供预览与控件编辑，避免静默改变布局。

**Q：保存后 Visual Studio 没有同步？**  
A：VS 通过“外部文件已修改”提示同步。请回到 VS，在提示中选择“重新加载”。设计器不直接操作 VS 进程。

**Q：保存时为什么提示只能保存 XAML？**  
A：设计器没有找到可靠的 code-behind 关联。此时可以只保存 XAML，但 XAML 中的事件与 C# 逻辑不会同步更新。

**Q：为什么空白 XAML 打开时提示创建 Grid？**  
A：0 字节的 `.xaml` 会导致 `Root element is missing`。设计器会询问是否创建默认 1 × 1 Grid 与标准命名空间，让设计器可以从空白文件开始工作。

**Q：自动保存开关有什么用？**  
A：开启后，拖放、属性修改、重命名等操作完成后会尝试立即保存到文件；保存仍受文件权限和 code-behind 关联状态影响。

**Q：删除行/列被阻止了怎么办？**  
A：目标区域仍有控件。请先移动或缩小这些控件，再删除行列。

**Q：手写的事件会被覆盖吗？**  
A：不会。设计器会识别用户手写的事件并保留，仅同步自动生成的事件。

---

## 许可证

本项目采用 Apache 2.0 License 发布。

---

## 贡献与反馈

· 欢迎在 GitHub 上提交 Issue 反馈 Bug 或提出建议。  
· 提交 PR 前请先确认本地可正常构建（x64 配置）。  
· 如果你喜欢这个工具，欢迎给个 Star ⭐

---

## 技术栈

· WinUI / Windows App SDK  
· .NET 8  
· Roslyn（C# 语法分析与代码编辑）  
· Single-project MSIX / Packaged  
