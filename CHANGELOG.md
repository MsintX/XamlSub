> 这个文件自2026/9/25起不再更新，平替请查看Github Commit日志/[新更新日志文件](/UpdateLog.md)
# XAML Sub — 完整更新日志

> 项目定位：一个独立于 Visual Studio 的轻量级 WinUI 3 XAML 可视化编辑器。
>
> 核心目标：把“拖控件 → 起 `x:Name` → 生成事件 → 写回 `.xaml/.cs`”这条高频路径从手写 XAML 变成可视化操作。

---

## 0. 项目建立 / 初始方案

### 初始目标

项目从“独立于 Visual Studio 的 XAML Sub”方案开始建立。

确定的产品边界：

- 独立 WinUI 3 桌面应用，不做 VSIX、IPC 或进程内 VS 自动化。
- 打开磁盘上的 WinUI XAML 文件，在独立设计画布中编辑。
- 采用**自绘假控件**，不运行真实 WinUI 控件，不追求高保真设计时渲染。
- 第一版只把 `Grid` 作为主要布局容器。
- Grid 行列统一按照 `*` 均分处理。
- 拖拽过程中只操作内存模型；松手后才更新模型并写回磁盘。
- Visual Studio 通过自己的“外部文件已修改”提示完成同步。

### 初始四层架构

```text
宿主层
├─ WinUI 窗口
├─ 工具栏
├─ 工具箱
├─ 设计画布
├─ 属性面板
└─ 状态栏

预览层
├─ 自绘假控件
├─ Grid 网格
├─ 选中状态
├─ 拖拽反馈
└─ 对齐辅助线

模型层
├─ XAML 文档模型
├─ Grid 行列
├─ 控件节点树
└─ C# 语义模型

写回层
├─ XAML 写回
├─ C# Roslyn 写回
└─ 回滚/事务
```

---

# 1. Initial MVP

第一份可运行 MVP 工程建立完成。

### UI

建立 WinUI 3 主窗口，划分为：

- 顶部工具栏
- 左侧工具箱
- 中间设计画布
- 右侧属性面板
- 底部状态栏

### 工具箱

第一版加入常用控件：

`Button`、`TextBlock`、`TextBox`、`Image`、`CheckBox`、`RadioButton`、`Slider`、`ProgressBar`、`ListView`、`ComboBox`

### 自绘画布

实现：

- Grid 网格绘制
- 假控件显示
- 控件选中
- 控件移动
- Grid.Row / Grid.Column 落点计算
- 控件默认尺寸

### XAML

实现：

- 打开 XAML
- 解析 Grid 行列
- 生成 Grid
- 写回 XAML
- 第一版主要使用 XML DOM 进行处理

### C# / 事件

引入 Roslyn 作为 C# 修改基础：

- 查找对应 `.cs`
- 查找 partial class
- 生成事件处理方法
- 为 `Button` 等控件生成 Click handler
- 无 Click 能力的控件按规则生成 Tapped handler

---

# 2. Phase 2 — 从 Demo 开始向 Designer 演进

### XAML 写回

开始从“重建整个 Grid”转向：

> 以打开时的原始 XAML 为基线，尽量只修改需要修改的节点和属性。

增加：

- 原始 XAML 基线保存
- 已有节点复用
- 增量式属性修改
- 成功写回后更新基线
- 双文件事务写回
- 失败回滚

### 设计交互

增加：

- Grid 目标区域高亮
- 拖拽辅助反馈
- Esc 取消放置
- Delete 删除控件
- Grid 行列编辑
- 删除行/列前检查现有控件

### 项目打开

增加：

- `.csproj` 项目选择
- 从项目目录选择 XAML
- 重新加载前检查未写回修改

### 关于

加入“关于”功能，最初以页面/窗口形式实现，后改为ContentDialog

---

# 3. Phase 2 Fixed 1 — MainWindow / ToolboxItems 修复

首次进入实际构建测试后，发现：

```text
类型“MainWindow”已定义了一个名为“MainWindow”的具有相同参数类型的成员
```

以及：

```text
在类型“MainWindow”中找不到属性“ToolboxItems”
```

### 修复

- 清理重复的 `MainWindow` 定义。
- 保证 `MainWindow` 只保留一个构造函数。
- 避免 `ToolboxItems` 依赖导致的 XAML 绑定生成问题。
- 工具箱集合改为在代码中明确设置 `ItemsSource`。

同时清理可能造成干扰的旧生成文件/示例文件。

---

# 4. Phase 2 Fixed 2 — XName / string 类型修复

编译过程中发现：

```text
无法从“System.Xml.Linq.XName”转换为“string”
```

