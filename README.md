<p align="center">
<img src="\Assets\Square44x44Logo.altform-lightunplated_targetsize-256.png" width="150" alt="Logo"/>
</p>

<div align="center">

# XAML Sub

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/MsintX/XamlSub)](https://github.com/MsintX/XamlSub/releases) ![GitHub Release Date](https://img.shields.io/github/release-date/MsintX/XamlSub) 
![GitHub All Releases](https://img.shields.io/github/downloads/MsintX/XamlSub/total) 
![GitHub stars](https://img.shields.io/github/stars/MsintX/XamlSub?style=flat) 
![GitHub forks](https://img.shields.io/github/forks/MsintX/XamlSub)
![GitHub issues](https://img.shields.io/github/issues/MsintX/XamlSub)
![GitHub license](https://img.shields.io/github/license/MsintX/XamlSub)
![GitHub last commit](https://img.shields.io/github/last-commit/MsintX/XamlSub)

`XAML Sub`是一个独立于 Visual Studio 的轻量级 XAML 可视化编辑器。

</div>

---

## 目录

- [这是什么](#这是什么)
- [截图预览](#截图预览)
- [功能特性](#功能特性)
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

## 这是什么

XAML Sub 是一个**独立运行的 WinUI 桌面应用**，用来可视化编辑磁盘上的 XAML 文件。  

它不依赖 Visual Studio，也不是 VSIX 插件，而是通过「外部文件已修改」这一机制与 Visual Studio 协作：  

```text
XAML Sub 保存到 .xaml / .cs
        ↓
Visual Studio 检测到外部修改
        ↓
选择「重新加载」即可同步
```

核心设计取舍：  

· 自绘假控件，不运行真实 WinUI 控件模板，不追求高保真设计时渲染。  
· 以打开时的原始 XAML 为基线，尽量只修改需要修改的节点和属性。  
· 拖拽过程中只操作内存模型，松手后才更新模型并写回磁盘。  
· 不支持的复杂 XAML 结构尽量保留，不静默删除或修改。  

---
## 截图预览  
<p align="center">
<img src="\Assets\readme\MainPage.png" alt="MainPage"/>
</p>
<p>主页</p>  

<p align="center">
<img src="\Assets\readme\SetPage.png" alt="SetPage"/>
</p>
<p>设置页</p>
---

## 功能特性

设计画布  

· Grid 网格绘制，单元格按 * 均分  
· 自绘 Fluent 线框式控件预览（接近 WinUI 观感，但不是真实控件）  
· 控件选中、移动、删除  
· Grid.Row / Grid.Column 落点计算，RowSpan / ColumnSpan 支持  
· 拖拽时目标单元格高亮  
· 单击空白区域选中 Window（窗口），单击控件选中控件  
· Ctrl + 滚轮 缩放设计视图（25% – 300%）  
· 窗口标题栏预览，跟随 Title 属性实时更新  

控件列表  

· 30+ 常用 WinUI 控件
· 原生拖放：按住工具箱控件直接拖到画布，无需先点击再点击  
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

属性面板  

· 所有可编辑框全部列出，根据当前选中控件类型动态启用/禁用  
· 支持编辑 Window（窗口）属性与控件属性  
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

· 单行输入框按 Enter 提交，焦点离开时也提交  
· 多行输入框（如 TextBox 的 Content）保留 Enter 换行行为  
· 属性术语后附带中文解释  

文件与保存  

· 打开单个 .xaml 文件  
· 打开 .csproj 项目并从项目中选择 XAML（自动排除 App.xaml、bin/、obj/）  
· 空白 XAML 文件自动提示创建默认 1 × 1 Grid  
· 增量式 XAML 写回：复用已有节点，只修改变化部分  
· 双文件事务写回（.xaml + .cs），失败自动回滚  
· 撤销 / 重做（最多 100 步）  
· 自动保存开关（工具栏 自动保存）  
· Ctrl + S 先提交当前焦点编辑器，再执行完整保存  

C# 事件生成  

· 基于 Roslyn 修改 .cs 文件  
· 修改 x:Name 时自动派生事件处理器  
· Button 等控件生成 Click，无 Click 能力的控件生成 Tapped  
· 双击画布空白区域为根内容元素生成 Loaded 订阅  
· 用户手写的事件保持不变，不被自动改名覆盖  

---

## 系统要求

项目 要求  
操作系统 Windows 10 1809（17763）或更高 / Windows 11  
运行时 .NET 8  
UI 框架 WinUI（Windows App SDK）  
打包方式 Single-project MSIX / Packaged（EnableMsixTooling=true）  
开发工具 Visual Studio 2026/2022（含 Windows App SDK 工作负载）  

---

## 安装与构建  

### 从源码构建  

1. 克隆仓库：  
   ```bash
   git clone https://github.com/<your-account>/XamlSub.git
   cd XamlSub
   ```
2. 用 Visual Studio 2026（或支持WinUI的VS版本） 打开解决方案（.sln）。   
3. 确认已安装 Windows App SDK 工作负载与 .NET 8 SDK。  
4. 选择 x64 平台配置。  
5. 生成解决方案：  
   ```text
   Ctrl + Shift + B
   ```

### 从安装包安装  

1.前往[Release界面](https://github.com/MsintX/XamlSub/releases)；  
2.选择合适（推荐选择Lastet Release）的版本 打开附件列表；  
3.安装pfx证书，位置选择“受信任的根证书颁发机构”；  
4.安装msix包。  

---

## 快速上手  

场景一：从空白 XAML 开始  
1. 在设置中选择一个合适的定位方式  
2. 新建一个文本文档，重命名为 任意合法名.xaml（内容为空）。  
3. 点击工具栏 打开文件，选择该文件。  
4. 弹出「XAML 文件为空」对话框，点击 创建默认 布局。  
5. 设计器自动写入标准命名空间与布局，并进入设计视图。  

场景二：编辑现有 XAML  

1. 点击 打开文件 选择 .xaml，或点击 打开项目 选择 .csproj 后从列表中选择 XAML。  
2. 左侧工具箱找到目标控件，按住直接拖到画布目标单元格。  
3. 松手后控件被放置，并自动生成唯一 x:Name（如 Button1）。  
4. 单击控件选中，在右侧属性面板修改属性。  
5. 修改 x:Name 后，事件处理器自动派生并写入同名 .xaml.cs。  
6. 点击 保存（或 Ctrl + S）保存文件。  
7. 回到 Visual Studio，选择「重新加载」同步外部修改。  

场景三：调整 Grid 布局（针对相对定位 → Grid）  

1. 左侧「网格（Grid）」区域使用 ＋ 行 / － 行 / ＋ 列 / － 列。  
2. 删除行/列前，设计器会检查是否有控件位于或跨越目标区域，若有则阻止并提示。  
3. 若 Grid 使用 Auto 或固定尺寸，行列编辑会被禁用，仅提供预览与控件编辑，避免静默改变原布局。  

---

## 界面说明

```text
┌──────────────────────────────────────────────────────────────┐
│ 工具栏：打开文件 / 打开项目 / 撤销 / 重做 / 保存 / 重载 / 自动保存 / 关于 │
├────────────┬────────────────────────────────┬────────────────┤
│ 工具箱      │ 设计画布                        │ 属性面板        │
│ Tip 提示    │ ┌──────────────────────────┐   │ Window 属性     │
│ 控件列表    │ │ WindowTitleBar 预览       │   │ 控件属性        │
│            │ ├──────────────────────────┤   │ 控件专属属性    │
│ 定位标题   │ │                          │   │ 更多属性        │
│ ＋操作区  │ │      控件        │   │ 事件           │
│            │ │                          │   │ 删除控件        │
│ 设计视图    │ └──────────────────────────┘   │                │
├────────────┴────────────────────────────────┴────────────────┤
│ 状态栏：文件状态 / 尺寸 / 提示信息                          │
└──────────────────────────────────────────────────────────────┘
```

· 工具栏：文件操作、撤销重做、写回、自动保存开关、关于。  
· 左侧：Tip 提示、控件列表（可拖拽）、Grid 行列编辑、设计视图说明。  
· 中间：设计画布，顶部带窗口标题栏预览。  
· 右侧：属性面板，按 Window / 控件 / 专属属性 / 更多属性 / 事件分组。  
· 底部：状态栏，显示当前文件、Grid 尺寸、缩放比例与提示。  

---

快捷键与鼠标操作  

操作 说明  
Ctrl + S 提交当前编辑器并写回 XAML / C#  
Ctrl + 滚轮 缩放设计视图（25% – 300%）  
Enter（单行输入框） 提交当前属性  
Enter（多行输入框） 换行  
Delete 删除选中控件  
Esc 取消放置 / 拖动  
单击控件 选中控件  
单击空白区域 选中 Window（窗口）  
双击空白区域 为根内容元素生成 Loaded 订阅  
右键画布 取消放置 / 拖动  
工具箱按住拖动 原生拖放控件到画布  

提示（Tip）区域每 1 分钟随机切换一条使用建议。  

---

## XAML 支持边界  

明确支持 / 重点支持  

· Grid（优先相对布局，单元格按 * 均分）  
· 大多数相对定位和绝对坐标定位方法  
· 常用 WinUI 控件（见控件列表）  
· StackPanel 的有限处理  
· 常用属性  
· x:Name  
· Click / Tapped / Loaded 事件  
· Grid.Row / Grid.Column / RowSpan / ColumnSpan  
· 增删行列  
· Undo / Redo  
· XAML / C# 写回  
· Grid.RowDefinitions / Grid.ColumnDefinitions 属性元素解析  

不考虑 / 暂不完全支持

· 任意 XAML 语法  
· 复杂 Binding  
· Template  
· Style  
· ResourceDictionary  
· 设计时属性  
· 完整 Auto / 固定尺寸布局编辑  
· 高保真实时控件渲染  
· VS 内部无缝同步  

不支持的复杂结构（如 CommandBar、Border、嵌套 Grid、模板等）：  

保留在原 XAML 中，但不进入当前可编辑视觉模型。  

---

## C# 事件生成规则

控件类型 派生事件  
Button / HyperlinkButton / ToggleButton / RepeatButton Click  
其他无 Click 能力的控件 Tapped  
根内容元素（双击空白区域） Loaded  

规则说明：  

· 修改 x:Name 时，若当前事件为自动生成，则同步更新事件处理器名称。  
· 若事件为用户手写，则保留不变，避免覆盖用户代码。  
· 清空 x:Name 时，自动生成的事件会被一并清除。  

---

已知限制  

· XAML 写回仍使用 XML DOM 参与增量写回：  
  · 原节点可尽量复用；  
  · 注释、空白和无关结构会尽量保留；  
  · 但不能保证所有词法细节逐字不变，属性引号、部分格式可能发生变化。  
· 复杂 XAML 结构不进入视觉模型，仅保留。  
· 高保真渲染不是目标，设计视图为线框式假控件。  
· 与 Visual Studio 的同步依赖 VS 自身的「外部文件已修改」提示，非无缝集成。  
· 项目仍处于 Demo（演示）阶段，可能存在未修复的 Bug。  

---

## 常见问题

Q：为什么打开复杂 XAML 后，有些控件看不见？  
A：设计器只把工具箱支持的叶子控件放入视觉模型。CommandBar、Border、嵌套 Grid、模板等结构会保留在原 XAML 中，但不参与可视化编辑，避免误改。

Q：为什么 Grid 行列编辑按钮是灰的？  
A：当前 Grid 使用了 Auto 或固定尺寸。设计器保持原布局，仅提供预览与控件编辑，避免静默改变布局。

Q：保存后 Visual Studio 没有同步？  
A：VS 通过「外部文件已修改」提示同步。请回到 VS，在提示中选择「重新加载」。设计器不直接操作 VS 进程。

Q：为什么空白 XAML 打开时提示创建 Grid？  
A：0 字节的 .xaml 会导致 Root element is missing.。设计器会询问是否创建默认 1 × 1 Grid 与标准命名空间，让设计器可以从空白文件开始工作。

Q：自动保存开关有什么用？  
A：开启后，拖放、属性修改、重命名等操作会在完成后立即保存到文件，无需手动保存。

Q：删除行/列被阻止了怎么办？  
A：目标区域仍有控件。请先移动或缩小这些控件，再删除行列。

Q：手写的事件会被覆盖吗？  
A：不会。设计器会识别用户手写的事件并保留，仅同步自动生成的事件。

---

## 许可证

本项目采用 Apache 2.0 License 发布。

---

## 贡献与反馈

· 欢迎在 GitHub 上提交 Issue 反馈 Bug 或提出建议。  
· 提交 PR 前请先确认本地可正常构建（x64 配置）。  
· 项目仍处于 Demo 阶段，UI 与 API 可能发生变动。  
· 如果你喜欢这个工具，欢迎给个 Star ⭐  

---

## 技术栈

· WinUI（Windows App SDK）  
· .NET 8  
· Roslyn（C# 语法分析与代码编辑）  
· Single-project MSIX / Packaged  

---

## 作者

MisntX & ChatGPT