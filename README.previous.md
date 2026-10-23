> 这个文件自2026/9/25起不再更新，平替请查看README
# XAML Sub

一个独立于 Visual Studio 的 WinUI 3 轻量级 XAML 可视化编辑器 MVP。Fix 10 在原模型上继续加入原生工具箱拖放、更丰富的 WinUI 控件、更完整的属性编辑器、动态属性禁用/启用、根窗口选择，以及更接近 Fluent 的线框式自绘预览。设计目标仍严格围绕：**拖控件 → 起 x:Name → 生成事件 → 写回 .xaml/.cs**。它不做 VSIX、IPC，也不做真实控件设计时渲染；画布使用可控的自绘假控件。

## 对照方案的实现

- WinUI 3 桌面宿主：工具栏、工具箱、设计画布、属性面板、状态栏。
- 根 `Grid` + 全部 `*` 行列；画布按行列均分计算落点。
- 工具箱扩充到 30+ 个常用控件，包括 Button、TextBlock、TextBox、PasswordBox、AutoSuggestBox、NumberBox、RichEditBox、ComboBox、ListView、TreeView、Slider、ProgressBar、ProgressRing、Image、DatePicker、CalendarDatePicker、TimePicker、Expander、InfoBar、RatingControl、NavigationView、TabView、CommandBar 等。
- 工具箱使用原生拖放，按住控件直接拖到画布；画布内拖动控件改变 `Grid.Row/Column`。
- `RowSpan/ColumnSpan`、常用宽高/最小最大尺寸、边距、内距、字体、颜色、可见性、交互状态、控件专属属性等。
- x:Name 自动派生 Click/Tapped；保存时用 Roslyn 生成/重命名 handler。
- 自动生成的 handler 与用户手写 handler 区分：用户手写事件不会因 x:Name 改名而被覆盖。
- 清除 x:Name 时，自动生成的 TODO handler 会一起移除；用户手写 handler 不会被删除。
- 双击画布空白区域生成 WinUI 3 内容元素 `Loaded`：自动给根 Grid 增加 `x:Name="ContentRoot"`，并在构造函数 `InitializeComponent()` 后订阅 `ContentRoot.Loaded += Window_Loaded;`，操作幂等。
- Undo/Redo 命令栈。
- 自动写回开关；关闭时可以批量修改后点击“写回”。
- XAML 增量写回：以打开时的原始 XAML 为基线，复用已有 XElement，仅更新 Grid/控件相关属性；尽量保留原有注释、空白和无关属性。
- 删除/增加 Grid 行列；删除会影响已有控件时拒绝操作，不静默移动或截断。
- 项目选择器可从 .csproj 所在目录选择 XAML；Esc 取消放置，Delete 删除选中控件。
- 独立“关于”页改为 ContentDialog
- 不支持的复杂结构会被保留但不进入视觉模型；Auto/固定行列只读预览，行列编辑按钮禁用，不静默修改原布局。

## XAML 保真说明

第二阶段不再每次保存时重建整个 Grid，而是以打开时的原始 XAML 为基线：已有根节点、控件节点和无关属性尽量复用，模型只对 Grid 定义、布局属性、名称、内容和事件做增量修改。由于底层仍是 XML DOM 而不是完整 XAML syntax tree，属性引号等词法细节仍不能保证逐字保留；下一阶段可继续替换为真正的 syntax-preserving XAML 文本编辑器。

## Windows 构建

此仓库目标为 Windows + WinUI 3。建议在 Windows 10/11 上使用 Visual Studio 的 Windows App SDK/WinUI 工作负载，选择 x64 后运行 `XamlSub.csproj`。

依赖：

- .NET 8
- Windows App SDK 2.5.1
- Microsoft.CodeAnalysis.CSharp 5.9.0
- Microsoft.CodeAnalysis.CSharp.Workspaces 5.9.0

## 使用示例

打开 `Sample/GridPage.xaml`，然后：

1. 点击工具箱中的 Button。
2. 点击 Grid 的目标格。
3. 选中 Button，在属性面板修改 `x:Name`。
4. 点击“写回”。
5. `.xaml` 会出现 `Click="Button1_Click"` 一类事件属性，`.xaml.cs` 会出现对应 handler。
6. 在 Visual Studio 中接受外部修改重载。

## 属性面板行为

右侧属性面板采用“字段预先列出、按对象能力动态启用”的方式。选择控件后，只对当前控件适用的属性开放编辑。例如 `Image` 的 `Source（资源路径）` 可编辑，而 `Button` 会自动禁用 `Source`；点击设计画布空白区域则选择根窗口/根容器，并切换到窗口属性。

## 明确边界

这是方案第一阶段的可用实现，而不是 VS Designer 的替代品。它不做真实控件渲染，不尝试推断复杂布局意图，也不碰 VS 进程内部同步。

## Fix 9 行为

工具箱采用 WinUI ListView 原生拖放：从控件项直接拖到设计画布，在 DragOver 时显示目标 Grid 单元格，松手后创建控件，不需要先点击工具箱进入“放置模式”。

解析器只将工具箱支持的叶子控件放入可视模型。CommandBar、Border、嵌套 Grid/StackPanel、模板和其他未知结构元素会保留在 XAML 中，但不会进入当前可编辑模型；因此项目自己的复杂 MainWindow.xaml 也可以打开，而不会把不理解的结构误当控件。


## Fix 13 行为

- 窗口（Window）属性编辑继续保持可用；窗口被选中时，窗口属性区保持启用。
- 文本属性输入框在焦点离开时提交；不允许换行的输入框按 Enter 也会提交。
- `Ctrl+S` 会先提交当前获得焦点的文本输入框，再执行完整写回。
- 支持换行的内容输入框保留 Enter 的换行语义。
- 左侧“工具箱”与“网格”“设计视图”说明分成独立区域，不再把网格/设计说明塞在工具箱列表底部。
- 中央设计视图顶部增加 `WindowTitleBar` 预览，显示窗口图标占位、窗口标题和标准窗口按钮轮廓。

## Tip 提示

工具箱上方提供 `Tip: <提示>` 区域，从内置提示池随机展示 Designer 使用技巧；首次打开立即展示，并每 1 分钟随机切换一次，连续两次不会重复。


## Fix 15
- 修复 HighContrast ThemeDictionary 中使用无效命名 Color（Window/WindowText/Highlight/HighlightText）导致的潜在运行时 XAML 解析异常。
- 统一改为合法的 WinUI Color 十六进制值。