以及：

```text
HashSet<string>.Add(string) 参数无效
```

### 原因

`XName` 被直接放进了 `HashSet<string>`。

### 修复

改为显式字符串：

```csharp
(X + "Name").ToString()
```

从而统一：

```text
XName
    ↓
string
    ↓
ExtraAttributes / HashSet<string>
```

---

# 5. Phase 2 Fixed 3 — ToolboxItem init-only 修复

出现：

```text
只能在对象初始值设定项中或在实例构造函数或 "init" 访问器中的 "this" 或 "base" 上分配 init-only 属性或索引器 "ToolboxItem.DisplayName"
```

以及同类 `TypeName` 错误。

### 原因

早期 `ToolboxItem` 使用了 `record`，生成的属性为 `init` 语义。

### 修复

改为普通 class：

```csharp
public sealed class ToolboxItem
{
    public string TypeName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

这样更加适合当前 WinUI 绑定和编辑场景。

---

# 6. Phase 2 Fixed 4 — WinUI Resource Key 修复

运行时遇到：

```text
Cannot find a Resource with the Name/Key CardStrokeColorDefaultBrush
```

### 修复方向

检查应用自定义资源，并减少对不明确/不存在 Resource Key 的依赖。

后续继续采用应用自己定义的稳定资源。

---

# 7. Phase 2 Fixed 5 — XamlControlsResources 修复

随后发现：

```text
Cannot find a Resource with the Name/Key TabViewButtonBackground
```

### 根因

WinUI Controls 默认资源字典没有正确加载。

### 修复

在 `App.xaml` 中加入：

```xml
<XamlControlsResources
    xmlns="using:Microsoft.UI.Xaml.Controls" />
```

恢复 WinUI 控件默认样式与资源。

这一步解决的是：

> WinUI 控件本身的模板依赖资源缺失

而不是继续给单个 Resource Key 打补丁。

---

# 8. Phase 2 Fixed 6 — XAML 打开体验与 UI 调整

### 修复/改进

#### 关于

从独立窗口改为：

> **直接使用 ContentDialog**

所有 ContentDialog 统一使用：

```csharp
Style =
    Application.Current.Resources["DefaultContentDialogStyle"] as Style
```

#### 工具箱

- 删除控件图标。
- 改为纯文字列表。

#### 属性术语

增加中文说明，例如：

```text
x:Name（控件名称）
Grid.Row（所在行）
Grid.Column（所在列）
RowSpan（跨行数）
ColumnSpan（跨列数）
HorizontalAlignment（水平对齐）
VerticalAlignment（垂直对齐）
```

#### XAML 错误诊断

打开文件时开始提供更明确的错误信息，而不是只显示底层 XML 异常。

---

# 9. Fix 7 — 空白 XAML 自动初始化

测试中发现一个很真实的场景：

> 新建一个完全空白的文本文档，然后把文件名改成 `1.xaml`。

此时文件合法存在，但内容是 0 字节。

原程序会得到：

```text
Root element is missing.
```

### Fix 7

改变行为：

```text
检测到 XAML 文件为空
        ↓
ContentDialog
        ↓
是否创建默认 Grid？
        ↓
创建
        ↓
进入 Designer
```

默认创建一个：

```text
1 × 1 Grid
```

并写入标准 WinUI XAML 命名空间。

这让 Designer 可以真正从一个空白 XAML 文件开始工作。

---

# 10. Fix 8 — Grid.RowDefinitions / ColumnDefinitions 解析修复

Fix 7 创建默认 Grid 后，又发现：

```text
暂不支持带子元素的控件/模板：
Grid.RowDefinitions
```

### 根因

解析器把：

```xml
<Grid.RowDefinitions>
```

误识别成了普通控件。

实际上它属于 XAML 的：

> **Property Element（属性元素）**

### Fix 8

解析器开始正确识别：

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="*"/>
</Grid.RowDefinitions>

<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*"/>
</Grid.ColumnDefinitions>
```

同时保证写回时也使用正确的属性元素名称。

---

# 11. Fix 9 — 支持打开 Designer 自身复杂 XAML + 原生拖放

测试时发现：

> 连 XAML Sub 自己的 `MainWindow.xaml` 都可能打不开。

### 根因

旧解析策略过于激进：

> 任何带子元素的直接子节点都可能被当成“不支持的控件”。

因此 `CommandBar`、`Border`、嵌套 `Grid` 等结构容易被误判。

### Fix 9 — XAML 解析策略调整

现在：

- 工具箱支持的叶子控件 → 进入视觉模型。
- `CommandBar`
- `Border`
- 嵌套 `Grid`
- `StackPanel`
- 模板
- 其他未知结构

