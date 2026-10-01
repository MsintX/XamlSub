# 目录  
[Demo → Release 1.0.0](#demo--release-100)  

# Demo → Release 1.0.0  

## 重要更新  

[新增] 新增自定义标题栏（TitleBar），窗口标题显示为 XAML Sub，并带副标题与图标  
[新增] 新增设置工作区（SettingsWorkspace），用 NavigationView 分"通用 / 设计 / 关于"三页，替代 Demo 里单一的"关于我们"弹窗  
[新增] 新增窗口材质设置，支持 Mica（云母）/ Acrylic（亚克力）/ None 三档，可即时切换并持久化  
[新增] 新增自动保存开关，作为持久化设置项放入设置页；Demo 里只是 CommandBar 上的一个临时 AppBarToggleButton  
[新增] 新增"宽泛的保存检查"选项：没有明确 x:Class 时，只要同目录存在同名 .xaml.cs，也允许把事件代码写回该 C# 文件  
[新增] 新增五种定位策略，可在设置中切换：  
        · 相对定位 → 自定义 Panel  
        · 相对定位 → DockPanel  
        · 相对定位 → Grid  
        · 绝对坐标 → Canvas  
        · 绝对坐标 → Grid（Margin 负值 / 精确值）  
      Demo 只有单一 Grid 相对定位  
[新增] 新增"定位方式"上下文工具面板，会随当前策略自动显示对应工具（＋/－行、靠边/居中按钮、X/Y NumberBox、停靠边 ComboBox 等）  
[新增] 新增 Canvas 绝对坐标定位，支持 X/Y 坐标与标尺辅助线  
[新增] 新增 DockPanel 定位，支持停靠边选择与拖动吸附到最近边  
[新增] 新增自定义 Panel 定位，支持靠左/中/右、靠上/中/下对齐辅助线  
[新增] 新增 Grid 绝对定位（Margin 负值 / 精确值），支持 X/Y 边距与对齐按钮  
[新增] 新增控件拖动偏移（OffsetX / OffsetY），支持自由摆放而不是仅按单元格吸附  
[新增] 新增 Ctrl+C / Ctrl+V 复制粘贴控件，粘贴时自动生成唯一 x:Name 与事件名  
[新增] 新增 Ctrl+S 键盘加速器，并支持保存前先提交当前正在编辑的属性  
[新增] 新增"设计视图"说明面板，明确自绘预览不运行真实控件模板  
[新增] 新增状态栏 Grid 尺寸显示、缩放百分比显示、自动保存开关状态显示  
[新增] 新增 Tip 定时器，每分钟随机切换一条 Tip  
[新增] 新增窗口标题栏预览（PreviewWindowTitleText），会跟随 Window 的 Title 属性更新  
[新增] 新增根容器（非 Window）支持，属性面板会按 RootTypeName 判断是否启用窗口级编辑项  

[修复] 修复 Demo 中拖动修饰键（Ctrl 复制 / Alt 自由移动 / Ctrl+Shift 锁定方向）只在 Tip 里写了、代码里没实现的问题  
[修复] 修复 Demo 中属性面板不区分"窗口选中"与"控件选中"的问题，Release 里 RootProperties 与 ControlNode 分开处理  
[修复] 修复 Demo 中重命名控件时不会回滚的问题，Release 里 CommitNameAsync 失败会 CopyFrom(before) 并恢复选中项  

[更改] 更改命名空间：WinUIXamlDesigner → XamlSub  
[更改] 更改窗口标题：WinUI XAML Designer → XAML Sub  
[更改] 更改根布局：RowDefinitions 从 Auto,*,Auto 改为 Auto,Auto,*,Auto，为标题栏腾出独立行  
[更改] 更改 CommandBar 位置：从 Grid.Row=0 移到 Grid.Row=1，标题栏独占 Row=0  
[更改] 更改"关于我们"入口：从 CommandBar 按钮 + ContentDialog，改为设置工作区内的独立 AboutSettingsPanel  
[更改] 更改"自动保存"入口：从 CommandBar 的 AppBarToggleButton，改为设置页内的 ToggleSwitch  
[更改] 更改设置持久化：Demo 不持久化，Release 通过 AppSettingsService 持久化  
[更改] 更改画布基准高度：DesignPreviewBaseHeight 从 662 改为 720，画布高度从 620 改为 678  
[更改] 更改设计预览的边框处理：Release 里用 Canvas 作为控件宿主，Demo 里直接用 Border  

## 次要更新  

[新增] 新增设置页进出动画，使用 ContentThemeTransition 与 Storyboard（DoubleAnimation + CubicEase）  
[新增] 新增返回按钮（SettingsBackButton），从 0 宽动画展开到 40 宽，带 Opacity 过渡  
[新增] 新增 SettingsButton 的 Label 动态切换（"设置" ↔ "主页"）  
[新增] 新增 PaneDisplayMode="LeftCompact" 的 NavigationView 设置导航  
[新增] 新增无障碍标注 AutomationProperties.Name="返回设计器"  
[新增] 新增 mica / acrylic / none 切换时 SettingsNavigation.Background 的联动处理  
[新增] 新增 AcrylicInAppFillColorDefaultBrush 资源引用，让 NavigationView 侧栏与窗口材质一致  
[新增] 新增 _writeGate（SemaphoreSlim）串行化写入，避免自动保存与手动保存并发  
[新增] 新增 _settingsTransitioning 标志，避免设置页切换重入  
[新增] 新增 _settingsPageReady 标志，避免重复初始化设置页  
[新增] 新增 _sizeHistoryCommitted 标志，控制 Width/Height 连续输入时的历史提交粒度  
[新增] 新增 _hasLastCanvasPointerPosition，记录最后一次画布指针位置供粘贴定位使用  
[新增] 新增 _hoverPoint，记录拖动悬停点供放置预览使用  
[新增] 新增 _lastTipIndex，避免连续两次 Tip 重复  
[新增] 新增 _document.CodeBehindPath 关联逻辑，支持 x:Class 与同名 .xaml.cs 两种匹配  
[新增] 新增 FindSameNameCodeBehind / FindAssociatedCodeBehind / GetBroadSaveClassIdentity 等 ProjectLocator 能力  
[新增] 新增 StatusText 对"已打开但未找到同名 .xaml.cs"与"找到同名但未关联"的区分提示  
[新增] 新增 IgnoredElementCount 提示，显示有多少非设计控件/容器被保留但不参与编辑  
[新增] 新增 GetPreviewWindowTitle，按 Title → 文件名 → RootTypeName → "Window" 的顺序回退  
[新增] 新增 UpdateAppTitleBar，让 AppTitleBar.Subtitle 显示"正在编辑：xxx.xaml"  
[新增] 新增 RootValue / PropertyValue 辅助方法，统一读取根属性与控件属性  
[新增] 新增 IsWindowPropertyEditor，区分窗口属性编辑器与控件属性编辑器  
[新增] 新增 ControlPropertyEditorMap，用字典统一管理控件属性编辑器与启用状态  
[新增] 新增 WindowPropertyControls，统一管理窗口属性编辑器集合  
[新增] 新增 PositionToolCombo_SelectionChanged，处理 DockBox 停靠边选择  
[新增] 新增 PositionAlign_Click，处理靠左/中/右、靠上/中/下按钮  
[新增] 新增 PersistVisualOffset，把 OffsetX/OffsetY 写回 Margin 属性  
[新增] 新增 SetNodeVisualOrigin / GetNodeVisualBounds，统一计算控件视觉边界  
[新增] 新增 DrawPositioningGuides，按当前策略绘制不同辅助线（网格 / 边距 / 停靠区 / 标尺）  
[新增] 新增 DrawDockZone，绘制 Top / Bottom / Left / Right 四个停靠区  
[新增] 新增 AddGuideLabel，为 Canvas 标尺绘制坐标标签  
[新增] 新增 AddPlacementPreview，统一绘制半透明放置预览框  
[新增] 新增 UpdateZoomHint，状态栏显示缩放百分比与自动保存状态  
[新增] 新增 UpdatePositionToolsUi，按当前策略与选中状态统一控制定位工具面板  
[新增] 新增 UpdatePositionStrategyVisibility，切换相对/绝对策略时显示对应子面板  
[新增] 新增 SelectSettingsCombo，按值选中设置页 ComboBox  
[新增] 新增 IsGridRelativeStrategy / IsCustomPanelStrategy / IsDockPanelStrategy / IsCanvasStrategy / UsesFreePositioning，统一判断当前定位策略  
[新增] 新增 GetMotionDuration，从资源读取 ControlNormalAnimationDuration，回退 250ms  
[新增] 新增 ConfigureWinUiTransitions，为 MainWorkspaceHost 与 SettingsPageHost 配置 ContentThemeTransition  
[新增] 新增 ShowSettingsPage，按 Tag 切换 General / Design / About 三个面板  
[新增] 新增 InitializeSettingsUi，统一初始化设置页控件并移除池中面板  
[新增] 新增 AutoSaveToggle_Toggled 与 BroadSaveCheckToggle_Toggled 处理  
[新增] 新增 SettingsCombo_SelectionChanged，统一处理 WindowBackdrop / PositioningMode / RelativeStrategy / AbsoluteStrategy  
[新增] 新增 ApplyWindowBackdrop，按材质切换 SystemBackdrop 与 SettingsNavigation.Background  
[新增] 新增 ThemeBrush 辅助方法，从 Application.Current.Resources 取画刷并带回退  
[新增] 新增 PreviewColor 辅助方法，统一构造 Windows.UI.Color  
[新增] 新增 CommitFocusedEditorAsync，保存前提交焦点中的 TextBox  
[新增] 新增 RootLayout_KeyDown，处理 Ctrl+S / Ctrl+C / Ctrl+V / Delete  
[新增] 新增 PasteCopiedControl，按当前策略计算粘贴位置并生成新名称与事件名  
[新增] 新增 SizeBox_TextChanged，支持 Width/Height 连续输入并控制历史提交粒度  
[新增] 新增 MakeUniqueName，保证新控件 x:Name 不与已有重复  
[新增] 新增 DefaultWidth / DefaultHeight / DefaultContent / ParseSize 等默认值辅助  
[新增] 新增 AboutSettingsPanel 中的"借物表"、"特别鸣谢"、"开发作者"三块结构化内容  
[新增] 新增"特别鸣谢"对 WinUI 与 Windows App SDK 团队的明确致谢  

[修复] 修复 Demo 中 DesignCanvas_DoubleTapped 生成 Loaded 功能在 Release 中被移除后、窗口级 Loaded 语义不清的问题（Release 改为通过根内容元素订阅）  
[修复] 修复 Demo 中 AutoWriteToggle 直接读 UI 状态的问题，Release 改为读 _settings.AutoSaveEnabled  
[修复] 修复 Demo 中拖动释放后无条件 Pop 撤销栈的问题，Release 改为只在位置未变时 Pop  
[修复] 修复 Demo 中 FindNodeAt 按单元格匹配、无法处理偏移控件的问题，Release 改为按视觉边界 Contains 匹配  
[修复] 修复 Demo 中 RenderCanvas 直接计算 width/height、未考虑最大宽高约束的问题，Release 改为先算 maxWidth/maxHeight 再取 min  
[修复] 修复 Demo 中 Node_PointerPressed 用 Border 作为宿主、无法处理选中高亮层的问题，Release 改为 Canvas + 内层 Border  

[更改] 更改 SaveDocumentAsync：Release 增加 PrepareCodeBehindForSave 与 _writeGate，保存前统一提交焦点编辑器  
[更改] 更改 AutoWriteAsync：Release 增加 _writeGate 与 PrepareCodeBehindForSave，失败时提示"变更仍保留在内存"  
[更改] 更改 CommitHistory：Release 仍保留 100 步上限，但 CommitNameAsync 改为事务式提交与回滚  
[更改] 更改 CommitPropertyTextBoxAsync：Release 增加 IsWindowPropertyEditor 分支，区分根属性与控件属性  
[更改] 更改 PropertyCombo_Changed：Release 增加 IsWindowPropertyEditor 分支  
[更改] 更改 GridValueChanged：Release 增加 _document.Normalize() 调用，保证行列索引合法  
[更改] 更改 ChangeGrid：Release 增加 IsGridRelativeStrategy 判断，非 Grid 策略时提示"当前定位方式不是 Grid"  
[更改] 更改 DeleteSelected：Release 增加 UpdateUi 调用，删除后同步状态  
[更改] 更改 Undo_Click / Redo_Click：Release 增加 UpdateUi 调用  
[更改] 更改 RefreshProperties：Release 大幅扩展，覆盖窗口属性、控件属性、定位工具、事件显示  
[更改] 更改 UpdateUi：Release 增加 UpdateAppTitleBar、UpdateZoomHint、UpdatePositionToolsUi、RefreshProperties 调用  
[更改] 更改 BuildPreview：Release 对 Border 宿主与 Canvas 宿主分别处理，Demo 只处理 Border  
[更改] 更改 RenderCanvas：Release 增加 DrawPositioningGuides、AddPlacementPreview、按策略分支处理  
[更改] 更改 DesignCanvas_PointerPressed：Release 增加 _lastCanvasPointerPosition 记录、_dragOriginalOffsetX/Y 记录  
[更改] 更改 DesignCanvas_PointerMoved：Release 增加 UsesFreePositioning 分支、SetNodeVisualOrigin 处理  
[更改] 更改 DesignCanvas_PointerReleased：Release 增加 DockPanel 吸附、Offset 变化判断  
[更改] 更改 DesignCanvas_PointerCanceled：Release 增加 OffsetX/OffsetY 回滚  
[更改] 更改 DesignCanvas_DragOver：Release 增加 _hoverPoint 记录  
[更改] 更改 Toolbox_DragItemsStarting：Release 增加 StatusText 提示与 RenderCanvas 调用  
[更改] 更改 LoadDocumentAsync：Release 增加 CodeBehindPath 关联、IgnoredElementCount 提示、同名 .xaml.cs 区分  
[更改] 更改 OpenProject_Click：Release 用 ProjectLocator.FindXamlFiles，Demo 逻辑更简单  
[更改] 更改 About_Click：Release 移除该方法，改为设置页内的 AboutSettingsPanel  

[移除] 移除 Demo 中 DesignCanvas_DoubleTapped 与 EnsureLoadedHandler 相关逻辑（Release 未保留该功能）  
[移除] 移除 Demo 中 AutoWriteToggle 这个 CommandBar 上的 AppBarToggleButton  
[移除] 移除 Demo 中 About_Click 与对应的 ContentDialog  
[移除] 移除 Demo 中 HintText 的固定文案，改为按 GridSizingSupported 动态显示  
[移除] 移除 Demo 中 DesignPreviewBaseHeight = 662 的旧基准，改为 720  