这些内容：

> **保留在原 XAML 中，但不进入当前可编辑视觉模型。**

这样 Designer 可以打开更复杂的 XAML，而不会假装理解自己不支持的全部 XAML 语法。

### 工具箱拖拽

同时实现真正的原生拖放：

```text
工具箱控件
    ↓ 按住
拖到画布
    ↓
DragOver
    ↓
高亮目标 Grid 单元格
    ↓
松手
    ↓
创建控件
```

不再要求：

```text
先点击工具箱
再点击画布
```

---

# 12. Fix 10 — 视觉预览、30+ 控件、动态属性面板

这是一次比较大的功能升级。

## 12.1 自绘视觉改进

原来的自绘控件比较像：

> “手画的矩形”

虽然不影响最终 Coding，但设计视图观感较差。

因此保留自绘架构，同时把视觉表现升级为：

> **接近 Fluent / WinUI 的线框式控件预览**

例如：

- Button → 按钮形态
- TextBox → 输入框形态
- CheckBox → 勾选框
- RadioButton → 单选框
- ToggleSwitch → 开关
- Slider → 滑块
- ProgressBar → 进度条
- ListView → 列表
- Image → 图片占位

注意：

> 仍然不是运行真实 WinUI 控件。

这样继续保留自绘的性能和可控性。

## 12.2 工具箱扩展

工具箱从早期的 10 个控件扩展到 30+ 个常用控件。

包含：

```text
Button
HyperlinkButton
ToggleButton
RepeatButton

CheckBox
RadioButton
ToggleSwitch

TextBlock
TextBox
PasswordBox
AutoSuggestBox
NumberBox
RichEditBox

ComboBox
ListView
ListBox
TreeView
ItemsRepeater

Slider
ProgressBar
ProgressRing

Image
Icon
FontIcon

DatePicker
CalendarDatePicker
TimePicker
CalendarView

Expander
InfoBar
RatingControl
NavigationView
Pivot
TabView
ScrollViewer
CommandBar
```

## 12.3 属性面板升级

根据我提出的设计思路：

> **所有可编辑框都列出来，再根据当前选中控件动态启用/禁用。**

例如：

```text
选中 Image

Source（资源路径）       [启用]
Content（内容）          [禁用]
```

而：

```text
选中 Button

Source（资源路径）       [禁用]
Content（内容）          [启用]
```

新增大量属性：

```text
x:Name
Content / Text

Grid.Row
Grid.Column
RowSpan
ColumnSpan

Width
Height
MinWidth
MaxWidth
MinHeight
MaxHeight

Margin
Padding

HorizontalAlignment
VerticalAlignment
HorizontalContentAlignment
VerticalContentAlignment

FontSize
FontWeight
FontFamily
FontStyle
TextAlignment
CharacterSpacing

Foreground
Background
BorderBrush
BorderThickness
CornerRadius

Opacity
Visibility

IsEnabled
IsTabStop
IsHitTestVisible

ToolTip
PlaceholderText
Header

Source
Stretch
TextWrapping
MaxLength

SelectedIndex
Minimum
Maximum
Value

Orientation
IsChecked
IsOn

OnContent
OffContent
GroupName

IsEditable
AcceptsReturn
IsReadOnly

Command
CommandParameter

IsDefault
IsCancel
ClickMode

IsIndeterminate

DateFormat
NumberFormat
Language
Tag
```

## 12.4 根窗口选择

交互改为：

```text
单击控件
→ 选中控件

单击空白区域
→ 选中窗口 / 根容器
```

这样右侧属性面板也可以编辑窗口属性。

---

# 13. Fix 11 — C# / WinUI 类型错误修复

Fix 10 扩展属性系统后，编译器出现一批错误：

```text
应为 string 类型的常量值
未提供 Thickness 构造函数参数
int 无法转换为 byte
Windows.UI.Colors 不存在
oldValue 重名
accentBrush 不存在
IsDesignableControl 不存在
string 无法隐式转换为 bool
```

### Fix 11

逐项收敛代码：

- 修正 `Thickness` 构造参数。
- 使用 `Color.FromArgb(...)` 创建颜色。
- 对颜色 Alpha 明确转换为 `byte`。
- 补充 `accentBrush`。
- 补充 `IsDesignableControl`。
- 消除 `oldValue` 变量冲突。
- 修正字符串 / bool 类型使用。
- 清理不兼容的模式匹配写法。

---

# 14. Fix 12 — 编译链兼容性收敛

Fix 11 后仍发现：

```text
IsDesignableControl 不存在
string → bool
string 常量值
```

### Fix 12

进一步做代码结构收敛：

- 将 `IsDesignableControl` 放到真正调用它的服务类。
- `ControlNode.IsPropertyEnabled()` 改为传统 `switch` / `if` 判断。
- 属性支持判断减少复杂模式匹配。
- `ToolboxItem` 初始化改为明确的普通构造写法。
- 清理不必要的临时方法。

---

# 15. 本机首次正式 Build 通过

最终在 Visual Studio 中完成实际生成。

生成结果：

```text
========== 生成: 1 成功，0 失败，0 最新，0 已跳过 ==========
========== 生成 于 11:51 完成，耗时 16.586 秒 ==========
```
（不对啊我为什么要贴输出）  

---

# 16. 当前功能总览

截至当前版本，项目已经形成以下完整主流程：

```text
打开 XAML
   ↓
解析 XAML
   ↓
识别支持的结构
   ↓
建立内存模型
   ↓
自绘设计视图
   ↓
工具箱直接拖拽
   ↓
目标 Grid 高亮
   ↓
放置控件
   ↓
单击控件选中
   ↓
右侧动态属性编辑
   ↓
修改 x:Name
   ↓
自动派生 Click / Tapped
   ↓
Roslyn 修改 .cs
   ↓
写回 .xaml + .cs
   ↓
Visual Studio 检测外部修改
   ↓
重新加载
```

---

# 17. 当前 XAML 支持边界

当前仍然刻意不追求完整 XAML Designer。

明确支持/重点支持：

- Grid
- 一律 `*` 的相对布局
- 常用 WinUI 控件
- StackPanel 的有限处理
- 常用属性
- x:Name
- Click
- Tapped
- Loaded
- Grid.Row / Column
- RowSpan / ColumnSpan
- 增删行列
- Undo / Redo
- XAML / C# 写回

明确不做或暂不完全支持：

- 任意 XAML 语法
- 复杂 Binding
- Template
- Style
- ResourceDictionary
- 设计时属性
- 完整 Auto / 固定尺寸布局编辑
- 高保真实时控件渲染
- VS 内部无缝同步

不支持的复杂结构尽量：

> **保留，不静默删除或修改。**

---

# 18. 已知技术限制

当前第二阶段仍然使用 XML DOM 参与 XAML 增量写回。

因此：

- 原节点可以尽量复用。
- 注释、空白和无关结构会尽量保留。
- 但不能保证所有词法细节逐字不变。
- 属性引号、部分格式等仍可能发生变化。

后续若要进一步提高保真度，可以把 XAML 写回层升级成真正的：

> **syntax-preserving / trivia-preserving XAML 文本编辑器**

---

# 19. 下一阶段方向

下一阶段的重点不再是单纯“让它能编译”，而是 Designer 的实际使用体验。

方向：

### Designer

- 更精细的 Fluent 线框视觉。
- 控件之间的对齐辅助线。
- 吸附。
- 多选。
- 框选。
- 更自然的拖拽动画。
- Span 的可视化调整。

### 属性系统

- 属性分类。
- 搜索属性。
- “常用 / 布局 / 外观 / 行为 / 高级”。
- 枚举属性改为 ComboBox。
- Thickness 使用结构化编辑。
- Color 使用颜色选择器。
- Brush 支持。
- 资源选择器。

### XAML

- 更严格的语法树映射。
- 属性元素。
- 嵌套 Grid。
- StackPanel 顺序编辑。
- Auto / 固定尺寸的可视化处理。
- 更强的未知结构保留能力。

### Roslyn

- 更可靠的 SemanticModel。
- 符号级 rename。
- 字段引用同步。
- 用户手写 handler 的保护。
- 更完整的原子回滚。

---

# 20. 版本记录速览

| 阶段 | 主要内容 |
|---|---|
| Initial MVP | WinUI 3 宿主、自绘画布、工具箱、属性面板、XAML/C# 写回 |
| Phase 2 | 增量 XAML 写回、事务、项目选择、更多 Designer 行为 |
| Fixed 1 | MainWindow / ToolboxItems 修复 |
| Fixed 2 | XName → string 修复 |
| Fixed 3 | ToolboxItem init-only 修复 |
| Fixed 4 | WinUI Resource Key 修复 |
| Fixed 5 | XamlControlsResources 修复 |
| Fixed 6 | ContentDialog、术语中文解释、空文件诊断 |
| Fix 7 | 空白 XAML 自动创建 1×1 Grid |
| Fix 8 | Grid.RowDefinitions / ColumnDefinitions 属性元素解析 |
| Fix 9 | 复杂 XAML 保留 + 工具箱原生拖拽 |
| Fix 10 | 30+ 控件、自绘视觉升级、动态属性系统 |
| Fix 11 | WinUI/C# 类型与 API 编译错误修复 |
| Fix 12 | 进一步收敛 C# / XAML 编译兼容性 |
| Current | **本机 Visual Studio Build 通过** |

---

# 21. Fix 13 — 属性提交与 WindowTitleBar 预览

- Window（窗口）属性区保持可编辑。
- 属性文本框在焦点离开时提交；不允许换行的输入框按 Enter 提交；允许换行的输入框保留 Enter 换行。
- `Ctrl+S` 先提交当前焦点编辑器，再执行完整写回。
- 左侧 Grid/Design View 说明从工具箱区域独立出来。
- 设计视图顶部增加 `WindowTitleBar` 预览，显示预览图标、窗口标题和窗口控制按钮轮廓。
- Window 标题提交后实时更新预览；无标题时回退到当前 XAML 文件名。


# 17. Fresh Packaged Rebuild

本版本不再继续修补旧的运行模式，而是重新整理为干净的 **WinUI 3 Single-project MSIX / Packaged** 项目。

- 启用 `EnableMsixTooling=true`。
- 使用 `WindowsPackageType=MSIX`。
- 使用 `AppxPackage=true`。
- 加入项目根目录 `Package.appxmanifest`。
- 加入 `Properties/launchSettings.json`，使用 `MsixPackage` 启动 profile。
- 加入 x64 发布配置。
- 加入本地开发签名证书。
- 保留此前 Designer 功能、Roslyn、工具箱拖拽、动态属性、Tip、About Dialog、WindowTitleBar Preview 等功能。
- 不使用 `WindowsPackageType=None`。
- 不使用 `WindowsAppSDKSelfContained=true` 作为默认分发方式。
## 2026-09-30

### 设计器交互与定位辅助
- 设置页与 NavigationView 汉堡窗格加入平滑过渡动画。
- 选中控件后提供八方向缩放手柄：左右调整宽度、上下调整高度、四角同时调整宽高。
- 不同定位策略显示不同的设计器辅助线/靠边提示，并将左侧 Grid 工具区改为随定位方式切换的定位工具区。
- Grid 模式保留行列增减；自定义 Panel 提供对齐辅助；DockPanel 提供停靠边选择；Canvas/绝对 Grid 提供坐标与对齐工具。
- 取消设计器交互时的离画布越界拖动，并完善取消拖动/缩放的状态恢复。


### 2026-09-30 — WMC0035 XAML 修复
- 移除 `Grid.KeyboardAccelerators` XAML 属性元素，避免 WinUI XAML Compiler 将其错误解析为对 `Grid.Children` 的重复赋值。
- Ctrl+S 快捷键改为在 `MainWindow` 构造函数中创建并加入 `RootLayout.KeyboardAccelerators`。
- 保留自定义 `TitleBar` 与现有 `XamlSaveStateFile` 隔离配置。

## 2026-09-30 - Settings motion and material polish

- Added a Store-style custom title-bar back ToggleButton that appears only in Settings and returns to the designer.
- Replaced hand-authored Storyboard transitions for Settings/workspace navigation with WinUI theme transitions (`ContentThemeTransition`).
- Kept NavigationView pane opening/closing on its built-in WinUI pane motion rather than layering a second custom animation over it.
- Settings navigation item changes now swap pages through a ContentControl so the page content uses WinUI's content transition.
- Settings NavigationView background now follows the selected window material: transparent over Mica, WinUI in-app Acrylic for Acrylic, and the normal theme background for None.
- Moved the designer/settings workspaces into a content host so entering/leaving Settings animates without changing the editor's existing logic.


## 2026-09-30 — Designer sizing and save behavior

- 修复 Ctrl+滚轮缩放后右下角缩放状态文字不同步：缩放视图尺寸与状态栏百分比现在由同一更新入口刷新。
- 默认设计窗口预览从 900 × 662 调整为 900 × 720，高度增加但保持小于原宽度。
- 移除画布上的鼠标缩放手柄与八方向调整逻辑，恢复右侧属性面板编辑 Width / Height。
- Width / Height 属性现在在输入过程中实时更新设计预览。
- 恢复“自动保存”开关，入口为“设置 → 通用”，默认关闭；关闭时仅保留内存修改，使用“保存”才写回。
- 保留 Ctrl+S 快捷键，但移除 KeyboardAccelerator，改用根布局键盘事件，因此鼠标停留时不再显示“Ctrl+S”提示。
