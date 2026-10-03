using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;
using FontWeight = Windows.UI.Text.FontWeight;
using FontStyle = Windows.UI.Text.FontStyle;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using XamlSub.Models;
using XamlSub.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace XamlSub;

public sealed partial class MainWindow : Window {
	public ObservableCollection<ToolboxItem> ToolboxItems { get; } = new()
	{
		new ToolboxItem("Button", "Button"), new ToolboxItem("HyperlinkButton", "HyperlinkButton"), new ToolboxItem("ToggleButton", "ToggleButton"),
		new ToolboxItem("RepeatButton", "RepeatButton"), new ToolboxItem("CheckBox", "CheckBox"), new ToolboxItem("RadioButton", "RadioButton"),
		new ToolboxItem("ToggleSwitch", "ToggleSwitch"), new ToolboxItem("TextBlock", "TextBlock"), new ToolboxItem("TextBox", "TextBox"),
		new ToolboxItem("PasswordBox", "PasswordBox"), new ToolboxItem("AutoSuggestBox", "AutoSuggestBox"), new ToolboxItem("NumberBox", "NumberBox"),
		new ToolboxItem("RichEditBox", "RichEditBox"), new ToolboxItem("ComboBox", "ComboBox"), new ToolboxItem("ListView", "ListView"),
		new ToolboxItem("ListBox", "ListBox"), new ToolboxItem("TreeView", "TreeView"), new ToolboxItem("ItemsRepeater", "ItemsRepeater"),
		new ToolboxItem("Slider", "Slider"), new ToolboxItem("ProgressBar", "ProgressBar"), new ToolboxItem("ProgressRing", "ProgressRing"),
		new ToolboxItem("Image", "Image"), new ToolboxItem("Icon", "Icon"), new ToolboxItem("FontIcon", "FontIcon"),
		new ToolboxItem("DatePicker", "DatePicker"), new ToolboxItem("CalendarDatePicker", "CalendarDatePicker"), new ToolboxItem("TimePicker", "TimePicker"),
		new ToolboxItem("CalendarView", "CalendarView"), new ToolboxItem("Expander", "Expander"), new ToolboxItem("InfoBar", "InfoBar"),
		new ToolboxItem("RatingControl", "RatingControl"), new ToolboxItem("NavigationView", "NavigationView"), new ToolboxItem("Pivot", "Pivot"),
		new ToolboxItem("TabView", "TabView"), new ToolboxItem("ScrollViewer", "ScrollViewer"), new ToolboxItem("CommandBar", "CommandBar")
	};

	private readonly XamlDocumentService _xaml = new();
	private readonly CSharpEventService _csharp = new();
	private GridDocument? _document;
	private GridDocument? _savedSnapshot;
	private ControlNode? _selected;
	private bool _windowSelected = true;
	private ToolboxItem? _pendingTool;
	private bool _ignorePropertyChanges;
	private bool _dragging;
	private Point _dragStartPoint;
	private Point _lastDragPoint;
	private double _dragCurrentVisualLeft;
	private double _dragCurrentVisualTop;
	private double _dragOriginalOffsetX;
	private double _dragOriginalOffsetY;
	private readonly Stack<GridDocument> _undo = new();
	private readonly Stack<GridDocument> _redo = new();
	private readonly SemaphoreSlim _writeGate = new(1, 1);
	private readonly AppSettingsService _settings = new();
	private bool _ignoreSettingsChanges;
	private bool _settingsTransitioning;
	private bool _sizeHistoryCommitted;
	private bool _settingsPageReady;
	private ControlNode? _clipboardNode;
	private Point _lastCanvasPointerPosition;
	private bool _hasLastCanvasPointerPosition;
	private int _dragOriginalRow;
	private int _dragOriginalColumn;
	private int _dragOriginalRowSpan;
	private int _dragOriginalColumnSpan;
	private int _hoverRow;
	private int _hoverColumn;
	private Point _hoverPoint;
	// Native visual tree and editor overlay are intentionally separate.
	private readonly Dictionary<ControlNode, FrameworkElement> _nativeElements = new();
	private readonly Dictionary<ControlNode, Border> _nodeHosts = new();
	private readonly Dictionary<ControlNode, Rectangle> _hostSelectionAdorners = new();
	private Border? _draggingHost;
	private FrameworkElement? _nativePreviewRoot;
	private Rect _dragOriginalNativeBounds = Rect.Empty;
	private double _dragVisualWidth;
	private double _dragVisualHeight;
	private Rectangle? _placementPreview;
	private bool _overlaySyncPending;

	private const double DesignPreviewBaseWidth = 900;
	private const double DesignPreviewBaseHeight = 720;
	private const double MinDesignZoom = 0.25;
	private const double MaxDesignZoom = 3.00;
	private double _designZoom = 1.0;

	private readonly string[] _tips =
	{
		"直接从工具箱拖到画布，无需先选中控件",
		"单击画布空白区域即可选择 Window（窗口）",
		"Ctrl+S 会先提交当前正在编辑的属性，再保存到 XAML",
		"单行输入框可以按 Enter 提交；多行输入框会保留换行行为",
		"拖动控件时更新位置；宽度和高度可在右侧属性面板实时修改",
		"保存时会为每个控件补齐 Click 或 Tapped 事件",
		"Visual Studio 检测到外部 XAML 修改后，可以选择重新加载",
		"Grid（网格布局）目前优先使用相对布局，每个单元格按 * 均分",
		"不支持的复杂 XAML 结构会尽量保留，而不是静默删除",
		"属性面板会根据当前控件类型自动启用或禁用相关编辑项",
		"删除行或列前，Designer 会检查是否会影响现有控件",
		"窗口标题栏预览会跟随 Window 的 Title（标题）属性更新",
		"自动保存默认关闭，可在设置 → 通用中启用",
		"拖动控件时，按住 Ctrl 可以复制控件而不是移动",
		"拖动控件时，按住 Alt 可以在网格中自由移动，而不受单元格限制",
		"拖动控件时，按住 Ctrl+Shift 可以复制并锁定方向",
		"在属性面板中，输入框支持粘贴 XAML 片段来设置复杂属性",
		"在属性面板中，ComboBox 下拉列表会根据控件类型显示可选值",
		"XAML Sub已于2026/10/1发布首个Release啦！（WoW）",
		"如果你喜欢这个工具，请考虑给MsintX点个Star，或者在GitHub上提交Bug和建议",
		"你知道吗？MsintX是一名初一生！还tm寄宿！",
		"Hello Coder!",
		"项目立项于2026.9.19！",
		"其实这个软件的作者MsintX是不仅是音游入，还是名wmc！",
		"学好数理化，走遍天下都不怕！",
		"你醒啦？请你复习一下数轴、相反数、绝对值、倒数、乘方、有理数、有理数的加 减 乘 除还有三元一次方程吧！",
		"我要网暴这个C#，回来吧面向过程，我最骄傲的信仰↑↑",
		"C++ ×\nCNM √",
		"闹吃vs古振兴，谁才是赢家？",
		"xxx xxx xxxxxxx",
		"x x xxx",
		"VS自动补全别捣乱行不",
		"我要的面向var编程哪去了，为什么我写var(var)var会报错",
		"请投入硬币，要开始了哟，欢迎回来！",
		"我是臀萌 + 句号",
		"UWP好看？跟我的SandBox、生命周期、Store分发、旁加载说去吧",
		"没人觉得Metro Design(Modern UI)很好看嘛",
		"WinUI 3的生命周期和UWP的生命周期不一样，WinUI 3的生命周期是Win32的生命周期",
		"前面忘了，中间忘了，后面忘了",
		"我把春、观沧海、次北固山下、闻王昌龄左迁龙标遥有此寄、天净沙·秋思都背完了！！",
		"Visual Studio自动补全那么牛逼能不能帮我把2000+ error的报错补全成not error found啊",
		"BiliBili关注MsintX谢谢喵，YouTube订阅MsintX谢谢喵",
		"我想要一个全是“awmc”的评论区",
		"A：你这tip怎么内嵌在cs里面啊\nQ：json多难写，解析json的NuGet引用多麻烦，你就忍忍呗（手动doge",
		"像素方块的硬核才是王道你的卡通画风根本没技巧\n萌趣的世界才受大众喜爱你的硬核玩法早就被时代落败",
		"雷军！金凡！",
		"7月份才想起澎湃解bl通道在1月份就关了，喂我花生喂我花生",
		"这种粉丝少的up整活最狠了",
		"中秋节当天，有人在吃月饼，有人在赏月，而我就不一样了，我在Phigros 4.0.0 Update",
		"“难道没人觉得一段文字加上双引号和英文句号会很高级吗.”",
		"VS的IntelliSense错误列表就是lj",
		"截至目前，XAML Sub已经有超过0人的下载量了！",
		"各位Watcher们能不能帮我写完作业，能写完的自动获得美国核弹发射权",
		"你知道吗？XAML Sub的第一个Release预计在中秋发布！但由于实际条件限制（其实就是还有一大堆新功能和bug），XAML Sub的第一个Release实际在10.1发布！"
	};

	private int _lastTipIndex = -1;
	private DispatcherQueueTimer? _tipTimer;

	public MainWindow() {
		InitializeComponent();
		// Use the Windows App SDK custom TitleBar control so the app content extends into
		// the title-bar area while Windows keeps ownership of the caption buttons.
		ExtendsContentIntoTitleBar = true;
		SetTitleBar(AppTitleBar);
		ToolboxList.ItemsSource = ToolboxItems;

		ConfigureWinUiTransitions();
		InitializeSettingsUi();
		ApplyWindowBackdrop(_settings.WindowBackdrop);

		// The two large workspaces are kept in a collapsed pool until the window
		// is initialized, then swapped through a ContentControl so WinUI's
		// built-in ContentThemeTransition can animate entering and leaving Settings.
		WorkspacePool.Children.Remove(EditorWorkspace);
		WorkspacePool.Children.Remove(SettingsWorkspace);
		MainWorkspaceHost.Content = EditorWorkspace;
		SettingsBackButton.Visibility = Visibility.Collapsed;

		_windowSelected = true;
		UpdatePositionToolsUi();
		ShowRandomTip();
		_tipTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
		if (_tipTimer is not null) {
			_tipTimer.Interval = TimeSpan.FromMinutes(1);
			_tipTimer.Tick += TipTimer_Tick;
			_tipTimer.Start();
		}
		Closed += MainWindow_Closed;
		UpdateUi();
	}

	private void TipTimer_Tick(DispatcherQueueTimer sender, object args) {
		ShowRandomTip();
	}

	private void ShowRandomTip() {
		if (_tips.Length == 0) return;

		var next = _tips.Length == 1
			? 0
			: Random.Shared.Next(_tips.Length);

		if (_tips.Length > 1) {
			while (next == _lastTipIndex)
				next = Random.Shared.Next(_tips.Length);
		}

		_lastTipIndex = next;
		if (TipText is not null)
			TipText.Text = $"Tip: {_tips[next]}";
	}

	private void MainWindow_Closed(object sender, WindowEventArgs args) {
		if (_tipTimer is not null) {
			_tipTimer.Stop();
			_tipTimer.Tick -= TipTimer_Tick;
		}
	}

	private async void OpenXaml_Click(object sender, RoutedEventArgs e) {
		var picker = new FileOpenPicker();
		picker.FileTypeFilter.Add(".xaml");
		InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
		var file = await picker.PickSingleFileAsync();
		if (file is null) return;
		await LoadDocumentAsync(file.Path);
	}

	private async void OpenProject_Click(object sender, RoutedEventArgs e) {
		var picker = new FileOpenPicker();
		picker.FileTypeFilter.Add(".csproj");
		InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
		var project = await picker.PickSingleFileAsync();
		if (project is null) return;

		var files = ProjectLocator.FindXamlFiles(project.Path);
		if (files.Count == 0) {
			await ShowErrorAsync("项目中没有可编辑的 XAML 文件App.xaml 以及 bin/obj 下文件已排除");
			return;
		}

		var combo = new ComboBox { ItemsSource = files, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
		var dialog = CreateDialog(
			title: "选择要编辑的 XAML",
			content: combo,
			primaryButtonText: "打开",
			closeButtonText: "取消",
			defaultButton: ContentDialogButton.Primary);
		if (await dialog.ShowAsync() == ContentDialogResult.Primary && combo.SelectedItem is string path)
			await LoadDocumentAsync(path);
	}

	private async Task LoadDocumentAsync(string path) {
		try {
			if (XamlDocumentService.IsBlankFile(path)) {
				var createDialog = CreateDialog(
					title: "XAML 文件为空",
					content: "这个 XAML 文件目前没有任何内容\n\n要为它创建一个默认的 1 × 1 Grid（网格布局）吗？",
					primaryButtonText: "创建默认 Grid",
					closeButtonText: "取消",
					defaultButton: ContentDialogButton.Primary);

				if (await createDialog.ShowAsync() != ContentDialogResult.Primary) {
					StatusText.Text = "已取消打开空白 XAML";
					return;
				}

				await File.WriteAllTextAsync(path, XamlDocumentService.DefaultGridXaml);
			}

			_document = _xaml.Load(path);
			_document.CodeBehindPath = ProjectLocator.FindAssociatedCodeBehind(path, _document.XClass);
			_savedSnapshot = _document.Clone();
			_selected = null;
			_clipboardNode = null;
			_windowSelected = true;
			_pendingTool = null;
			_hasLastCanvasPointerPosition = false;
			_undo.Clear();
			_redo.Clear();
			RenderCanvas();
			UpdateUi();
			var ignoredHint = _document.IgnoredElementCount > 0
				? $"；有 {_document.IgnoredElementCount} 个非设计控件/容器已保留但不参与编辑"
				: "";
			var sameNameCodeBehind = ProjectLocator.FindSameNameCodeBehind(path);
			StatusText.Text = _document.CodeBehindPath is not null
				? $"已打开：{path}{ignoredHint}"
				: sameNameCodeBehind is not null
					? $"已打开：{path}；找到同名 .xaml.cs，但当前没有明确关联{ignoredHint}"
					: $"已打开：{path}；未找到同名 .xaml.cs{ignoredHint}";
		} catch (Exception ex) {
			await ShowErrorAsync(ex.Message);
		}
	}

	private async void Save_Click(object sender, RoutedEventArgs e) => await SaveDocumentAsync();

	private bool PrepareCodeBehindForSave(out string errorMessage)
	{
		errorMessage = string.Empty;
		if (_document is null) return false;

		// Check the same-name candidate first, independently of whether it is associated.
		var sameNameCandidate = ProjectLocator.FindSameNameCodeBehind(_document.FilePath);
		var associated = ProjectLocator.FindAssociatedCodeBehind(_document.FilePath, _document.XClass);

		if (associated is not null)
		{
			_document.CodeBehindPath = associated;
			return true;
		}

		if (_settings.BroadSaveCheckEnabled && sameNameCandidate is not null)
		{
			_document.CodeBehindPath = sameNameCandidate;
			// Broad mode deliberately treats the same-name .xaml.cs as authoritative,
			// even when an existing x:Class points somewhere else or is absent.
			_document.XClass = ProjectLocator.GetBroadSaveClassIdentity(_document.FilePath, sameNameCandidate);
			return true;
		}

		_document.CodeBehindPath = null;
		var missingXClass = string.IsNullOrWhiteSpace(_document.XClass);
		if (sameNameCandidate is not null)
		{
			errorMessage = missingXClass
				? "保存失败：当前 XAML 没有 x:Class，因此无法绑定到 code-behind\n\n但在同目录下找到同名 .xaml.cs；如要让设计器建立这种关联，请在设置内开启“宽泛的保存检查”"
				: "保存失败：没有找到与当前 x:Class 关联的逻辑文件\n\n但在同目录下找到同名 .xaml.cs；如要允许设计器使用它，请在设置内开启“宽泛的保存检查”";
		}
		else
		{
			errorMessage = missingXClass
				? "保存失败：当前 XAML 没有 x:Class，因此无法绑定到 code-behind"
				: "保存失败：没有找到关联的逻辑文件";
		}
		return false;
	}

	private async Task SaveDocumentAsync()
	{
		if (_document is null) return;
		try
		{
			await CommitFocusedEditorAsync();
			if (!PrepareCodeBehindForSave(out var saveError))
			{
				await ShowCodeBehindSaveFallbackAsync(saveError);
				return;
			}
			await _writeGate.WaitAsync();
			try
			{
				_document.Normalize();
				_document.RelativeFreePositioning = IsCustomPanelStrategy();
				await new WriteBackService(_xaml, _csharp).WriteAsync(_document, _savedSnapshot);
				_savedSnapshot = _document.Clone();
				StatusText.Text = $"已保存：{_document.FilePath}；每个控件的事件代码与 Window Loaded 已同步";
				UpdateUi();
			}
			finally
			{
				_writeGate.Release();
			}
		}
		catch (Exception ex)
		{
			await ShowErrorAsync($"保存失败：{ex.Message}");
		}
	}

	private async void Reload_Click(object sender, RoutedEventArgs e) {
		if (_document is null) return;
		if (_document.IsDirty) {
			var dialog = CreateDialog(
				title: "重新加载",
				content: "当前有未保存的内存修改，重新加载会丢弃这些修改",
				primaryButtonText: "重新加载",
				closeButtonText: "取消",
				defaultButton: ContentDialogButton.Close);
			if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
		}
		await LoadDocumentAsync(_document.FilePath);
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		if (_settingsTransitioning) return;

		if (ReferenceEquals(MainWorkspaceHost.Content, SettingsWorkspace))
			await CloseSettingsAsync();
		else
			await OpenSettingsAsync();
	}

	private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
	{
		if (_settingsTransitioning) return;
		SettingsBackButton.IsChecked = false;
		_ = CloseSettingsAsync();
	}

	private async Task OpenSettingsAsync()
	{
		if (_settingsTransitioning || ReferenceEquals(MainWorkspaceHost.Content, SettingsWorkspace)) return;
		_settingsTransitioning = true;
		try
		{
			SettingsWorkspace.Visibility = Visibility.Visible;
			SettingsBackButton.Visibility = Visibility.Visible;
			SettingsBackButton.IsChecked = true;
			SettingsBackButton.Width = 0;
			SettingsBackButton.Opacity = 0;
			MainWorkspaceHost.Content = SettingsWorkspace;
			if (!_settingsPageReady)
			{
				ShowSettingsPage("General");
				_settingsPageReady = true;
			}
			SettingsButton.Label = "主页";
			await AnimateSettingsChromeAsync(true);
		}
		finally
		{
			_settingsTransitioning = false;
		}
	}

	private async Task CloseSettingsAsync()
	{
		if (_settingsTransitioning || !ReferenceEquals(MainWorkspaceHost.Content, SettingsWorkspace)) return;
		_settingsTransitioning = true;
		try
		{
			SettingsBackButton.IsChecked = false;
			await AnimateSettingsChromeAsync(false);
			MainWorkspaceHost.Content = EditorWorkspace;
			SettingsBackButton.Visibility = Visibility.Collapsed;
			SettingsButton.Label = "设置";
		}
		finally
		{
			_settingsTransitioning = false;
		}
	}

	private async Task AnimateSettingsChromeAsync(bool enteringSettings)
	{
		var duration = GetMotionDuration();
		var easing = new CubicEase { EasingMode = enteringSettings ? EasingMode.EaseOut : EasingMode.EaseIn };
		var widthAnimation = new DoubleAnimation
		{
			From = enteringSettings ? 0 : SettingsBackButton.Width,
			To = enteringSettings ? 40 : 0,
			Duration = duration,
			EasingFunction = easing
		};
		var opacityAnimation = new DoubleAnimation
		{
			From = enteringSettings ? 0 : SettingsBackButton.Opacity,
			To = enteringSettings ? 1 : 0,
			Duration = duration,
			EasingFunction = easing
		};

		var storyboard = new Storyboard();
		storyboard.Children.Add(widthAnimation);
		storyboard.Children.Add(opacityAnimation);
		Storyboard.SetTarget(widthAnimation, SettingsBackButton);
		Storyboard.SetTargetProperty(widthAnimation, "Width");
		Storyboard.SetTarget(opacityAnimation, SettingsBackButton);
		Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		void Completed(object? sender, object? args) => completion.TrySetResult(null);
		storyboard.Completed += Completed;
		storyboard.Begin();
		try
		{
			await completion.Task;
		}
		finally
		{
			storyboard.Completed -= Completed;
		}

		if (!enteringSettings)
			SettingsBackButton.Width = 0;
	}

	private static Duration GetMotionDuration()
	{
		if (Application.Current.Resources.TryGetValue("ControlNormalAnimationDuration", out var resource) &&
			resource is Duration duration)
			return duration;

		return new Duration(TimeSpan.FromMilliseconds(250));
	}

	private void ConfigureWinUiTransitions()
	{
		var workspaceTransitions = new TransitionCollection();
		workspaceTransitions.Add(new ContentThemeTransition());
		MainWorkspaceHost.ContentTransitions = workspaceTransitions;

		var pageTransitions = new TransitionCollection();
		pageTransitions.Add(new ContentThemeTransition());
		SettingsPageHost.ContentTransitions = pageTransitions;
	}

	private void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
	{
		var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
		if (!string.IsNullOrWhiteSpace(tag))
			ShowSettingsPage(tag!);
	}

	private void ShowSettingsPage(string tag)
	{
		UIElement? next = tag switch
		{
			"Design" => DesignSettingsPanel,
			"Experimental" => ExperimentalSettingsPanel,
			"About" => AboutSettingsPanel,
			_ => GeneralSettingsPanel
		};

		if (ReferenceEquals(SettingsPageHost.Content, next)) return;

		// The panels live in a collapsed pool at load time. Once detached, a panel
		// can move directly between ContentControl states without another parent.
		if (next is FrameworkElement nextElement)
			nextElement.Visibility = Visibility.Visible;

		SettingsPageHost.Content = next;
	}

	private void InitializeSettingsUi()
	{
		_ignoreSettingsChanges = true;
		try
		{
			SelectSettingsCombo(WindowBackdropBox, _settings.WindowBackdrop);
			SelectSettingsCombo(PositioningModeBox, _settings.PositioningMode);
			SelectSettingsCombo(RelativeStrategyBox, _settings.RelativeStrategy);
			SelectSettingsCombo(AbsoluteStrategyBox, _settings.AbsoluteStrategy);
			AutoSaveToggle.IsOn = _settings.AutoSaveEnabled;
			BroadSaveCheckToggle.IsOn = _settings.BroadSaveCheckEnabled;
			HighFidelityPreviewToggle.IsOn = _settings.HighFidelityPreviewEnabled;
			UpdatePositionStrategyVisibility();
			UpdatePositionToolsUi();

			SettingsPagePool.Children.Remove(GeneralSettingsPanel);
			SettingsPagePool.Children.Remove(DesignSettingsPanel);
			SettingsPagePool.Children.Remove(ExperimentalSettingsPanel);
			SettingsPagePool.Children.Remove(AboutSettingsPanel);
			ShowSettingsPage("General");
			_settingsPageReady = true;
			SettingsNavigation.SelectedItem = SettingsGeneralItem;
		}
		finally
		{
			_ignoreSettingsChanges = false;
		}
	}

	private void AutoSaveToggle_Toggled(object sender, RoutedEventArgs e)
	{
		if (_ignoreSettingsChanges || sender is not ToggleSwitch toggle)
			return;

		_settings.AutoSaveEnabled = toggle.IsOn;
		UpdateUi();
		if (toggle.IsOn && _document is not null && _document.IsDirty)
			_ = AutoWriteAsync();
	}

	private void BroadSaveCheckToggle_Toggled(object sender, RoutedEventArgs e)
	{
		if (_ignoreSettingsChanges || sender is not ToggleSwitch toggle)
			return;

		_settings.BroadSaveCheckEnabled = toggle.IsOn;
	}

	private void HighFidelityPreviewToggle_Toggled(object sender, RoutedEventArgs e)
	{
		if (_ignoreSettingsChanges || sender is not ToggleSwitch toggle)
			return;

		_settings.HighFidelityPreviewEnabled = toggle.IsOn;
		UpdateZoomHint();
		RenderCanvas();
	}

	private void SettingsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_ignoreSettingsChanges || sender is not ComboBox combo || combo.Tag is not string key)
			return;

		var selected = combo.SelectedItem as ComboBoxItem;
		var value = selected?.Tag?.ToString() ?? selected?.Content?.ToString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(value))
			return;

		switch (key)
		{
			case "WindowBackdrop":
				_settings.WindowBackdrop = value;
				ApplyWindowBackdrop(value);
				break;
			case "PositioningMode":
				ApplyPositioningStrategyChange(value, null);
				break;
			case "RelativeStrategy":
				ApplyPositioningStrategyChange(null, value);
				break;
			case "AbsoluteStrategy":
				_settings.AbsoluteStrategy = value;
				UpdatePositionToolsUi();
				break;
		}
	}

	private void UpdatePositionStrategyVisibility()
	{
		var relative = string.Equals(_settings.PositioningMode, AppSettingsService.Relative, StringComparison.Ordinal);
		RelativeStrategyPanel.Visibility = relative ? Visibility.Visible : Visibility.Collapsed;
		AbsoluteStrategyPanel.Visibility = relative ? Visibility.Collapsed : Visibility.Visible;
	}

	private static void SelectSettingsCombo(ComboBox combo, string value)
	{
		combo.SelectedIndex = -1;
		for (var i = 0; i < combo.Items.Count; i++)
		{
			if (combo.Items[i] is ComboBoxItem item &&
				string.Equals(item.Tag?.ToString() ?? item.Content?.ToString(), value, StringComparison.Ordinal))
			{
				combo.SelectedIndex = i;
				return;
			}
		}
	}

	private void ApplyWindowBackdrop(string material)
	{
		SystemBackdrop = material switch
		{
			AppSettingsService.Acrylic => new DesktopAcrylicBackdrop(),
			AppSettingsService.None => null,
			_ => new MicaBackdrop()
		};

		// A NavigationView pane is an in-window surface. Keep it connected to the
		// selected window material instead of letting the collapsed/expanded pane
		// fall back to a hard-coded or fully transparent background. Mica reveals
		// the window backdrop; Acrylic uses WinUI's theme-aware in-app Acrylic brush;
		// None uses the normal theme background.
		if (string.Equals(material, AppSettingsService.Acrylic, StringComparison.Ordinal))
		{
			SettingsNavigation.Background = Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"] as Brush
				?? ThemeBrush("DesignerCardBackgroundBrush");
		}
		else if (string.Equals(material, AppSettingsService.None, StringComparison.Ordinal))
		{
			SettingsNavigation.Background = ThemeBrush("DesignerPageBackgroundBrush");
		}
		else
		{
			SettingsNavigation.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
		}
	}

	private void Toolbox_DragItemsStarting(object sender, DragItemsStartingEventArgs e) {
		if (e.Items.FirstOrDefault() is not ToolboxItem item)
			return;

		e.Data.SetText(item.TypeName);
		e.Data.Properties.Title = item.DisplayName;
		e.Data.Properties.Description = "拖到设计画布中以放置控件";
		_pendingTool = item;
		StatusText.Text = $"正在拖动 {item.DisplayName}：松手放置到画布位置";
		RenderCanvas();
	}

	private void Toolbox_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) {
		if (args.DropResult == DataPackageOperation.None) {
			_pendingTool = null;
			RemovePlacementPreview();
			StatusText.Text = "已取消拖动";
			RenderCanvas();
		}
	}

	private void DesignCanvas_DragOver(object sender, DragEventArgs e) {
		if (_document is null || !e.DataView.Contains(StandardDataFormats.Text)) {
			e.AcceptedOperation = DataPackageOperation.None;
			return;
		}

		e.AcceptedOperation = DataPackageOperation.Copy;
		var point = e.GetPosition(EditorOverlayCanvas);
		_hoverPoint = point;
		(_hoverRow, _hoverColumn) = PointToCell(point);
		UpdatePlacementPreviewForPoint(point);
		e.Handled = true;
	}

	private void DesignCanvas_DragLeave(object sender, DragEventArgs e) {
		if (_dragging) return;
		RemovePlacementPreview();
	}

	private async void DesignCanvas_Drop(object sender, DragEventArgs e) {
		if (_document is null || !e.DataView.Contains(StandardDataFormats.Text))
			return;

		try {
			var typeName = await e.DataView.GetTextAsync();
			var item = ToolboxItems.FirstOrDefault(t => t.TypeName == typeName);
			if (item is null)
				return;

			CommitHistory();
			var point = e.GetPosition(EditorOverlayCanvas);
			var node = CreateNodeForPlacement(item.TypeName, point);
			_document.Nodes.Add(node);
			_document.IsDirty = true;
			_pendingTool = null;
			Select(node);
			await AutoWriteAsync();
			e.AcceptedOperation = DataPackageOperation.Copy;
			e.Handled = true;
		} catch (Exception ex) {
			_pendingTool = null;
			await ShowErrorAsync($"放置控件失败：{ex.Message}");
		} finally {
			RemovePlacementPreview();
			RenderCanvas();
		}
	}

	private void UpdatePlacementPreviewForPoint(Point point)
	{
		if (_document is null || _dragging || _pendingTool is null) return;

		double width;
		double height;
		double left;
		double top;

		if (IsGridRelativeStrategy())
		{
			var cellWidth = EditorOverlayCanvas.Width / Math.Max(1, _document.Columns);
			var cellHeight = EditorOverlayCanvas.Height / Math.Max(1, _document.Rows);
			width = Math.Max(8, cellWidth - 6);
			height = Math.Max(8, cellHeight - 6);
			left = _hoverColumn * cellWidth + 3;
			top = _hoverRow * cellHeight + 3;
		}
		else
		{
			width = DefaultWidth(_pendingTool.TypeName);
			height = DefaultHeight(_pendingTool.TypeName);
			var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
			left = Math.Clamp(point.X - width / 2, 0, Math.Max(0, surfaceWidth - width));
			top = Math.Clamp(point.Y - height / 2, 0, Math.Max(0, surfaceHeight - height));
		}

		if (_placementPreview is null)
		{
			_placementPreview = new Rectangle
			{
				RadiusX = 6,
				RadiusY = 6,
				Stroke = ThemeBrush("DesignerAccentBrush"),
				StrokeThickness = 2,
				Fill = new SolidColorBrush(PreviewColor(28, 0, 120, 215)),
				IsHitTestVisible = false
			};
			EditorOverlayCanvas.Children.Add(_placementPreview);
		}

		_placementPreview.Width = width;
		_placementPreview.Height = height;
		Canvas.SetLeft(_placementPreview, left);
		Canvas.SetTop(_placementPreview, top);
	}

	private void RemovePlacementPreview()
	{
		if (_placementPreview is null) return;
		EditorOverlayCanvas.Children.Remove(_placementPreview);
		_placementPreview = null;
	}

	private void DesignCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) {
		if (_document is null)
			return;

		var point = e.GetCurrentPoint(DesignPreviewScrollViewer);
		var ctrlDown = InputKeyboardSource
			.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
			.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

		if (!ctrlDown || point.Properties.MouseWheelDelta == 0)
			return;

		var oldZoom = _designZoom;
		var step = point.Properties.MouseWheelDelta > 0 ? 1.1 : 1.0 / 1.1;
		_designZoom = Math.Clamp(_designZoom * step, MinDesignZoom, MaxDesignZoom);

		if (Math.Abs(_designZoom - oldZoom) < 0.0001) {
			e.Handled = true;
			return;
		}

		UpdateDesignZoom();
		e.Handled = true;
	}

	private void DesignCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) {
		if (_document is null) return;
		EditorOverlayCanvas.Focus(FocusState.Pointer);
		var point = e.GetCurrentPoint(EditorOverlayCanvas).Position;
		_lastCanvasPointerPosition = point;
		_hasLastCanvasPointerPosition = true;

		if (_pendingTool is not null) {
			CommitHistory();
			var node = CreateNodeForPlacement(_pendingTool.TypeName, point);
			_document.Nodes.Add(node);
			_pendingTool = null;
			_document.IsDirty = true;
			Select(node);
			_ = AutoWriteAsync();
			e.Handled = true;
			return;
		}

		var hit = FindNodeAt(point);
		if (hit is null) {
			_selected = null;
			_windowSelected = true;
			RenderCanvas();
			UpdateUi();
			return;
		}

		Select(hit);
		_dragOriginalRow = hit.Row;
		_dragOriginalColumn = hit.Column;
		_dragOriginalRowSpan = hit.RowSpan;
		_dragOriginalColumnSpan = hit.ColumnSpan;
		_dragStartPoint = point;
		_lastDragPoint = point;
		_dragOriginalOffsetX = hit.OffsetX;
		_dragOriginalOffsetY = hit.OffsetY;
		if (IsCanvasStrategy() && _nativeElements.TryGetValue(hit, out var nativeAtPointer))
		{
			var nativeLeft = Canvas.GetLeft(nativeAtPointer);
			var nativeTop = Canvas.GetTop(nativeAtPointer);
			_dragOriginalOffsetX = double.IsFinite(nativeLeft) ? nativeLeft : 0;
			_dragOriginalOffsetY = double.IsFinite(nativeTop) ? nativeTop : 0;
		}
		CommitHistory();
		_dragOriginalNativeBounds = TryGetNativeBounds(hit, out var hitBounds) ? hitBounds : GetNodeVisualBounds(hit);
		_dragCurrentVisualLeft = _dragOriginalNativeBounds.X;
		_dragCurrentVisualTop = _dragOriginalNativeBounds.Y;
		_dragVisualWidth = Math.Max(1, _dragOriginalNativeBounds.Width);
		_dragVisualHeight = Math.Max(1, _dragOriginalNativeBounds.Height);
		_nodeHosts.TryGetValue(hit, out _draggingHost);
		_dragging = true;
		EditorOverlayCanvas.CapturePointer(e.Pointer);
		e.Handled = true;
	}

	private void DesignCanvas_PointerMoved(object sender, PointerRoutedEventArgs e) {
		if (_document is null) return;
		var point = e.GetCurrentPoint(EditorOverlayCanvas).Position;
		_lastCanvasPointerPosition = point;
		_hasLastCanvasPointerPosition = true;

		// PointerMoved fires at a very high frequency. Avoid rebuilding the complete
		// visual tree when the pointer is simply hovering over the design surface.
		if (!_dragging && _pendingTool is null)
			return;

		_hoverPoint = point;
		var (row, col) = PointToCell(point);
		_hoverRow = row;
		_hoverColumn = col;

		if (_dragging && _selected is not null)
		{
			if (UsesFreePositioning())
			{
				// Follow XAML Lab's drag pattern: consume the pointer movement as an
				// incremental delta and update the model/layout value directly. This is
				// more stable than repeatedly deriving a position from the original
				// bounds while the parent panel is re-arranging its child.
				var dx = point.X - _lastDragPoint.X;
				var dy = point.Y - _lastDragPoint.Y;
				if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
				{
					MoveFreePositionByDelta(_selected, dx, dy);
				}
			}

			else
			{
				row = Math.Clamp(row, 0, Math.Max(0, _document.Rows - _selected.RowSpan));
				col = Math.Clamp(col, 0, Math.Max(0, _document.Columns - _selected.ColumnSpan));
				_selected.Row = row;
				_selected.Column = col;
			}
			_document.IsDirty = true;
			if (_nativeElements.TryGetValue(_selected, out var movedNative))
				ApplyNativeLayoutForDrag(movedNative, _selected);

			// Reposition the existing visual. Recreating every native WinUI control on
			// each PointerMoved event was the main source of drag-time stutter.
			if (_draggingHost is not null)
			{
				_draggingHost.Width = _dragVisualWidth;
				_draggingHost.Height = _dragVisualHeight;
				SyncOverlayBounds(_selected, _draggingHost);
			}
			return;
		}

		UpdatePlacementPreviewForPoint(point);
	}

	private void MoveFreePositionByDelta(ControlNode node, double dx, double dy)
	{
		// Mirror XAML Lab's Resizer: accumulate the effective pointer delta instead of
		// recomputing from a moving layout rectangle. The effective pointer position is
		// advanced only by the amount the element actually moved at an edge.
		var previousLeft = _dragCurrentVisualLeft;
		var previousTop = _dragCurrentVisualTop;
		var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
		_dragCurrentVisualLeft = Math.Clamp(
			_dragCurrentVisualLeft + dx,
			0,
			Math.Max(0, surfaceWidth - _dragVisualWidth));
		_dragCurrentVisualTop = Math.Clamp(
			_dragCurrentVisualTop + dy,
			0,
			Math.Max(0, surfaceHeight - _dragVisualHeight));

		var appliedDx = _dragCurrentVisualLeft - previousLeft;
		var appliedDy = _dragCurrentVisualTop - previousTop;

		if (IsCanvasStrategy() || IsCustomPanelStrategy())
		{
			// Both Canvas and free-relative positioning are directly relative to the root
			// design surface. Custom mode deliberately does NOT convert through Row/Column.
			node.OffsetX = _dragCurrentVisualLeft;
			node.OffsetY = _dragCurrentVisualTop;
		}
		else
		{
			SetNodeVisualOrigin(node, _dragCurrentVisualLeft, _dragCurrentVisualTop, persist: false);
		}

		if (_nativeElements.TryGetValue(node, out var native))
			ApplyNativeLayoutForDrag(native, node);
		_document!.IsDirty = true;
		_lastDragPoint = new Point(_lastDragPoint.X + appliedDx, _lastDragPoint.Y + appliedDy);
		QueueOverlaySync();
	}

	private void ApplyNativeLayoutForDrag(FrameworkElement native, ControlNode node)
	{
		if (IsCanvasStrategy())
		{
			Canvas.SetLeft(native, node.OffsetX);
			Canvas.SetTop(native, node.OffsetY);
		}
		else if (IsCustomPanelStrategy() && _nativePreviewRoot is Grid customGrid)
		{
			ApplyCanonicalFreeRelativeLayout(customGrid, native, node);
		}
		else if (_nativePreviewRoot is Grid)
		{
			Grid.SetRow(native, Math.Clamp(node.Row, 0, Math.Max(0, (_document?.Rows ?? 1) - 1)));
			Grid.SetColumn(native, Math.Clamp(node.Column, 0, Math.Max(0, (_document?.Columns ?? 1) - 1)));
			Grid.SetRowSpan(native, Math.Max(1, node.RowSpan));
			Grid.SetColumnSpan(native, Math.Max(1, node.ColumnSpan));
		}
		else if (_nativePreviewRoot is DesignerDockPanel)
		{
			DesignerDockPanel.SetDock(native, node.Dock ?? "Left");
		}

		QueueOverlaySync();
	}

	private async void DesignCanvas_PointerReleased(object sender, PointerRoutedEventArgs e) {
		if (_document is null || _selected is null || !_dragging) return;
		_dragging = false;
		_draggingHost = null;
		EditorOverlayCanvas.ReleasePointerCapture(e.Pointer);
		RemovePlacementPreview();

		if (UsesFreePositioning() && IsDockPanelStrategy())
			SnapDockToNearestEdge(_selected);

		if (UsesFreePositioning())
		{
			if (IsCanvasStrategy() && _nativeElements.TryGetValue(_selected, out var releasedNative) &&
				TryGetNativeBounds(_selected, out var currentBounds))
			{
				// Clamp the REAL visual rectangle, then convert back to Canvas.Left/Top.
				// This guards the exact release-time jump that occurs when Margin exists.
				var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
				var visualLeft = Math.Clamp(currentBounds.X, 0, Math.Max(0, surfaceWidth - currentBounds.Width));
				var visualTop = Math.Clamp(currentBounds.Y, 0, Math.Max(0, surfaceHeight - currentBounds.Height));
				var margin = releasedNative.Margin;
				_selected.OffsetX = visualLeft - margin.Left;
				_selected.OffsetY = visualTop - margin.Top;
				Canvas.SetLeft(releasedNative, _selected.OffsetX);
				Canvas.SetTop(releasedNative, _selected.OffsetY);
			}
			PersistVisualOffset(_selected);
		}

		var changed = _selected.Row != _dragOriginalRow || _selected.Column != _dragOriginalColumn ||
			_selected.RowSpan != _dragOriginalRowSpan || _selected.ColumnSpan != _dragOriginalColumnSpan ||
			Math.Abs(_selected.OffsetX - _dragOriginalOffsetX) > 0.1 || Math.Abs(_selected.OffsetY - _dragOriginalOffsetY) > 0.1;
		if (!changed && _undo.Count > 0) _undo.Pop();

		await AutoWriteAsync();
		StatusText.Text = _document.IsDirty ? "有未保存变更" : "已保存";
		RenderCanvas();
	}

	private void DesignCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e) {
		if (!_dragging || _selected is null) return;
		_selected.Row = _dragOriginalRow;
		_selected.Column = _dragOriginalColumn;
		_selected.RowSpan = _dragOriginalRowSpan;
		_selected.ColumnSpan = _dragOriginalColumnSpan;
		_selected.OffsetX = _dragOriginalOffsetX;
		_selected.OffsetY = _dragOriginalOffsetY;
		_dragging = false;
		_draggingHost = null;
		RemovePlacementPreview();
		EditorOverlayCanvas.ReleasePointerCapture(e.Pointer);
		RenderCanvas();
		UpdateUi();
	}

	private void DesignCanvas_PointerExited(object sender, PointerRoutedEventArgs e) {
		if (_dragging || _pendingTool is not null) return;
		RenderCanvas();
	}


	private void DesignCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e) {
		_pendingTool = null;
		RenderCanvas();
		StatusText.Text = "已取消放置/拖动";
	}

	private void DesignCanvas_KeyDown(object sender, KeyRoutedEventArgs e) {
		if (e.Key == Windows.System.VirtualKey.Escape) {
			_pendingTool = null;
			RenderCanvas();
			StatusText.Text = "已取消放置/拖动";
			e.Handled = true;
		} else if (e.Key == Windows.System.VirtualKey.Delete && _selected is not null) {
			DeleteSelected();
			e.Handled = true;
		}
	}

	private async void Editor_KeyDown(object sender, KeyRoutedEventArgs e) {
		if (e.Key != Windows.System.VirtualKey.Enter || sender is not TextBox box || box.AcceptsReturn)
			return;

		e.Handled = true;
		if (box == NameBox)
			await CommitNameAsync();
		else
			await CommitPropertyTextBoxAsync(box);
	}

	private async void RootLayout_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		var xamlRoot = Content.XamlRoot;
		var focused = xamlRoot is null ? null : FocusManager.GetFocusedElement(xamlRoot);
		var textEditing = focused is TextBox || focused is PasswordBox || focused is RichEditBox || focused is AutoSuggestBox;
		if (textEditing)
			return;

		var ctrlDown = InputKeyboardSource
			.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
			.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

		if (ctrlDown && e.Key == Windows.System.VirtualKey.S)
		{
			e.Handled = true;
			await SaveDocumentAsync();
			return;
		}

		if (ctrlDown && e.Key == Windows.System.VirtualKey.C && _selected is not null)
		{
			_clipboardNode = _selected.Clone();
			StatusText.Text = $"已复制：{_selected.TypeName}";
			e.Handled = true;
			return;
		}

		if (ctrlDown && e.Key == Windows.System.VirtualKey.V && _clipboardNode is not null && _document is not null)
		{
			PasteCopiedControl();
			e.Handled = true;
			return;
		}

		if (e.Key == Windows.System.VirtualKey.Delete && _selected is not null)
		{
			DeleteSelected();
			e.Handled = true;
		}
	}

	private void PasteCopiedControl()
	{
		if (_document is null || _clipboardNode is null) return;

		CommitHistory();
		var node = _clipboardNode.Clone();
		node.Id = Guid.NewGuid();
		node.SourceKey = null;
		var newName = MakeUniqueName(node.TypeName);
		node.XName = newName;
		if (node.EventWasAutoGenerated || string.IsNullOrWhiteSpace(node.EventName))
		{
			node.EventKind = node.SupportsClick ? "Click" : "Tapped";
			node.EventName = newName + "_" + node.EventKind;
			node.EventWasAutoGenerated = true;
		}

		var point = _hasLastCanvasPointerPosition
			? _lastCanvasPointerPosition
			: (_selected is not null ? new Point(GetNodeVisualBounds(_selected).X + 24, GetNodeVisualBounds(_selected).Y + 24) : new Point(EditorOverlayCanvas.Width / 2, EditorOverlayCanvas.Height / 2));
		point = new Point(Math.Clamp(point.X, 0, EditorOverlayCanvas.Width), Math.Clamp(point.Y, 0, EditorOverlayCanvas.Height));

		if (IsGridRelativeStrategy())
		{
			(node.Row, node.Column) = PointToCell(point);
			node.Row = Math.Clamp(node.Row, 0, Math.Max(0, _document.Rows - node.RowSpan));
			node.Column = Math.Clamp(node.Column, 0, Math.Max(0, _document.Columns - node.ColumnSpan));
			node.OffsetX = 0;
			node.OffsetY = 0;
			node.DesignerPositionCustomized = false;
			node.Properties.Remove("Margin");
		}
		else
		{
			if (!IsCustomPanelStrategy())
				(node.Row, node.Column) = PointToCell(point);
			else
			{
				node.Row = 0;
				node.Column = 0;
			}
			var width = ParseSize(node.Width, DefaultWidth(node.TypeName));
			var height = ParseSize(node.Height, DefaultHeight(node.TypeName));
			SetNodeVisualOrigin(node, point.X - width / 2, point.Y - height / 2);
			if (IsDockPanelStrategy())
				node.Dock = FindNearestDockEdge(point);
		}

		_document.Nodes.Add(node);
		_document.IsDirty = true;
		Select(node);
		_ = AutoWriteAsync();
		StatusText.Text = $"已粘贴：{node.TypeName}";
	}

	private async Task CommitFocusedEditorAsync() {
		if (_ignorePropertyChanges)
			return;

		var xamlRoot = Content.XamlRoot;
		if (xamlRoot is null)
			return;

		if (FocusManager.GetFocusedElement(xamlRoot) is TextBox box) {
			if (box == NameBox)
				await CommitNameAsync();
			else
				await CommitPropertyTextBoxAsync(box);
		}
	}

	private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_ignorePropertyChanges || _document is null || sender is not TextBox box || box.Tag is not string key)
			return;
		if (key is not ("Width" or "Height"))
			return;

		if (!_sizeHistoryCommitted)
		{
			CommitHistory();
			_sizeHistoryCommitted = true;
		}

		var value = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
		if (IsWindowPropertyEditor(box))
		{
			if (value is null) _document.RootProperties.Remove(key);
			else _document.RootProperties[key] = value;
		}
		else if (_selected is not null)
		{
			if (key == "Width") _selected.Width = value;
			else _selected.Height = value;
		}
		else
		{
			return;
		}

		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void NameBox_LostFocus(object sender, RoutedEventArgs e) => _ = CommitNameAsync();

	private async Task CommitNameAsync() {
		if (_ignorePropertyChanges || _selected is null || _document is null) return;
		var newName = NameBox.Text.Trim();
		var oldName = _selected.XName;
		if (string.Equals(newName, oldName, StringComparison.Ordinal)) return;
		if (!string.IsNullOrWhiteSpace(newName) && _document.Nodes.Any(n => !ReferenceEquals(n, _selected) && n.XName == newName)) {
			await ShowErrorAsync($"x:Name 已存在：{newName}");
			RefreshProperties();
			return;
		}

		CommitHistory();

		if (string.IsNullOrWhiteSpace(newName)) {
			_selected.XName = null;
			if (_selected.EventWasAutoGenerated) {
				_selected.EventName = null;
				_selected.EventKind = null;
				_selected.EventWasAutoGenerated = false;
			}
		} else {
			_selected.XName = newName;
			if (_selected.EventName is null || _selected.EventWasAutoGenerated) {
				_selected.EventName = _selected.DerivedEventName;
				_selected.EventKind = _selected.DerivedEventKind;
				_selected.EventWasAutoGenerated = true;
			}
		}

		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		await AutoWriteAsync();
	}

	private async void PropertyBox_LostFocus(object sender, RoutedEventArgs e) {
		if (sender is not TextBox box) return;
		await CommitPropertyTextBoxAsync(box);
	}

	private async Task CommitPropertyTextBoxAsync(TextBox box) {
		if (_ignorePropertyChanges || _document is null || box.Tag is not string key)
			return;

		if (IsWindowPropertyEditor(box)) {
			var value = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
			_document.RootProperties.TryGetValue(key, out var rootOldValue);
			if (string.Equals(rootOldValue, value, StringComparison.Ordinal))
			{
				if (key is "Width" or "Height") _sizeHistoryCommitted = false;
				return;
			}
			if (key is "Width" or "Height") _sizeHistoryCommitted = false;
			CommitHistory();
			if (value is null) _document.RootProperties.Remove(key); else _document.RootProperties[key] = value;
			_document.IsDirty = true;
			UpdateUi();
			await AutoWriteAsync();
			return;
		}

		if (_selected is null) return;

		var propertyKey = key;
		var valueText = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
		string? oldText;
		if (propertyKey == "Width") oldText = _selected.Width;
		else if (propertyKey == "Height") oldText = _selected.Height;
		else if (propertyKey == "ContentOrText") oldText = _selected.Content;
		else oldText = _selected.Properties.TryGetValue(propertyKey, out var current) ? current : null;
		if (string.Equals(oldText, valueText, StringComparison.Ordinal))
		{
			if (propertyKey is "Width" or "Height") _sizeHistoryCommitted = false;
			return;
		}

		if (propertyKey is "Width" or "Height") _sizeHistoryCommitted = false;
		CommitHistory();
		switch (propertyKey) {
			case "Width": _selected.Width = valueText; break;
			case "Height": _selected.Height = valueText; break;
			case "ContentOrText": _selected.Content = valueText; break;
			case "Margin":
				if (IsCustomPanelStrategy() && TryParseMarginOffset(valueText, out var marginX, out var marginY))
				{
					// Margin is the canonical free-relative position. Route edits through the
					// same clamped visual-origin path used by dragging so the model, native
					// element and serialized XAML cannot diverge.
					SetNodeVisualOrigin(_selected, marginX, marginY);
				}
				else
				{
					_selected.Properties[propertyKey] = valueText ?? string.Empty;
				}
				break;
			default:
				_selected.Properties[propertyKey] = valueText ?? string.Empty;
				break;
		}
		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		await AutoWriteAsync();
	}

	private void PropertyCombo_Changed(object sender, SelectionChangedEventArgs e) {
		if (_ignorePropertyChanges || _document is null || sender is not ComboBox combo || combo.Tag is not string key)
			return;
		var value = (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
		value = string.IsNullOrWhiteSpace(value) ? null : value;

		if (IsWindowPropertyEditor(combo)) {
			_document.RootProperties.TryGetValue(key, out var rootOldValue);
			if (string.Equals(rootOldValue, value, StringComparison.Ordinal)) return;
			CommitHistory();
			if (value is null) _document.RootProperties.Remove(key); else _document.RootProperties[key] = value;
			_document.IsDirty = true;
			UpdateUi();
			_ = AutoWriteAsync();
			return;
		}

		if (_selected is null) return;
		string? oldValue;
		if (key == "HorizontalAlignment") oldValue = _selected.HorizontalAlignment;
		else if (key == "VerticalAlignment") oldValue = _selected.VerticalAlignment;
		else oldValue = _selected.Properties.TryGetValue(key, out var current) ? current : null;
		if (string.Equals(oldValue, value, StringComparison.Ordinal)) return;
		CommitHistory();
		switch (key) {
			case "HorizontalAlignment": _selected.HorizontalAlignment = value; break;
			case "VerticalAlignment": _selected.VerticalAlignment = value; break;
			default:
				_selected.Properties[key] = value ?? string.Empty;
				break;
		}
		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void GridValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) {
		if (_ignorePropertyChanges || _selected is null || _document is null || double.IsNaN(args.NewValue)) return;
		CommitHistory();
		var value = Math.Max(0, (int)args.NewValue);
		if (sender == RowBox) _selected.Row = Math.Min(value, _document.Rows - 1);
		else if (sender == ColumnBox) _selected.Column = Math.Min(value, _document.Columns - 1);
		else if (sender == RowSpanBox) _selected.RowSpan = Math.Max(1, value);
		else if (sender == ColumnSpanBox) _selected.ColumnSpan = Math.Max(1, value);
		_document.Normalize();
		_document.IsDirty = true;
		RenderCanvas();
		_ = AutoWriteAsync();
	}

	private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();

	private void DeleteSelected() {
		if (_document is null || _selected is null) return;
		CommitHistory();
		_document.Nodes.Remove(_selected);
		_selected = null;
		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void AddRow_Click(object sender, RoutedEventArgs e) => ChangeGrid(+1, 0);
	private void RemoveRow_Click(object sender, RoutedEventArgs e) => ChangeGrid(-1, 0);
	private void AddColumn_Click(object sender, RoutedEventArgs e) => ChangeGrid(0, +1);
	private void RemoveColumn_Click(object sender, RoutedEventArgs e) => ChangeGrid(0, -1);

	private void ChangeGrid(int rowDelta, int colDelta) {
		if (_document is null) return;
		if (!IsGridRelativeStrategy()) {
			StatusText.Text = "当前定位方式不是 Grid，相应的定位工具已显示在左侧";
			return;
		}
		if (!_document.GridSizingSupported) {
			StatusText.Text = "当前 Grid 使用 Auto 或固定尺寸，行列编辑已禁用以避免静默改变原布局";
			return;
		}
		var rows = Math.Max(1, _document.Rows + rowDelta);
		var cols = Math.Max(1, _document.Columns + colDelta);
		if (rows == _document.Rows && cols == _document.Columns) return;

		if (rows < _document.Rows) {
			var blocked = _document.Nodes.FirstOrDefault(n => n.Row >= rows || n.Row + n.RowSpan > rows);
			if (blocked is not null) {
				StatusText.Text = $"无法删除行：{blocked.TypeName} 位于第 {blocked.Row + 1} 行或跨越目标区域请先移动/缩小控件";
				return;
			}
		}
		if (cols < _document.Columns) {
			var blocked = _document.Nodes.FirstOrDefault(n => n.Column >= cols || n.Column + n.ColumnSpan > cols);
			if (blocked is not null) {
				StatusText.Text = $"无法删除列：{blocked.TypeName} 位于第 {blocked.Column + 1} 列或跨越目标区域请先移动/缩小控件";
				return;
			}
		}

		CommitHistory();
		_document.Rows = rows;
		_document.Columns = cols;
		_document.Normalize();
		_document.IsDirty = true;
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void Undo_Click(object sender, RoutedEventArgs e) {
		if (_document is null || _undo.Count == 0) return;
		_redo.Push(_document.Clone());
		_document.CopyFrom(_undo.Pop());
		_document.IsDirty = true;
		RestoreSelection();
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void Redo_Click(object sender, RoutedEventArgs e) {
		if (_document is null || _redo.Count == 0) return;
		_undo.Push(_document.Clone());
		_document.CopyFrom(_redo.Pop());
		_document.IsDirty = true;
		RestoreSelection();
		RenderCanvas();
		UpdateUi();
		_ = AutoWriteAsync();
	}

	private void CommitHistory() {
		if (_document is null) return;
		_undo.Push(_document.Clone());
		_redo.Clear();
		if (_undo.Count > 100) _undo.Pop();
	}

	private void RestoreSelection() {
		if (_selected is null || _document is null) return;
		_selected = _document.Nodes.FirstOrDefault(n => n.Id == _selected.Id);
	}

	private async Task AutoWriteAsync()
	{
		if (!_settings.AutoSaveEnabled)
			return;

		if (_document is null)
		{
			UpdateUi();
			return;
		}

		if (!PrepareCodeBehindForSave(out var saveError))
		{
			await ShowErrorAsync(saveError);
			return;
		}

		await _writeGate.WaitAsync();
		try
		{
			_document.RelativeFreePositioning = IsCustomPanelStrategy();
			await new WriteBackService(_xaml, _csharp).WriteAsync(_document, _savedSnapshot);
			_savedSnapshot = _document.Clone();
			UpdateUi();
		}
		catch (Exception ex)
		{
			await ShowErrorAsync($"自动保存失败（变更仍保留在内存）：{ex.Message}");
		}
		finally
		{
			_writeGate.Release();
		}
	}

	private void Select(ControlNode node) {
		_selected = node;
		_windowSelected = false;
		RefreshProperties();
		RenderCanvas();
		UpdateUi();
	}

	private void SelectWindow() {
		_selected = null;
		_windowSelected = true;
		RefreshProperties();
		RenderCanvas();
		UpdateUi();
	}

	private void UpdateDesignZoom() {
		DesignPreviewViewbox.Width = DesignPreviewBaseWidth * _designZoom;
		DesignPreviewViewbox.Height = DesignPreviewBaseHeight * _designZoom;
		UpdateZoomHint();
	}

	private void UpdateZoomHint() {
		if (HintText is null) return;
		var zoomText = $"Ctrl+滚轮缩放 {_designZoom * 100:0}%";
		var saveText = _settings.AutoSaveEnabled ? "自动保存：开" : "自动保存：关";
		var previewText = _settings.HighFidelityPreviewEnabled ? "真实控件 + Overlay：开" : "传统预览：开";
		HintText.Text = $"XAML Sub by MsintX（{saveText}，{previewText}，{zoomText}）";
	}

	private void RenderCanvas()
	{
		_placementPreview = null;
		EditorOverlayCanvas.Children.Clear();
		_nodeHosts.Clear();
		_hostSelectionAdorners.Clear();
		_nativeElements.Clear();
		_draggingHost = null;
		_nativePreviewRoot = null;
		if (_document is null)
		{
			NativePreviewHost.Content = null;
			return;
		}

		UpdateDesignZoom();
		_document.RelativeFreePositioning = IsCustomPanelStrategy();
		_nativePreviewRoot = _settings.HighFidelityPreviewEnabled
			? BuildNativePreviewRoot()
			: BuildLegacyPreviewRoot();
		_nativePreviewRoot.IsHitTestVisible = false;
		NativePreviewHost.Content = _nativePreviewRoot;

		var cellWidth = EditorOverlayCanvas.Width / Math.Max(1, _document.Columns);
		var cellHeight = EditorOverlayCanvas.Height / Math.Max(1, _document.Rows);
		var gridBrush = ThemeBrush("DesignerCanvasGridBrush");
		var accentBrush = ThemeBrush("DesignerAccentBrush");
		DrawPositioningGuides(cellWidth, cellHeight, gridBrush, accentBrush);

		foreach (var node in _document.Nodes)
			CreateOverlayHost(node);

		QueueOverlaySync();
	}

	private FrameworkElement BuildLegacyPreviewRoot()
	{
		var root = new Canvas
		{
			Width = DesignPreviewBaseWidth,
			Height = DesignPreviewBaseHeight - 42,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			IsHitTestVisible = false
		};

		foreach (var node in _document!.Nodes)
		{
			var bounds = GetLegacyNodeBounds(node);
			var visual = BuildHandDrawnPreview(node, Math.Max(28, bounds.Width), Math.Max(24, bounds.Height));
			visual.Width = Math.Max(28, bounds.Width);
			visual.Height = Math.Max(24, bounds.Height);
			Canvas.SetLeft(visual, bounds.X);
			Canvas.SetTop(visual, bounds.Y);
			root.Children.Add(visual);
		}

		return root;
	}

	private Rect GetLegacyNodeBounds(ControlNode node)
	{
		var width = ParseSize(node.Width, DefaultWidth(node.TypeName));
		var height = ParseSize(node.Height, DefaultHeight(node.TypeName));
		if (IsCanvasStrategy() || IsDockPanelStrategy())
			return new Rect(node.OffsetX, node.OffsetY, width, height);

		var cellWidth = DesignPreviewBaseWidth / Math.Max(1, _document?.Columns ?? 1);
		var cellHeight = (DesignPreviewBaseHeight - 42) / Math.Max(1, _document?.Rows ?? 1);
		if (IsCustomPanelStrategy())
		{
			return new Rect(node.OffsetX, node.OffsetY, width, height);
		}
		return new Rect(node.Column * cellWidth + 3, node.Row * cellHeight + 3,
			Math.Max(8, cellWidth - 6), Math.Max(8, cellHeight - 6));
	}

	private FrameworkElement BuildHandDrawnPreview(ControlNode node, double width, double height) {
		var type = node.TypeName;
		var label = string.IsNullOrWhiteSpace(node.Content) ? type : node.Content!;
		var surface = ThemeBrush("DesignerPreviewSurfaceBrush");
		var accentBrush = ThemeBrush("DesignerAccentBrush");
		var foreground = new SolidColorBrush(PreviewColor(225, 255, 255, 255));

		if (type == "Button" || type == "HyperlinkButton" || type == "ToggleButton" || type == "RepeatButton") {
			return new Border {
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				CornerRadius = new CornerRadius(5),
				Height = Math.Min(34, height),
				Background = surface,
				BorderBrush = ThemeBrush("DesignerControlBorderBrush"),
				BorderThickness = new Thickness(1),
				Child = CenterText(label)
			};
		}

		if (type == "TextBox" || type == "PasswordBox" || type == "AutoSuggestBox" || type == "NumberBox" || type == "RichEditBox") {
			var panel = new Grid();
			panel.Children.Add(new Border {
				Background = new SolidColorBrush(PreviewColor(45, 128, 128, 128)),
				BorderBrush = ThemeBrush("DesignerControlBorderBrush"),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(4)
			});
			panel.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(node.Content) ? type : node.Content, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 9, 0), Opacity = 0.78 });
			return panel;
		}

		if (type == "Image") {
			var panel = new Grid();
			panel.Children.Add(new TextBlock { Text = "▧", FontSize = Math.Min(28, height), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.72 });
			var source = node.Properties.TryGetValue("Source", out var sourceValue) ? sourceValue : null;
			panel.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(source) ? "Image" : source, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
			return panel;
		}

		if (type == "CheckBox" || type == "RadioButton") {
			var grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			var mark = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(type == "RadioButton" ? 9 : 3), BorderThickness = new Thickness(1.5), BorderBrush = ThemeBrush("DesignerControlBorderBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			grid.Children.Add(mark);
			var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 0, 0) };
			Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
		}

		if (type == "ToggleSwitch") {
			var grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			var pill = new Border { Width = 34, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(PreviewColor(90, 150, 150, 150)), BorderBrush = ThemeBrush("DesignerControlBorderBrush"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
			pill.Child = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = foreground, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
			grid.Children.Add(pill);
			var onContent = node.Properties.TryGetValue("OnContent", out var onContentValue) ? onContentValue : null;
			var text = new TextBlock { Text = string.IsNullOrWhiteSpace(onContent) ? "ToggleSwitch" : onContent, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
			Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
		}

		if (type == "Slider" || type == "ProgressBar") {
			var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
			stack.Children.Add(new TextBlock { Text = type, FontSize = 11, Opacity = 0.65 });
			var track = new Grid { Height = 8 };
			track.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(PreviewColor(55, 140, 140, 140)) });
			track.Children.Add(new Border { Width = type == "ProgressBar" ? width * 0.58 : 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2), Background = accentBrush });
			if (type == "Slider") track.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = accentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Math.Max(0, width * 0.52), 0, 0, 0) });
			stack.Children.Add(track); return stack;
		}

		if (type == "ProgressRing" || type == "RatingControl")
			return new TextBlock { Text = type == "ProgressRing" ? "◌" : "★★★★★", FontSize = Math.Min(24, height), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.78 };

		if (type == "ComboBox" || type == "DatePicker" || type == "CalendarDatePicker" || type == "TimePicker")
			return new Border { Height = Math.Min(34, height), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(4), Background = surface, BorderBrush = ThemeBrush("DesignerControlBorderBrush"), BorderThickness = new Thickness(1), Child = new TextBlock { Text = label + "   ▾", Margin = new Thickness(9, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };

		if (type == "ListView" || type == "ListBox" || type == "TreeView" || type == "ItemsRepeater" || type == "NavigationView" || type == "Pivot" || type == "TabView") {
			var stack = new StackPanel { Spacing = 3, Margin = new Thickness(3) };
			for (var i = 0; i < 3; i++) stack.Children.Add(new Border { Height = Math.Max(12, (height - 12) / 3), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(PreviewColor((byte)(i == 0 ? 50 : 28), 140, 140, 140)), Child = new TextBlock { Text = i == 0 ? label : $"{type} item", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0), TextTrimming = TextTrimming.CharacterEllipsis } });
			return stack;
		}

		return new Grid {
			Children = { new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis } }
		};
	}


	private FrameworkElement BuildNativePreviewRoot()
	{
		FrameworkElement root;
		if (IsGridRelativeStrategy() || IsCustomPanelStrategy() || IsAbsoluteGridStrategy())
		{
			var grid = new Grid();
			for (var r = 0; r < Math.Max(1, _document!.Rows); r++)
				grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			for (var c = 0; c < Math.Max(1, _document.Columns); c++)
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			root = grid;
		}
		else if (IsDockPanelStrategy())
		{
			root = new DesignerDockPanel();
		}
		else
		{
			root = new Canvas();
		}

		root.Width = DesignPreviewBaseWidth;
		root.Height = 678;
		root.HorizontalAlignment = HorizontalAlignment.Left;
		root.VerticalAlignment = VerticalAlignment.Top;

		if (root is Panel panel)
		{
			foreach (var node in _document!.Nodes)
			{
				var element = CreateNativeControl(node);
				ApplyNativeLayout(panel, element, node);
				panel.Children.Add(element);
			}
		}

		return root;
	}

	private FrameworkElement CreateNativeControl(ControlNode node)
	{
		var label = string.IsNullOrWhiteSpace(node.Content) ? node.TypeName : node.Content!;
		FrameworkElement element = node.TypeName switch
		{
			"Button" => new Button { Content = label },
			"HyperlinkButton" => new HyperlinkButton { Content = label },
			"ToggleButton" => new ToggleButton { Content = label },
			"RepeatButton" => new RepeatButton { Content = label },
			"CheckBox" => new CheckBox { Content = label },
			"RadioButton" => new RadioButton { Content = label },
			"ToggleSwitch" => new ToggleSwitch { OnContent = label, OffContent = label },
			"TextBlock" => new TextBlock { Text = label },
			"TextBox" => new TextBox { Text = node.Content ?? string.Empty },
			"PasswordBox" => new PasswordBox { Password = node.Content ?? string.Empty },
			"AutoSuggestBox" => new AutoSuggestBox { Text = node.Content ?? string.Empty },
			"NumberBox" => new NumberBox(),
			"RichEditBox" => new RichEditBox(),
			"Image" => BuildNativeImage(node),
			"Icon" => new SymbolIcon(Symbol.Edit),
			"FontIcon" => new FontIcon { Glyph = "\uE11B" },
			"Slider" => new Slider(),
			"ProgressBar" => new ProgressBar(),
			"ProgressRing" => new ProgressRing { IsActive = false },
			"ComboBox" => BuildNativeComboBox(label),
			"ListView" => BuildNativeListView(),
			"ListBox" => BuildNativeListBox(),
			"TreeView" => BuildNativeTreeView(),
			"ItemsRepeater" => BuildNativeItemsRepeater(),
			"DatePicker" => new DatePicker(),
			"CalendarDatePicker" => new CalendarDatePicker(),
			"TimePicker" => new TimePicker(),
			"CalendarView" => new CalendarView(),
			"Expander" => new Expander { Header = label },
			"InfoBar" => new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Informational, Title = label, Message = "InfoBar" },
			"RatingControl" => new RatingControl { Value = 3 },
			"NavigationView" => BuildNativeNavigationView(),
			"Pivot" => BuildNativePivot(),
			"TabView" => BuildNativeTabView(),
			"ScrollViewer" => new ScrollViewer { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } },
			"CommandBar" => BuildNativeCommandBar(),
			_ => new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }
		};

		// Do not synthesize Width/Height. Let the real WinUI control Measure/Arrange unless
		// the user explicitly provided Width/Height in the XAML model.
		element.MinWidth = 0;
		element.MinHeight = 0;
		element.IsHitTestVisible = false;
		ApplyNativeProperties(element, node);
		_nativeElements[node] = element;
		return element;
	}

	private void ApplyNativeLayout(Panel parent, FrameworkElement element, ControlNode node)
	{
		if (parent is Grid grid)
		{
			if (IsCustomPanelStrategy())
			{
				ApplyCanonicalFreeRelativeLayout(grid, element, node);
			}
			else
			{
				Grid.SetRow(element, Math.Clamp(node.Row, 0, Math.Max(0, (_document?.Rows ?? 1) - 1)));
				Grid.SetColumn(element, Math.Clamp(node.Column, 0, Math.Max(0, (_document?.Columns ?? 1) - 1)));
				Grid.SetRowSpan(element, Math.Max(1, node.RowSpan));
				Grid.SetColumnSpan(element, Math.Max(1, node.ColumnSpan));
			}
		}
		else if (parent is Canvas)
		{
			var left = 0d;
			var top = 0d;
			if (node.Properties.TryGetValue("Canvas.Left", out var leftText) && TryParseDesignerDouble(leftText, out var explicitLeft))
				left = explicitLeft;
			if (node.Properties.TryGetValue("Canvas.Top", out var topText) && TryParseDesignerDouble(topText, out var explicitTop))
				top = explicitTop;
			Canvas.SetLeft(element, left);
			Canvas.SetTop(element, top);
			element.HorizontalAlignment = HorizontalAlignment.Left;
			element.VerticalAlignment = VerticalAlignment.Top;
		}
		else if (parent is DesignerDockPanel)
		{
			DesignerDockPanel.SetDock(element, node.Dock ?? "Left");
		}

		if (!string.IsNullOrWhiteSpace(node.Width) && TryParseDesignerDouble(node.Width, out var width) && width > 0)
			element.Width = width;
		else
			element.Width = double.NaN;

		if (!string.IsNullOrWhiteSpace(node.Height) && TryParseDesignerDouble(node.Height, out var height) && height > 0)
			element.Height = height;
		else
			element.Height = double.NaN;

		if (!IsCustomPanelStrategy() && !string.IsNullOrWhiteSpace(node.HorizontalAlignment) && Enum.TryParse<HorizontalAlignment>(node.HorizontalAlignment, true, out var horizontalAlignment))
			element.HorizontalAlignment = horizontalAlignment;
		if (!IsCustomPanelStrategy() && !string.IsNullOrWhiteSpace(node.VerticalAlignment) && Enum.TryParse<VerticalAlignment>(node.VerticalAlignment, true, out var verticalAlignment))
			element.VerticalAlignment = verticalAlignment;
	}

	private static void ApplyCanonicalFreeRelativeLayout(Grid root, FrameworkElement element, ControlNode node)
	{
		// Canonical free-relative positioning:
		// 1. Take the complete root Grid as the child's layout slot.
		// 2. Anchor at the slot's top-left.
		// 3. Express the entire position with Margin.Left/Top only.
		//
		// This deliberately avoids Row/Column-dependent offsets and avoids Canvas
		// coordinates, while still allowing arbitrary parent-relative placement.
		Grid.SetRow(element, 0);
		Grid.SetColumn(element, 0);
		Grid.SetRowSpan(element, Math.Max(1, root.RowDefinitions.Count));
		Grid.SetColumnSpan(element, Math.Max(1, root.ColumnDefinitions.Count));
		element.HorizontalAlignment = HorizontalAlignment.Left;
		element.VerticalAlignment = VerticalAlignment.Top;
		try
		{
			element.ClearValue(Canvas.LeftProperty);
			element.ClearValue(Canvas.TopProperty);
		}
		catch
		{
			// Attached-property cleanup is defensive; the canonical Margin layout below
			// remains valid even when the element had no Canvas values to begin with.
		}
		element.Margin = new Thickness(
			Math.Max(0, node.OffsetX),
			Math.Max(0, node.OffsetY),
			0,
			0);
	}

	private void CreateOverlayHost(ControlNode node)
	{
		var host = new Border
		{
			Tag = node,
			Background = new SolidColorBrush(PreviewColor(1, 0, 0, 0)),
			BorderThickness = new Thickness(0),
			CornerRadius = new CornerRadius(5),
			IsHitTestVisible = true
		};
		ToolTipService.SetToolTip(host, $"{node.TypeName}" + (string.IsNullOrWhiteSpace(node.XName) ? "" : $" - {node.XName}"));
		host.PointerPressed += Node_PointerPressed;
		_nodeHosts[node] = host;
		EditorOverlayCanvas.Children.Add(host);
		Canvas.SetZIndex(host, 1000);

		if (ReferenceEquals(node, _selected))
		{
			var selection = new Rectangle
			{
				Tag = node,
				Stroke = ThemeBrush("DesignerControlSelectedBorderBrush"),
				StrokeThickness = 2,
				RadiusX = 5,
				RadiusY = 5,
				Fill = new SolidColorBrush(PreviewColor(0, 0, 0, 0)),
				IsHitTestVisible = false
			};
			EditorOverlayCanvas.Children.Add(selection);
			Canvas.SetZIndex(selection, 2000);
			_hostSelectionAdorners[node] = selection;
		}
	}

	private void DesignSurfaceRoot_SizeChanged(object sender, SizeChangedEventArgs e) => QueueOverlaySync();

	private void QueueOverlaySync()
	{
		if (_overlaySyncPending) return;
		_overlaySyncPending = true;
		DispatcherQueue.TryEnqueue(() =>
		{
			_overlaySyncPending = false;
			SyncAllOverlays();
		});
	}

	private void SyncAllOverlays()
	{
		if (_document is null) return;
		foreach (var node in _document.Nodes)
		{
			if (_nodeHosts.TryGetValue(node, out var overlay))
				SyncOverlayBounds(node, overlay);
			if (_hostSelectionAdorners.TryGetValue(node, out var selection))
				SyncOverlayBounds(node, selection);
		}
	}

	private bool TryGetNativeBounds(ControlNode node, out Rect bounds)
	{
		bounds = Rect.Empty;
		if (!_nativeElements.TryGetValue(node, out var element) || element.Visibility == Visibility.Collapsed) return false;
		try
		{
			var width = element.ActualWidth;
			var height = element.ActualHeight;
			if (width <= 0 || height <= 0) return false;
			var transform = element.TransformToVisual(EditorOverlayCanvas);
			bounds = transform.TransformBounds(new Rect(0, 0, width, height));
			return !bounds.IsEmpty;
		}
		catch
		{
			return false;
		}
	}

	private void SyncOverlayBounds(ControlNode node, FrameworkElement overlay)
	{
		if (!TryGetNativeBounds(node, out var bounds))
		{
			overlay.Visibility = Visibility.Collapsed;
			return;
		}

		overlay.Visibility = Visibility.Visible;
		overlay.Width = Math.Max(1, bounds.Width);
		overlay.Height = Math.Max(1, bounds.Height);
		Canvas.SetLeft(overlay, bounds.X);
		Canvas.SetTop(overlay, bounds.Y);
	}

	private static Image BuildNativeImage(ControlNode node)
	{
		var image = new Image { Stretch = Stretch.Uniform };
		if (node.Properties.TryGetValue("Source", out var source) && !string.IsNullOrWhiteSpace(source))
		{
			try
			{
				image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(source.Trim(), UriKind.RelativeOrAbsolute));
			}
			catch
			{
				// Keep the real Image control even when the source is not resolvable in the designer.
			}
		}
		return image;
	}

	private static ComboBox BuildNativeComboBox(string label)
	{
		var combo = new ComboBox { PlaceholderText = label };
		combo.Items.Add("Item 1");
		combo.Items.Add("Item 2");
		combo.Items.Add("Item 3");
		return combo;
	}

	private static ListView BuildNativeListView()
	{
		return new ListView
		{
			ItemsSource = new[] { "Item 1", "Item 2", "Item 3" }
		};
	}

	private static ListBox BuildNativeListBox()
	{
		return new ListBox
		{
			ItemsSource = new[] { "Item 1", "Item 2", "Item 3" }
		};
	}

	private static TreeView BuildNativeTreeView()
	{
		var tree = new TreeView();
		var root = new TreeViewNode { Content = "Item 1", IsExpanded = true };
		root.Children.Add(new TreeViewNode { Content = "Child 1" });
		root.Children.Add(new TreeViewNode { Content = "Child 2" });
		tree.RootNodes.Add(root);
		tree.RootNodes.Add(new TreeViewNode { Content = "Item 2" });
		return tree;
	}

	private static ItemsRepeater BuildNativeItemsRepeater()
	{
		return new ItemsRepeater
		{
			ItemsSource = new[] { "Item 1", "Item 2", "Item 3" },
			Layout = new StackLayout { Spacing = 4 }
		};
	}

	private static NavigationView BuildNativeNavigationView()
	{
		var navigation = new NavigationView
		{
			PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
			IsPaneToggleButtonVisible = false,
			IsSettingsVisible = false,
			Content = new TextBlock { Text = "NavigationView 内容", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
		};
		navigation.MenuItems.Add(new NavigationViewItem { Content = "Home" });
		navigation.MenuItems.Add(new NavigationViewItem { Content = "Settings" });
		return navigation;
	}

	private static Pivot BuildNativePivot()
	{
		var pivot = new Pivot();
		pivot.Items.Add(new PivotItem { Header = "Item 1", Content = new TextBlock { Text = "Pivot 内容 1", Margin = new Thickness(8) } });
		pivot.Items.Add(new PivotItem { Header = "Item 2", Content = new TextBlock { Text = "Pivot 内容 2", Margin = new Thickness(8) } });
		return pivot;
	}

	private static TabView BuildNativeTabView()
	{
		var tabs = new TabView();
		tabs.TabItems.Add(new TabViewItem { Header = "Tab 1", Content = new TextBlock { Text = "Tab 内容 1", Margin = new Thickness(8) } });
		tabs.TabItems.Add(new TabViewItem { Header = "Tab 2", Content = new TextBlock { Text = "Tab 内容 2", Margin = new Thickness(8) } });
		return tabs;
	}

	private static CommandBar BuildNativeCommandBar()
	{
		var bar = new CommandBar
		{
			OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		bar.PrimaryCommands.Add(new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add" });
		bar.PrimaryCommands.Add(new AppBarButton { Icon = new SymbolIcon(Symbol.Edit), Label = "Edit" });
		return bar;
	}

	private static void ApplyNativeProperties(FrameworkElement element, ControlNode node)
	{
		if (node.Properties.TryGetValue("ToolTip", out var toolTip) && !string.IsNullOrWhiteSpace(toolTip))
			ToolTipService.SetToolTip(element, toolTip);

		if (node.Properties.TryGetValue("Margin", out var margin) && TryParseThickness(margin, out var parsedMargin))
			element.Margin = parsedMargin;

		if (node.Properties.TryGetValue("MinWidth", out var minWidth) && TryParseDesignerDouble(minWidth, out var parsedMinWidth))
			element.MinWidth = parsedMinWidth;
		if (node.Properties.TryGetValue("MaxWidth", out var maxWidth) && TryParseDesignerDouble(maxWidth, out var parsedMaxWidth))
			element.MaxWidth = parsedMaxWidth;
		if (node.Properties.TryGetValue("MinHeight", out var minHeight) && TryParseDesignerDouble(minHeight, out var parsedMinHeight))
			element.MinHeight = parsedMinHeight;
		if (node.Properties.TryGetValue("MaxHeight", out var maxHeight) && TryParseDesignerDouble(maxHeight, out var parsedMaxHeight))
			element.MaxHeight = parsedMaxHeight;
		if (node.Properties.TryGetValue("Opacity", out var opacity) && TryParseDesignerDouble(opacity, out var parsedOpacity))
			element.Opacity = Math.Clamp(parsedOpacity, 0, 1);
		if (node.Properties.TryGetValue("Visibility", out var visibility) && Enum.TryParse<Visibility>(visibility, true, out var parsedVisibility))
			element.Visibility = parsedVisibility;
		if (node.Properties.TryGetValue("FlowDirection", out var flowDirection) && Enum.TryParse<FlowDirection>(flowDirection, true, out var parsedFlowDirection))
			element.FlowDirection = parsedFlowDirection;

		if (element is TextBlock textBlock)
		{
			ApplyTextElementProperties(textBlock, node);
		}
		else if (element is Control control)
		{
			ApplyControlProperties(control, node);
		}

		if (node.Properties.TryGetValue("Text", out var textValue))
		{
			switch (element)
			{
				case TextBox textInputBox:
					textInputBox.Text = textValue;
					break;
				case PasswordBox passwordInputBox:
					passwordInputBox.Password = textValue;
					break;
				case AutoSuggestBox autoSuggestInputBox:
					autoSuggestInputBox.Text = textValue;
					break;
			}
		}
	}

	private static void ApplyTextElementProperties(TextBlock textBlock, ControlNode node)
	{
		if (node.Properties.TryGetValue("TextWrapping", out var wrapping) && Enum.TryParse<TextWrapping>(wrapping, true, out var parsedWrapping))
			textBlock.TextWrapping = parsedWrapping;
		if (node.Properties.TryGetValue("TextAlignment", out var alignment) && Enum.TryParse<TextAlignment>(alignment, true, out var parsedAlignment))
			textBlock.TextAlignment = parsedAlignment;
		ApplyFontProperties(textBlock, node);
		if (node.Properties.TryGetValue("Foreground", out var foreground) && TryParseBrush(foreground, out var foregroundBrush))
			textBlock.Foreground = foregroundBrush;
	}

	private static void ApplyControlProperties(Control control, ControlNode node)
	{
		ApplyFontProperties(control, node);
		if (node.Properties.TryGetValue("IsEnabled", out var isEnabled) && bool.TryParse(isEnabled, out var parsedIsEnabled))
			control.IsEnabled = parsedIsEnabled;
		if (node.Properties.TryGetValue("IsTabStop", out var isTabStop) && bool.TryParse(isTabStop, out var parsedIsTabStop))
			control.IsTabStop = parsedIsTabStop;
		if (node.Properties.TryGetValue("UseSystemFocusVisuals", out var focusVisuals) && bool.TryParse(focusVisuals, out var parsedFocusVisuals))
			control.UseSystemFocusVisuals = parsedFocusVisuals;
		if (node.Properties.TryGetValue("Padding", out var padding) && TryParseThickness(padding, out var parsedPadding))
			control.Padding = parsedPadding;
		if (node.Properties.TryGetValue("HorizontalContentAlignment", out var horizontalContentAlignment) && Enum.TryParse<HorizontalAlignment>(horizontalContentAlignment, true, out var parsedHorizontalContentAlignment))
			control.HorizontalContentAlignment = parsedHorizontalContentAlignment;
		if (node.Properties.TryGetValue("VerticalContentAlignment", out var verticalContentAlignment) && Enum.TryParse<VerticalAlignment>(verticalContentAlignment, true, out var parsedVerticalContentAlignment))
			control.VerticalContentAlignment = parsedVerticalContentAlignment;
		if (node.Properties.TryGetValue("FontFamily", out var fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
			control.FontFamily = new FontFamily(fontFamily);
		if (node.Properties.TryGetValue("Foreground", out var foreground) && TryParseBrush(foreground, out var foregroundBrush))
			control.Foreground = foregroundBrush;
		if (node.Properties.TryGetValue("Background", out var background) && TryParseBrush(background, out var backgroundBrush))
			control.Background = backgroundBrush;
		if (node.Properties.TryGetValue("BorderBrush", out var borderBrush) && TryParseBrush(borderBrush, out var parsedBorderBrush))
			control.BorderBrush = parsedBorderBrush;
		if (node.Properties.TryGetValue("BorderThickness", out var borderThickness) && TryParseThickness(borderThickness, out var parsedBorderThickness))
			control.BorderThickness = parsedBorderThickness;
		if (node.Properties.TryGetValue("CornerRadius", out var cornerRadius) && TryParseCornerRadius(cornerRadius, out var parsedCornerRadius))
			control.CornerRadius = parsedCornerRadius;

		if (node.Properties.TryGetValue("Header", out var header))
		{
			switch (control)
			{
				case TextBox headerTextBox:
					headerTextBox.Header = header;
					break;
				case PasswordBox headerPasswordBox:
					headerPasswordBox.Header = header;
					break;
				case NumberBox headerNumberBox:
					headerNumberBox.Header = header;
					break;
				case ComboBox headerComboBox:
					headerComboBox.Header = header;
					break;
				case DatePicker headerDatePicker:
					headerDatePicker.Header = header;
					break;
				case TimePicker headerTimePicker:
					headerTimePicker.Header = header;
					break;
			}
		}

		if (control is ButtonBase buttonBase)
		{
			if (node.Properties.TryGetValue("ClickMode", out var clickMode) && Enum.TryParse<ClickMode>(clickMode, true, out var parsedClickMode))
				buttonBase.ClickMode = parsedClickMode;
		}
		if (control is CheckBox checkBox && TryParseNullableBool(node, "IsChecked", out var checkValue))
			checkBox.IsChecked = checkValue;
		if (control is RadioButton radioButton && TryParseNullableBool(node, "IsChecked", out var radioValue))
			radioButton.IsChecked = radioValue;
		if (control is ToggleButton toggleButton && TryParseNullableBool(node, "IsChecked", out var toggleButtonValue))
			toggleButton.IsChecked = toggleButtonValue;
		if (control is ToggleSwitch toggleSwitch)
		{
			if (TryParseBool(node, "IsOn", out var isOn)) toggleSwitch.IsOn = isOn;
			if (node.Properties.TryGetValue("OnContent", out var onContent)) toggleSwitch.OnContent = onContent;
			if (node.Properties.TryGetValue("OffContent", out var offContent)) toggleSwitch.OffContent = offContent;
		}
		if (control is TextBox textBoxControl)
		{
			if (node.Properties.TryGetValue("PlaceholderText", out var placeholder)) textBoxControl.PlaceholderText = placeholder;
			if (TryParseBool(node, "AcceptsReturn", out var acceptsReturn)) textBoxControl.AcceptsReturn = acceptsReturn;
			if (TryParseBool(node, "IsReadOnly", out var isReadOnly)) textBoxControl.IsReadOnly = isReadOnly;
			if (TryParseInt(node, "MaxLength", out var maxLength)) textBoxControl.MaxLength = Math.Max(0, maxLength);
		}
		if (control is PasswordBox passwordBoxControl)
		{
			if (node.Properties.TryGetValue("PlaceholderText", out var placeholder)) passwordBoxControl.PlaceholderText = placeholder;
			if (TryParseInt(node, "MaxLength", out var maxLength)) passwordBoxControl.MaxLength = Math.Max(0, maxLength);
		}
		if (control is AutoSuggestBox autoSuggestBox && node.Properties.TryGetValue("PlaceholderText", out var autoPlaceholder))
			autoSuggestBox.PlaceholderText = autoPlaceholder;
		if (control is ComboBox comboBox)
		{
			if (node.Properties.TryGetValue("PlaceholderText", out var placeholder)) comboBox.PlaceholderText = placeholder;
			if (TryParseBool(node, "IsEditable", out var isEditable)) comboBox.IsEditable = isEditable;
			if (TryParseInt(node, "SelectedIndex", out var selectedIndex) && comboBox.Items.Count > 0)
				comboBox.SelectedIndex = Math.Clamp(selectedIndex, -1, comboBox.Items.Count - 1);
		}
		if (control is NumberBox numberBox)
		{
			if (TryParseDouble(node, "Minimum", out var minimum) && !double.IsNaN(minimum)) numberBox.Minimum = minimum;
			if (TryParseDouble(node, "Maximum", out var maximum) && !double.IsNaN(maximum) && maximum >= numberBox.Minimum) numberBox.Maximum = maximum;
			if (TryParseDouble(node, "Value", out var value) && !double.IsNaN(value)) numberBox.Value = Math.Clamp(value, numberBox.Minimum, numberBox.Maximum);
			if (node.Properties.TryGetValue("Header", out var numberHeader)) numberBox.Header = numberHeader;
		}
		if (control is Slider slider)
		{
			if (TryParseDouble(node, "Minimum", out var minimum) && !double.IsNaN(minimum)) slider.Minimum = minimum;
			if (TryParseDouble(node, "Maximum", out var maximum) && !double.IsNaN(maximum) && maximum >= slider.Minimum) slider.Maximum = maximum;
			if (TryParseDouble(node, "Value", out var value) && !double.IsNaN(value)) slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
			if (node.Properties.TryGetValue("Orientation", out var orientation) && Enum.TryParse<Orientation>(orientation, true, out var parsedOrientation)) slider.Orientation = parsedOrientation;
		}
		if (control is ProgressBar progressBar)
		{
			if (TryParseDouble(node, "Minimum", out var minimum) && !double.IsNaN(minimum)) progressBar.Minimum = minimum;
			if (TryParseDouble(node, "Maximum", out var maximum) && !double.IsNaN(maximum) && maximum >= progressBar.Minimum) progressBar.Maximum = maximum;
			if (TryParseDouble(node, "Value", out var value) && !double.IsNaN(value)) progressBar.Value = Math.Clamp(value, progressBar.Minimum, progressBar.Maximum);
			if (TryParseBool(node, "IsIndeterminate", out var isIndeterminate)) progressBar.IsIndeterminate = isIndeterminate;
		}
		if (control is ProgressRing progressRing && TryParseBool(node, "IsIndeterminate", out var ringIndeterminate))
			progressRing.IsIndeterminate = ringIndeterminate;
		if (control is RatingControl ratingControl && TryParseDouble(node, "Value", out var ratingValue))
			ratingControl.Value = ratingValue;
		if (control is ListView listView && TryParseInt(node, "SelectedIndex", out var listIndex)) listView.SelectedIndex = listIndex;
		if (control is ListBox listBox && TryParseInt(node, "SelectedIndex", out var listBoxIndex)) listBox.SelectedIndex = listBoxIndex;
		if (control is TabView tabView && TryParseInt(node, "SelectedIndex", out var tabIndex)) tabView.SelectedIndex = tabIndex;
		if (control is CalendarDatePicker calendarDatePicker && node.Properties.TryGetValue("DateFormat", out var calendarDateFormat))
			calendarDatePicker.DateFormat = calendarDateFormat;
		if (control is TimePicker timePicker && node.Properties.TryGetValue("SelectedTime", out var selectedTime) && TimeSpan.TryParse(selectedTime, CultureInfo.InvariantCulture, out var parsedTime))
			timePicker.Time = parsedTime;
		if (control is FrameworkElement frameworkElement && node.Properties.TryGetValue("Language", out var language))
			frameworkElement.Language = language;
		if (node.Properties.TryGetValue("Tag", out var tag))
			control.Tag = tag;
		if (node.Properties.TryGetValue("FontSize", out var fontSize) && TryParseDesignerDouble(fontSize, out var parsedFontSize))
			control.FontSize = parsedFontSize;
		if (node.Properties.TryGetValue("FontStyle", out var fontStyle) && Enum.TryParse<FontStyle>(fontStyle, true, out var parsedFontStyle))
			control.FontStyle = parsedFontStyle;
		if (node.Properties.TryGetValue("TextAlignment", out var textAlignment) && Enum.TryParse<TextAlignment>(textAlignment, true, out var parsedTextAlignment))
		{
			if (control is TextBox alignedTextBox) alignedTextBox.TextAlignment = parsedTextAlignment;
			else if (control is PasswordBox alignedPasswordBox) alignedPasswordBox.HorizontalContentAlignment = textAlignment.Equals("Center", StringComparison.OrdinalIgnoreCase) ? HorizontalAlignment.Center : alignedPasswordBox.HorizontalContentAlignment;
		}
	}

	private static void ApplyFontProperties(Control control, ControlNode node)
	{
		if (node.Properties.TryGetValue("FontSize", out var fontSize) && TryParseDesignerDouble(fontSize, out var parsedFontSize))
			control.FontSize = parsedFontSize;
		if (node.Properties.TryGetValue("FontFamily", out var fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
			control.FontFamily = new FontFamily(fontFamily);
		if (node.Properties.TryGetValue("FontWeight", out var fontWeight) && TryParseFontWeight(fontWeight, out var parsedFontWeight))
			control.FontWeight = parsedFontWeight;
		if (node.Properties.TryGetValue("FontStyle", out var fontStyle) && Enum.TryParse<FontStyle>(fontStyle, true, out var parsedFontStyle))
			control.FontStyle = parsedFontStyle;
		if (node.Properties.TryGetValue("CharacterSpacing", out var characterSpacing) && int.TryParse(characterSpacing, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCharacterSpacing))
			control.CharacterSpacing = parsedCharacterSpacing;
	}

	private static void ApplyFontProperties(TextBlock textBlock, ControlNode node)
	{
		if (node.Properties.TryGetValue("FontSize", out var fontSize) && TryParseDesignerDouble(fontSize, out var parsedFontSize))
			textBlock.FontSize = parsedFontSize;
		if (node.Properties.TryGetValue("FontFamily", out var fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
			textBlock.FontFamily = new FontFamily(fontFamily);
		if (node.Properties.TryGetValue("FontWeight", out var fontWeight) && TryParseFontWeight(fontWeight, out var parsedFontWeight))
			textBlock.FontWeight = parsedFontWeight;
		if (node.Properties.TryGetValue("FontStyle", out var fontStyle) && Enum.TryParse<FontStyle>(fontStyle, true, out var parsedFontStyle))
			textBlock.FontStyle = parsedFontStyle;
		if (node.Properties.TryGetValue("CharacterSpacing", out var characterSpacing) && int.TryParse(characterSpacing, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCharacterSpacing))
			textBlock.CharacterSpacing = parsedCharacterSpacing;
	}

	private static bool TryParseDesignerDouble(string text, out double value) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

	private static bool TryParseDouble(ControlNode node, string key, out double value)
	{
		value = default;
		return node.Properties.TryGetValue(key, out var text) && TryParseDesignerDouble(text, out value);
	}

	private static bool TryParseInt(ControlNode node, string key, out int value)
	{
		value = default;
		return node.Properties.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	private static bool TryParseBool(ControlNode node, string key, out bool value)
	{
		value = default;
		return node.Properties.TryGetValue(key, out var text) && bool.TryParse(text, out value);
	}

	private static bool TryParseNullableBool(ControlNode node, string key, out bool? value)
	{
		value = null;
		if (!node.Properties.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text)) return false;
		if (bool.TryParse(text, out var parsed)) { value = parsed; return true; }
		return false;
	}

	private static bool TryParseThickness(string? text, out Thickness value)
	{
		value = default;
		if (!TryParseFourPartNumbers(text, out var parts)) return false;
		value = new Thickness(parts[0], parts[1], parts[2], parts[3]);
		return true;
	}

	private static bool TryParseCornerRadius(string? text, out CornerRadius value)
	{
		value = default;
		if (!TryParseFourPartNumbers(text, out var parts)) return false;
		value = new CornerRadius(parts[0], parts[1], parts[2], parts[3]);
		return true;
	}

	private static bool TryParseFourPartNumbers(string? text, out double[] parts)
	{
		parts = Array.Empty<double>();
		if (string.IsNullOrWhiteSpace(text)) return false;
		var pieces = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (pieces.Length == 1 && TryParseDesignerDouble(pieces[0], out var all)) { parts = new[] { all, all, all, all }; return true; }
		if (pieces.Length == 2 && TryParseDesignerDouble(pieces[0], out var horizontal) && TryParseDesignerDouble(pieces[1], out var vertical))
		{
			parts = new[] { horizontal, vertical, horizontal, vertical };
			return true;
		}
		if (pieces.Length == 4 && pieces.All(piece => TryParseDesignerDouble(piece, out _)))
		{
			parts = pieces.Select(piece => double.Parse(piece, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
			return true;
		}
		return false;
	}

	private static bool TryParseBrush(string text, out Brush? brush)
	{
		brush = null;
		try
		{
			var escaped = System.Security.SecurityElement.Escape(text);
			var xaml = $"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{escaped}\"/>";
			brush = ((Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml)).Background;
			return brush is not null;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryParseFontWeight(string text, out FontWeight value)
	{
		value = FontWeights.Normal;
		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
		{
			if (numeric < 1 || numeric > ushort.MaxValue) return false;
			value = new FontWeight { Weight = (ushort)numeric };
			return true;
		}
		value = text.Trim().ToLowerInvariant() switch
		{
			"thin" => FontWeights.Thin,
			"extra light" or "extralight" => FontWeights.ExtraLight,
			"light" => FontWeights.Light,
			"semi light" or "semilight" => FontWeights.SemiLight,
			"normal" => FontWeights.Normal,
			"medium" => FontWeights.Medium,
			"semi bold" or "semibold" => FontWeights.SemiBold,
			"bold" => FontWeights.Bold,
			"extra bold" or "extrabold" => FontWeights.ExtraBold,
			"black" => FontWeights.Black,
			"extra black" or "extrablack" => FontWeights.ExtraBlack,
			_ => default
		};
		return value.Weight != 0;
	}


	private static bool IsDesignableControl(string typeName) => XamlDocumentService.DesignableControlTypes.Contains(typeName);

	private static Windows.UI.Color PreviewColor(byte a, byte r, byte g, byte b) => Windows.UI.Color.FromArgb(a, r, g, b);

	private static Brush ThemeBrush(string key) => Application.Current.Resources[key] as Brush ?? new SolidColorBrush(PreviewColor(255, 128, 128, 128));

	private void AddLine(double left, double top, double width, double height, Brush brush, double opacity = 0.35) {
		var line = new Rectangle { Width = width, Height = height, Fill = brush, Opacity = opacity, IsHitTestVisible = false };
		Canvas.SetLeft(line, left); Canvas.SetTop(line, top); EditorOverlayCanvas.Children.Add(line);
	}

	private void Node_PointerPressed(object sender, PointerRoutedEventArgs e) {
		if (sender is Border border && border.Tag is ControlNode node) {
			Select(node);
			_dragOriginalRow = node.Row;
			_dragOriginalColumn = node.Column;
			_dragOriginalRowSpan = node.RowSpan;
			_dragOriginalColumnSpan = node.ColumnSpan;
			_dragStartPoint = e.GetCurrentPoint(EditorOverlayCanvas).Position;
			_lastDragPoint = _dragStartPoint;
			_dragOriginalOffsetX = node.OffsetX;
			_dragOriginalOffsetY = node.OffsetY;
			if (IsCustomPanelStrategy() && TryGetNativeBounds(node, out var customBounds))
			{
				// The editor value is the native element's root-relative visual origin.
				// Do not read Margin in isolation: the actual arranged bounds are the
				// authoritative value after Grid has applied all layout rules.
				_dragOriginalOffsetX = customBounds.X;
				_dragOriginalOffsetY = customBounds.Y;
				node.OffsetX = customBounds.X;
				node.OffsetY = customBounds.Y;
			}
			if (IsCanvasStrategy() && _nativeElements.TryGetValue(node, out var nativeAtPointer))
			{
				var nativeLeft = Canvas.GetLeft(nativeAtPointer);
				var nativeTop = Canvas.GetTop(nativeAtPointer);
				_dragOriginalOffsetX = double.IsFinite(nativeLeft) ? nativeLeft : 0;
				_dragOriginalOffsetY = double.IsFinite(nativeTop) ? nativeTop : 0;
			}
			CommitHistory();
			_dragOriginalNativeBounds = TryGetNativeBounds(node, out var nodeBounds) ? nodeBounds : GetNodeVisualBounds(node);
			_dragCurrentVisualLeft = _dragOriginalNativeBounds.X;
			_dragCurrentVisualTop = _dragOriginalNativeBounds.Y;
			_dragVisualWidth = Math.Max(1, _dragOriginalNativeBounds.Width);
			_dragVisualHeight = Math.Max(1, _dragOriginalNativeBounds.Height);
			_nodeHosts.TryGetValue(node, out _draggingHost);
			_dragging = true;
			EditorOverlayCanvas.CapturePointer(e.Pointer);
			e.Handled = true;
		}
	}

	private ControlNode? FindNodeAt(Point point) {
		if (_document is null) return null;

		// Hit-test the editor overlay geometry, which is already synchronized to the
		// actual WinUI element bounds when native rendering is enabled.
		foreach (var node in _document.Nodes.AsEnumerable().Reverse())
		{
			if (_nodeHosts.TryGetValue(node, out var host) &&
				double.IsFinite(Canvas.GetLeft(host)) && double.IsFinite(Canvas.GetTop(host)) &&
				host.Width > 0 && host.Height > 0 &&
				new Rect(Canvas.GetLeft(host), Canvas.GetTop(host), host.Width, host.Height).Contains(point))
				return node;

			if (GetNodeVisualBounds(node).Contains(point))
				return node;
		}

		return null;
	}

	private (int row, int col) PointToCell(Point point) {
		if (_document is null) return (0, 0);
		var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
		var cellHeight = surfaceHeight / Math.Max(1, _document.Rows);
		var cellWidth = surfaceWidth / Math.Max(1, _document.Columns);
		return (
			Math.Clamp((int)(point.Y / cellHeight), 0, _document.Rows - 1),
			Math.Clamp((int)(point.X / cellWidth), 0, _document.Columns - 1));
	}


	private bool IsGridRelativeStrategy() => string.Equals(_settings.PositioningMode, AppSettingsService.Relative, StringComparison.Ordinal) &&
		string.Equals(_settings.RelativeStrategy, AppSettingsService.Grid, StringComparison.Ordinal);
	private bool IsFreeRelativeStrategy() => string.Equals(_settings.PositioningMode, AppSettingsService.Relative, StringComparison.Ordinal) &&
		string.Equals(_settings.RelativeStrategy, AppSettingsService.CustomPanel, StringComparison.Ordinal);
	private bool IsCustomPanelStrategy() => IsFreeRelativeStrategy();
	private bool IsDockPanelStrategy() => string.Equals(_settings.PositioningMode, AppSettingsService.Relative, StringComparison.Ordinal) &&
		string.Equals(_settings.RelativeStrategy, AppSettingsService.DockPanel, StringComparison.Ordinal);
	private bool IsCanvasStrategy() => string.Equals(_settings.PositioningMode, AppSettingsService.Absolute, StringComparison.Ordinal) &&
		string.Equals(_settings.AbsoluteStrategy, AppSettingsService.Canvas, StringComparison.Ordinal);
	private bool IsAbsoluteGridStrategy() => string.Equals(_settings.PositioningMode, AppSettingsService.Absolute, StringComparison.Ordinal) && !IsCanvasStrategy();
	private bool UsesFreePositioning() => !IsGridRelativeStrategy();

	private void ApplyPositioningStrategyChange(string? newPositioningMode, string? newRelativeStrategy)
	{
		if (_document is null)
		{
			if (newPositioningMode is not null) _settings.PositioningMode = newPositioningMode;
			if (newRelativeStrategy is not null) _settings.RelativeStrategy = newRelativeStrategy;
			UpdatePositionStrategyVisibility();
			UpdatePositionToolsUi();
			return;
		}

		var targetMode = newPositioningMode ?? _settings.PositioningMode;
		var targetRelativeStrategy = newRelativeStrategy ?? _settings.RelativeStrategy;
		var enteringFreeRelative = string.Equals(targetMode, AppSettingsService.Relative, StringComparison.Ordinal) &&
			string.Equals(targetRelativeStrategy, AppSettingsService.CustomPanel, StringComparison.Ordinal) &&
			!IsCustomPanelStrategy();

		Dictionary<ControlNode, Rect>? visualBounds = null;
		if (enteringFreeRelative)
		{
			visualBounds = new Dictionary<ControlNode, Rect>();
			foreach (var node in _document.Nodes)
			{
				if (TryGetNativeBounds(node, out var bounds))
					visualBounds[node] = bounds;
				else
					visualBounds[node] = GetNodeVisualBounds(node);
			}
		}

		if (newPositioningMode is not null) _settings.PositioningMode = newPositioningMode;
		if (newRelativeStrategy is not null) _settings.RelativeStrategy = newRelativeStrategy;

		if (IsCustomPanelStrategy() && visualBounds != null)
		{
			var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
			foreach (var node in _document.Nodes)
			{
				if (!visualBounds.TryGetValue(node, out var bounds)) continue;
				node.OffsetX = Math.Clamp(bounds.X, 0, Math.Max(0, surfaceWidth - bounds.Width));
				node.OffsetY = Math.Clamp(bounds.Y, 0, Math.Max(0, surfaceHeight - bounds.Height));
				node.Row = 0;
				node.Column = 0;
				PersistVisualOffset(node);
			}
		}

		_document.RelativeFreePositioning = IsCustomPanelStrategy();
		UpdatePositionStrategyVisibility();
		UpdatePositionToolsUi();
		RenderCanvas();
	}

	private void UpdatePositionToolsUi()
	{
		var grid = IsGridRelativeStrategy();
		var custom = IsCustomPanelStrategy();
		var dock = IsDockPanelStrategy();
		var canvas = IsCanvasStrategy();
		var absoluteGrid = string.Equals(_settings.PositioningMode, AppSettingsService.Absolute, StringComparison.Ordinal) && !canvas;

		GridPositionTools.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
		CustomPanelPositionTools.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
		DockPanelPositionTools.Visibility = dock ? Visibility.Visible : Visibility.Collapsed;
		CanvasPositionTools.Visibility = canvas ? Visibility.Visible : Visibility.Collapsed;
		AbsoluteGridPositionTools.Visibility = absoluteGrid ? Visibility.Visible : Visibility.Collapsed;

		if (grid) {
			PositionToolsTitle.Text = "网格（Grid）";
			PositionToolsHint.Text = _document is not null ? $"当前布局：{_document.Rows} × {_document.Columns}；这里可增加/减少行列" : "当前使用 Grid 行列定位";
		}
		else if (custom) {
			PositionToolsTitle.Text = AppSettingsService.RelativeFreeDisplayName;
			PositionToolsHint.Text = "统一使用根 Panel 左上角为原点：HorizontalAlignment=Left、（吧啦吧啦）因此，此定位方式可以自由摆放控件而定位相对！";
		}
		else if (dock) {
			PositionToolsTitle.Text = "DockPanel";
			PositionToolsHint.Text = "为选中控件指定停靠边；拖动时会吸附到最近边";
		}
		else if (canvas) {
			PositionToolsTitle.Text = "Canvas（绝对坐标）";
			PositionToolsHint.Text = "使用 X / Y 坐标和标尺辅助线自由定位";
		}
		else {
			PositionToolsTitle.Text = "Grid（绝对值 / Margin）";
			PositionToolsHint.Text = string.Equals(_settings.AbsoluteStrategy, AppSettingsService.GridNegativeMargin, StringComparison.Ordinal)
				? "通过 Margin 偏移值定位；允许负值"
				: "通过精确边距定位控件；拖动和对齐会同步更新边距";
		}

		var hasSelection = _selected is not null;
		var positionEditorsEnabled = hasSelection && (canvas || absoluteGrid || custom || dock);
		OffsetXBox.IsEnabled = positionEditorsEnabled && canvas;
		OffsetYBox.IsEnabled = positionEditorsEnabled && canvas;
		GridOffsetXBox.IsEnabled = positionEditorsEnabled && absoluteGrid;
		GridOffsetYBox.IsEnabled = positionEditorsEnabled && absoluteGrid;
		DockBox.IsEnabled = positionEditorsEnabled && dock;

		var gridEnabled = _document is not null && _document.GridSizingSupported && grid;
		AddRowButton.IsEnabled = gridEnabled;
		RemoveRowButton.IsEnabled = gridEnabled && _document!.Rows > 1;
		AddColumnButton.IsEnabled = gridEnabled;
		RemoveColumnButton.IsEnabled = gridEnabled && _document!.Columns > 1;
	}

	private void DrawPositioningGuides(double cellWidth, double cellHeight, Brush gridBrush, Brush accentBrush)
	{
		if (IsGridRelativeStrategy()) {
			for (var c = 1; c < _document!.Columns; c++) AddLine(c * cellWidth, 0, 1, EditorOverlayCanvas.Height, gridBrush);
			for (var r = 1; r < _document.Rows; r++) AddLine(0, r * cellHeight, EditorOverlayCanvas.Width, 1, gridBrush);
			return;
		}

		if (IsCustomPanelStrategy()) {
			AddLine(8, 8, EditorOverlayCanvas.Width - 16, 1, accentBrush, 0.22);
			AddLine(8, EditorOverlayCanvas.Height - 9, EditorOverlayCanvas.Width - 16, 1, accentBrush, 0.22);
			AddLine(8, 8, 1, EditorOverlayCanvas.Height - 16, accentBrush, 0.22);
			AddLine(EditorOverlayCanvas.Width - 9, 8, 1, EditorOverlayCanvas.Height - 16, accentBrush, 0.22);
			AddLine(EditorOverlayCanvas.Width / 2, 0, 1, EditorOverlayCanvas.Height, accentBrush, 0.10);
			AddLine(0, EditorOverlayCanvas.Height / 2, EditorOverlayCanvas.Width, 1, accentBrush, 0.10);
			return;
		}

		if (IsDockPanelStrategy()) {
			DrawDockZone(0, 0, EditorOverlayCanvas.Width, 42, "Top", accentBrush);
			DrawDockZone(0, EditorOverlayCanvas.Height - 42, EditorOverlayCanvas.Width, 42, "Bottom", accentBrush);
			DrawDockZone(0, 0, 42, EditorOverlayCanvas.Height, "Left", accentBrush);
			DrawDockZone(EditorOverlayCanvas.Width - 42, 0, 42, EditorOverlayCanvas.Height, "Right", accentBrush);
			return;
		}

		if (IsCanvasStrategy()) {
			for (var x = 0; x <= EditorOverlayCanvas.Width; x += 50) AddLine(x, 0, x == 0 ? 2 : 1, EditorOverlayCanvas.Height, gridBrush, x % 100 == 0 ? 0.28 : 0.12);
			for (var y = 0; y <= EditorOverlayCanvas.Height; y += 50) AddLine(0, y, EditorOverlayCanvas.Width, y == 0 ? 2 : 1, gridBrush, y % 100 == 0 ? 0.28 : 0.12);
			for (var x = 0; x <= EditorOverlayCanvas.Width; x += 100) AddGuideLabel(x + 4, 4, x.ToString(CultureInfo.InvariantCulture));
			for (var y = 0; y <= EditorOverlayCanvas.Height; y += 100) AddGuideLabel(4, y + 18, y.ToString(CultureInfo.InvariantCulture));
			return;
		}

		for (var c = 1; c < _document!.Columns; c++) AddLine(c * cellWidth, 0, 1, EditorOverlayCanvas.Height, gridBrush, 0.16);
		for (var r = 1; r < _document.Rows; r++) AddLine(0, r * cellHeight, EditorOverlayCanvas.Width, 1, gridBrush, 0.16);
		AddLine(0, 0, EditorOverlayCanvas.Width, 2, accentBrush, 0.34);
		AddLine(0, 0, 2, EditorOverlayCanvas.Height, accentBrush, 0.34);
	}

	private void DrawDockZone(double left, double top, double width, double height, string label, Brush brush)
	{
		var fill = new Rectangle
		{
			Width = width, Height = height,
			Fill = new SolidColorBrush(PreviewColor(20, 0, 120, 215)),
			Stroke = brush, StrokeThickness = 1,
			IsHitTestVisible = false
		};
		Canvas.SetLeft(fill, left); Canvas.SetTop(fill, top); Canvas.SetZIndex(fill, -1); EditorOverlayCanvas.Children.Add(fill);

		var text = new TextBlock
		{
			Text = label, FontSize = 11, Opacity = 0.5,
			Width = width, Height = height, TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
		};
		Canvas.SetLeft(text, left); Canvas.SetTop(text, top); Canvas.SetZIndex(text, -1); EditorOverlayCanvas.Children.Add(text);
	}

	private void AddGuideLabel(double left, double top, string text)
	{
		var label = new TextBlock { Text = text, FontSize = 10, Opacity = 0.42, IsHitTestVisible = false };
		Canvas.SetLeft(label, left); Canvas.SetTop(label, top); EditorOverlayCanvas.Children.Add(label);
	}

	private Rect GetNodeVisualBounds(ControlNode node)
	{
		if (TryGetNativeBounds(node, out var bounds))
			return bounds;

		// Only used during the first layout pass. The visible editor overlay never uses this fallback.
		var width = ParseSize(node.Width, DefaultWidth(node.TypeName));
		var height = ParseSize(node.Height, DefaultHeight(node.TypeName));
		if (IsCanvasStrategy() || IsDockPanelStrategy())
			return new Rect(node.OffsetX, node.OffsetY, width, height);

		if (IsCustomPanelStrategy())
			return new Rect(node.OffsetX, node.OffsetY, width, height);

		var cellWidth = EditorOverlayCanvas.Width / Math.Max(1, _document?.Columns ?? 1);
		var cellHeight = EditorOverlayCanvas.Height / Math.Max(1, _document?.Rows ?? 1);
		return new Rect(node.Column * cellWidth, node.Row * cellHeight, width, height);
	}

	private void SetNodeVisualOrigin(ControlNode node, double left, double top, bool persist = true)
	{
		var bounds = TryGetNativeBounds(node, out var nativeBounds) ? nativeBounds : GetNodeVisualBounds(node);
		var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
		var clampedLeft = Math.Clamp(left, 0, Math.Max(0, surfaceWidth - bounds.Width));
		var clampedTop = Math.Clamp(top, 0, Math.Max(0, surfaceHeight - bounds.Height));

		if (IsCanvasStrategy())
		{
			node.OffsetX = clampedLeft;
			node.OffsetY = clampedTop;
			if (_nativeElements.TryGetValue(node, out var canvasNative))
			{
				Canvas.SetLeft(canvasNative, node.OffsetX);
				Canvas.SetTop(canvasNative, node.OffsetY);
			}
		}
		else if (IsCustomPanelStrategy())
		{
			// Canonical relative style: whole-root Grid slot + Left/Top + Margin.
			node.OffsetX = clampedLeft;
			node.OffsetY = clampedTop;
			node.Row = 0;
			node.Column = 0;
			if (_nativeElements.TryGetValue(node, out var customNative))
				ApplyNativeLayoutForDrag(customNative, node);
		}
		else if (IsDockPanelStrategy())
		{
			node.Dock = FindNearestDockEdge(new Point(clampedLeft + bounds.Width / 2, clampedTop + bounds.Height / 2));
			if (_nativeElements.TryGetValue(node, out var dockNative))
				DesignerDockPanel.SetDock(dockNative, node.Dock);
		}
		else if (_nativeElements.TryGetValue(node, out var gridNative))
		{
			var dx = clampedLeft - bounds.X;
			var dy = clampedTop - bounds.Y;
			var margin = gridNative.Margin;
			gridNative.Margin = new Thickness(margin.Left + dx, margin.Top + dy, margin.Right, margin.Bottom);
			node.OffsetX = gridNative.Margin.Left;
			node.OffsetY = gridNative.Margin.Top;
		}

		if (persist) PersistVisualOffset(node);
		QueueOverlaySync();
	}

	private (double Width, double Height) GetDesignSurfaceSize()
	{
		var width = _nativePreviewRoot is not null && double.IsFinite(_nativePreviewRoot.ActualWidth) && _nativePreviewRoot.ActualWidth > 0
			? _nativePreviewRoot.ActualWidth
			: DesignPreviewBaseWidth;
		var height = _nativePreviewRoot is not null && double.IsFinite(_nativePreviewRoot.ActualHeight) && _nativePreviewRoot.ActualHeight > 0
			? _nativePreviewRoot.ActualHeight
			: DesignPreviewBaseHeight - 42;
		return (width, height);
	}

	private static bool TryParseMarginOffset(string? value, out double x, out double y)
	{
		x = 0;
		y = 0;
		if (string.IsNullOrWhiteSpace(value)) return true;
		var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0 || parts.Length > 4) return false;
		if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
		if (parts.Length == 1) { y = x; return true; }
		return double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
	}

	private void PersistVisualOffset(ControlNode node)
	{
		if (IsCustomPanelStrategy())
		{
			node.Row = 0;
			node.Column = 0;
		}

		if (IsCanvasStrategy())
		{
			node.Properties["Canvas.Left"] = FormatDesignerNumber(node.OffsetX);
			node.Properties["Canvas.Top"] = FormatDesignerNumber(node.OffsetY);
			return;
		}

		// Custom Panel and the absolute-Grid strategies use Margin as their
		// persisted local offset. Never leave stale Canvas attached properties
		// behind, otherwise a later native rebuild can switch coordinate systems.
		node.Properties.Remove("Canvas.Left");
		node.Properties.Remove("Canvas.Top");
		var margin = string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},0,0", node.OffsetX, node.OffsetY);
		node.DesignerPositionCustomized = true;
		node.Properties["Margin"] = margin;
	}

	private static string FormatDesignerNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	private static double DefaultWidth(string typeName) => typeName is "TextBlock" or "CheckBox" or "RadioButton" ? 150 : 160;

	private ControlNode CreateNodeForPlacement(string typeName, Point point)
	{
		var node = new ControlNode {
			TypeName = typeName,
			Content = DefaultContent(typeName),
			XName = MakeUniqueName(typeName),
			EventName = null,
			EventKind = null,
			EventWasAutoGenerated = false
		};
		if (UsesFreePositioning()) {
			var width = DefaultWidth(typeName);
			var height = DefaultHeight(typeName);
			var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
			var left = Math.Clamp(point.X - width / 2, 0, Math.Max(0, surfaceWidth - width));
			var top = Math.Clamp(point.Y - height / 2, 0, Math.Max(0, surfaceHeight - height));
			node.OffsetX = left;
			node.OffsetY = top;
			if (IsCustomPanelStrategy()) {
				node.Row = 0;
				node.Column = 0;
			}
			PersistVisualOffset(node);
		} else {
			(node.Row, node.Column) = PointToCell(point);
		}
		if (IsDockPanelStrategy()) node.Dock = FindNearestDockEdge(point);
		return node;
	}

	private string FindNearestDockEdge(Point point)
	{
		var distances = new Dictionary<string, double> {
			["Left"] = point.X,
			["Right"] = EditorOverlayCanvas.Width - point.X,
			["Top"] = point.Y,
			["Bottom"] = EditorOverlayCanvas.Height - point.Y
		};
		return distances.OrderBy(x => x.Value).First().Key;
	}

	private void SnapDockToNearestEdge(ControlNode node)
	{
		var current = GetNodeVisualBounds(node);
		node.Dock = FindNearestDockEdge(new Point(current.X + current.Width / 2, current.Y + current.Height / 2));
		var bounds = GetNodeVisualBounds(node);
		const double margin = 8;
		switch (node.Dock) {
			case "Left": SetNodeVisualOrigin(node, margin, bounds.Y); break;
			case "Right": SetNodeVisualOrigin(node, EditorOverlayCanvas.Width - bounds.Width - margin, bounds.Y); break;
			case "Top": SetNodeVisualOrigin(node, bounds.X, margin); break;
			case "Bottom": SetNodeVisualOrigin(node, bounds.X, EditorOverlayCanvas.Height - bounds.Height - margin); break;
		}
	}

	private async void PositionOffsetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_ignorePropertyChanges || _selected is null || _document is null || double.IsNaN(args.NewValue)) return;
		CommitHistory();
		if (sender == OffsetXBox || sender == GridOffsetXBox) _selected.OffsetX = args.NewValue;
		else if (sender == OffsetYBox || sender == GridOffsetYBox) _selected.OffsetY = args.NewValue;
		PersistVisualOffset(_selected);
		_document.IsDirty = true;
		RenderCanvas();
		UpdatePositionToolsUi();
		await AutoWriteAsync();
	}

	private void PositionToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_ignorePropertyChanges || _selected is null || _document is null || sender is not ComboBox combo) return;
		var value = (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
		if (string.IsNullOrWhiteSpace(value)) return;
		if (string.Equals(_selected.Dock, value, StringComparison.Ordinal)) return;
		CommitHistory();
		_selected.Dock = value;
		_document.IsDirty = true;
		RenderCanvas();
		UpdatePositionToolsUi();
		_ = AutoWriteAsync();
	}

	private void PositionAlign_Click(object sender, RoutedEventArgs e)
	{
		if (_selected is null || _document is null || sender is not Button button || button.Tag is not string tag) return;
		CommitHistory();
		var bounds = GetNodeVisualBounds(_selected);
		var (surfaceWidth, surfaceHeight) = GetDesignSurfaceSize();
		const double margin = 8;
		var left = bounds.X;
		var top = bounds.Y;
		if (tag == "Left") left = margin;
		else if (tag == "Right") left = surfaceWidth - bounds.Width - margin;
		else if (tag == "CenterHorizontal") left = (surfaceWidth - bounds.Width) / 2;
		else if (tag == "Top") top = margin;
		else if (tag == "Bottom") top = surfaceHeight - bounds.Height - margin;
		else if (tag == "CenterVertical") top = (surfaceHeight - bounds.Height) / 2;
		SetNodeVisualOrigin(_selected, Math.Max(0, left), Math.Max(0, top));
		_document.IsDirty = true;
		RenderCanvas();
		UpdatePositionToolsUi();
		_ = AutoWriteAsync();
	}

	private void RefreshProperties() {
		_ignorePropertyChanges = true;
		try {
			var n = _selected;
			var isControl = n is not null;
			var isWindow = _document is not null && _windowSelected && n is null;

			SelectedTypeText.Text = isControl
				? $"{n!.TypeName}（控件）"
				: isWindow
					? $"{_document!.RootTypeName}（根容器）"
					: "未选择对象";

			// Root/window editors.
			WindowTitleBox.Text = RootValue("Title");
			WindowWidthBox.Text = RootValue("Width");
			WindowHeightBox.Text = RootValue("Height");
			WindowMinWidthBox.Text = RootValue("MinWidth");
			WindowMinHeightBox.Text = RootValue("MinHeight");
			WindowMaxWidthBox.Text = RootValue("MaxWidth");
			WindowMaxHeightBox.Text = RootValue("MaxHeight");
			SelectCombo(WindowThemeBox, RootValue("RequestedTheme"));
			SelectCombo(WindowExtendTitleBarBox, RootValue("ExtendsContentIntoTitleBar"));

			foreach (var control in WindowPropertyControls()) control.IsEnabled = _document is not null;

			// Core control fields.
			NameBox.Text = n?.XName ?? "";
			ContentBox.Text = n is null ? "" : (n.Content ?? DefaultContent(n.TypeName));
			RowBox.Value = n?.Row ?? 0;
			ColumnBox.Value = n?.Column ?? 0;
			RowSpanBox.Value = n?.RowSpan ?? 1;
			ColumnSpanBox.Value = n?.ColumnSpan ?? 1;
			WidthBox.Text = n is null ? "" : (n.Width ?? "Auto");
			HeightBox.Text = n is null ? "" : (n.Height ?? "Auto");
			SelectCombo(HorizontalAlignmentBox, EffectivePropertyValue(n, "HorizontalAlignment"));
			SelectCombo(VerticalAlignmentBox, EffectivePropertyValue(n, "VerticalAlignment"));

			SetText(PropertyBox("MinWidth"), EffectivePropertyValue(n, "MinWidth"));
			SetText(PropertyBox("MaxWidth"), EffectivePropertyValue(n, "MaxWidth"));
			SetText(PropertyBox("MinHeight"), EffectivePropertyValue(n, "MinHeight"));
			SetText(PropertyBox("MaxHeight"), EffectivePropertyValue(n, "MaxHeight"));
			SetText(PropertyBox("Margin"), EffectivePropertyValue(n, "Margin"));
			SetText(PropertyBox("Padding"), EffectivePropertyValue(n, "Padding"));
			SetText(PropertyBox("FontSize"), EffectivePropertyValue(n, "FontSize"));
			SelectCombo(FontWeightBox, EffectivePropertyValue(n, "FontWeight"));
			SetText(PropertyBox("CharacterSpacing"), EffectivePropertyValue(n, "CharacterSpacing"));
			SetText(PropertyBox("Foreground"), EffectivePropertyValue(n, "Foreground"));
			SetText(PropertyBox("Background"), EffectivePropertyValue(n, "Background"));
			SetText(PropertyBox("BorderBrush"), EffectivePropertyValue(n, "BorderBrush"));
			SetText(PropertyBox("BorderThickness"), EffectivePropertyValue(n, "BorderThickness"));
			SetText(PropertyBox("CornerRadius"), EffectivePropertyValue(n, "CornerRadius"));
			SetText(PropertyBox("Opacity"), EffectivePropertyValue(n, "Opacity"));
			SelectCombo(VisibilityBox, EffectivePropertyValue(n, "Visibility"));
			SelectCombo(IsEnabledBox, EffectivePropertyValue(n, "IsEnabled"));
			SelectCombo(IsTabStopBox, EffectivePropertyValue(n, "IsTabStop"));
			SelectCombo(IsHitTestVisibleBox, EffectivePropertyValue(n, "IsHitTestVisible"));
			SelectCombo(HorizontalContentAlignmentBox, EffectivePropertyValue(n, "HorizontalContentAlignment"));
			SelectCombo(VerticalContentAlignmentBox, EffectivePropertyValue(n, "VerticalContentAlignment"));
			SetText(ToolTipBox, EffectivePropertyValue(n, "ToolTip"));
			SetText(PropertyBox("PlaceholderText"), EffectivePropertyValue(n, "PlaceholderText"));
			SetText(PropertyBox("Header"), EffectivePropertyValue(n, "Header"));
			SetText(SourceBox, EffectivePropertyValue(n, "Source"));
			SelectCombo(StretchBox, EffectivePropertyValue(n, "Stretch"));
			SelectCombo(TextWrappingBox, EffectivePropertyValue(n, "TextWrapping"));
			SetText(PropertyBox("MaxLength"), EffectivePropertyValue(n, "MaxLength"));
			SetText(PropertyBox("SelectedIndex"), EffectivePropertyValue(n, "SelectedIndex"));
			SetText(PropertyBox("Minimum"), EffectivePropertyValue(n, "Minimum"));
			SetText(PropertyBox("Maximum"), EffectivePropertyValue(n, "Maximum"));
			SetText(PropertyBox("Value"), EffectivePropertyValue(n, "Value"));
			SelectCombo(OrientationBox, EffectivePropertyValue(n, "Orientation"));
			SelectCombo(IsCheckedBox, EffectivePropertyValue(n, "IsChecked"));
			SelectCombo(IsOnBox, EffectivePropertyValue(n, "IsOn"));
			SetText(OnContentBox, EffectivePropertyValue(n, "OnContent"));
			SetText(OffContentBox, EffectivePropertyValue(n, "OffContent"));
			SetText(GroupNameBox, EffectivePropertyValue(n, "GroupName"));
			SelectCombo(IsEditableBox, EffectivePropertyValue(n, "IsEditable"));
			SelectCombo(AcceptsReturnBox, EffectivePropertyValue(n, "AcceptsReturn"));
			SelectCombo(IsReadOnlyBox, EffectivePropertyValue(n, "IsReadOnly"));
			SetText(FontFamilyBox, EffectivePropertyValue(n, "FontFamily"));
			SelectCombo(FontStyleBox, EffectivePropertyValue(n, "FontStyle"));
			SelectCombo(TextAlignmentBox, EffectivePropertyValue(n, "TextAlignment"));
			SelectCombo(FlowDirectionBox, EffectivePropertyValue(n, "FlowDirection"));
			SetText(TabIndexBox, EffectivePropertyValue(n, "TabIndex"));
			SelectCombo(UseSystemFocusVisualsBox, EffectivePropertyValue(n, "UseSystemFocusVisuals"));
			OffsetXBox.Value = n?.OffsetX ?? 0;
			OffsetYBox.Value = n?.OffsetY ?? 0;
			GridOffsetXBox.Value = n?.OffsetX ?? 0;
			GridOffsetYBox.Value = n?.OffsetY ?? 0;
			SelectCombo(DockBox, n?.Dock);
			SetText(CommandBox, EffectivePropertyValue(n, "Command"));
			SetText(CommandParameterBox, EffectivePropertyValue(n, "CommandParameter"));
			SelectCombo(IsDefaultBox, EffectivePropertyValue(n, "IsDefault"));
			SelectCombo(IsCancelBox, EffectivePropertyValue(n, "IsCancel"));
			SelectCombo(ClickModeBox, EffectivePropertyValue(n, "ClickMode"));
			SelectCombo(IsIndeterminateBox, EffectivePropertyValue(n, "IsIndeterminate"));
			SetText(DateFormatBox, EffectivePropertyValue(n, "DateFormat"));
			SetText(NumberFormatBox, EffectivePropertyValue(n, "NumberFormat"));
			SetText(LanguageBox, EffectivePropertyValue(n, "Language"));
			SetText(TagBox, EffectivePropertyValue(n, "Tag"));

			foreach (var editor in ControlPropertyEditors()) editor.IsEnabled = false;
			if (n is not null) {
				foreach (var pair in ControlPropertyEditorMap())
					pair.Value.IsEnabled = n.IsPropertyEnabled(pair.Key);
				NameBox.IsEnabled = true;
				ContentBox.IsEnabled = SupportsContentText(n);
				var gridPositionEditorsEnabled = IsGridRelativeStrategy();
				RowBox.IsEnabled = gridPositionEditorsEnabled;
				ColumnBox.IsEnabled = gridPositionEditorsEnabled;
				RowSpanBox.IsEnabled = gridPositionEditorsEnabled;
				ColumnSpanBox.IsEnabled = gridPositionEditorsEnabled;
				if (IsCustomPanelStrategy())
				{
					HorizontalAlignmentBox.IsEnabled = false;
					VerticalAlignmentBox.IsEnabled = false;
				}
				DeleteButton.IsEnabled = true;
			} else {
				NameBox.IsEnabled = false;
				ContentBox.IsEnabled = false;
				RowBox.IsEnabled = false;
				ColumnBox.IsEnabled = false;
				RowSpanBox.IsEnabled = false;
				ColumnSpanBox.IsEnabled = false;
				DeleteButton.IsEnabled = false;
			}

			ContentBox.AcceptsReturn = n is not null &&
				(n.TypeName == "TextBox" || n.TypeName == "RichEditBox");

			PreviewWindowTitleText.Text = GetPreviewWindowTitle();

			EventText.Text = n?.EventName is null
				? (n is null ? "选择控件后可查看事件" : "未生成事件")
				: $"{n.EventKind ?? n.DerivedEventKind} → {n.EventName}" + (n.EventWasAutoGenerated ? "（自动）" : "（用户手写）");
			UpdatePositionToolsUi();
		} finally {
			_ignorePropertyChanges = false;
		}
	}

	private static bool SupportsContentText(ControlNode n) {
		return n.TypeName == "Button" || n.TypeName == "HyperlinkButton" || n.TypeName == "ToggleButton" ||
			   n.TypeName == "RepeatButton" || n.TypeName == "CheckBox" || n.TypeName == "RadioButton" ||
			   n.TypeName == "Expander" || n.TypeName == "TextBlock" || n.TypeName == "TextBox" ||
			   n.TypeName == "PasswordBox" || n.TypeName == "AutoSuggestBox" || n.TypeName == "RichEditBox";
	}

	private TextBox PropertyBox(string key) {
		if (key == "MinWidth") return MinWidthBox;
		if (key == "MaxWidth") return MaxWidthBox;
		if (key == "MinHeight") return MinHeightBox;
		if (key == "MaxHeight") return MaxHeightBox;
		if (key == "Margin") return MarginBox;
		if (key == "Padding") return PaddingBox;
		if (key == "FontSize") return FontSizeBox;
		if (key == "CharacterSpacing") return CharacterSpacingBox;
		if (key == "Foreground") return ForegroundBox;
		if (key == "Background") return BackgroundBox;
		if (key == "BorderBrush") return BorderBrushBox;
		if (key == "BorderThickness") return BorderThicknessBox;
		if (key == "CornerRadius") return CornerRadiusBox;
		if (key == "Opacity") return OpacityBox;
		if (key == "ToolTip") return ToolTipBox;
		if (key == "PlaceholderText") return PlaceholderTextBox;
		if (key == "Header") return HeaderBox;
		if (key == "MaxLength") return MaxLengthBox;
		if (key == "SelectedIndex") return SelectedIndexBox;
		if (key == "Minimum") return MinimumBox;
		if (key == "Maximum") return MaximumBox;
		if (key == "Value") return ValueBox;
		throw new KeyNotFoundException(key);
	}

	private IReadOnlyDictionary<string, Control> ControlPropertyEditorMap() => new Dictionary<string, Control>(StringComparer.Ordinal) {
		["ToolTip"] = ToolTipBox,
		["PlaceholderText"] = PlaceholderTextBox,
		["Header"] = HeaderBox,
		["Source"] = SourceBox,
		["Stretch"] = StretchBox,
		["MinWidth"] = MinWidthBox, ["MaxWidth"] = MaxWidthBox,
		["MinHeight"] = MinHeightBox, ["MaxHeight"] = MaxHeightBox,
		["Margin"] = MarginBox, ["Padding"] = PaddingBox,
		["HorizontalContentAlignment"] = HorizontalContentAlignmentBox,
		["VerticalContentAlignment"] = VerticalContentAlignmentBox,
		["FontSize"] = FontSizeBox, ["FontWeight"] = FontWeightBox, ["CharacterSpacing"] = CharacterSpacingBox,
		["Foreground"] = ForegroundBox, ["Background"] = BackgroundBox,
		["BorderBrush"] = BorderBrushBox, ["BorderThickness"] = BorderThicknessBox, ["CornerRadius"] = CornerRadiusBox,
		["Opacity"] = OpacityBox, ["Visibility"] = VisibilityBox, ["IsEnabled"] = IsEnabledBox,
		["IsTabStop"] = IsTabStopBox, ["IsHitTestVisible"] = IsHitTestVisibleBox,
		["TextWrapping"] = TextWrappingBox, ["AcceptsReturn"] = AcceptsReturnBox, ["IsReadOnly"] = IsReadOnlyBox,
		["MaxLength"] = MaxLengthBox, ["SelectedIndex"] = SelectedIndexBox, ["Minimum"] = MinimumBox,
		["Maximum"] = MaximumBox, ["Value"] = ValueBox, ["Orientation"] = OrientationBox,
		["IsChecked"] = IsCheckedBox, ["IsOn"] = IsOnBox, ["OnContent"] = OnContentBox, ["OffContent"] = OffContentBox,
		["GroupName"] = GroupNameBox, ["IsEditable"] = IsEditableBox,
		["FontFamily"] = FontFamilyBox, ["FontStyle"] = FontStyleBox, ["TextAlignment"] = TextAlignmentBox,
		["FlowDirection"] = FlowDirectionBox, ["TabIndex"] = TabIndexBox, ["UseSystemFocusVisuals"] = UseSystemFocusVisualsBox,
		["Command"] = CommandBox, ["CommandParameter"] = CommandParameterBox, ["IsDefault"] = IsDefaultBox,
		["IsCancel"] = IsCancelBox, ["ClickMode"] = ClickModeBox, ["IsIndeterminate"] = IsIndeterminateBox,
		["DateFormat"] = DateFormatBox, ["NumberFormat"] = NumberFormatBox, ["Language"] = LanguageBox, ["Tag"] = TagBox
	};

	private IEnumerable<Control> ControlPropertyEditors() => ControlPropertyEditorMap().Values;
	private bool IsWindowPropertyEditor(Control control) => WindowPropertyControls().Contains(control);
	private IEnumerable<Control> WindowPropertyControls() => new Control[]
	{
		WindowTitleBox, WindowWidthBox, WindowHeightBox, WindowMinWidthBox, WindowMinHeightBox,
		WindowMaxWidthBox, WindowMaxHeightBox, WindowThemeBox, WindowExtendTitleBarBox
	};

	private static void SetText(TextBox? box, string? value) {
		if (box is not null) box.Text = value ?? "";
	}

	private string? EffectivePropertyValue(ControlNode? node, string key)
	{
		if (node is null) return null;
		if (node.Properties.TryGetValue(key, out var explicitValue) && !string.IsNullOrWhiteSpace(explicitValue))
			return explicitValue;

		if (_nativeElements.TryGetValue(node, out var element) && TryReadNativeProperty(element, key, out var nativeValue))
			return nativeValue;

		return key switch
		{
			"Width" or "Height" => "Auto",
			"HorizontalAlignment" or "VerticalAlignment" => "Stretch",
			"Margin" or "Padding" => "0,0,0,0",
			"FontSize" => "14",
			"FontWeight" => "Normal",
			"Opacity" => "1",
			"Visibility" => "Visible",
			"IsEnabled" or "IsTabStop" or "IsHitTestVisible" => "True",
			"HorizontalContentAlignment" or "VerticalContentAlignment" => "Stretch",
			"CharacterSpacing" => "0",
			_ => null
		};
	}

	private static bool TryReadNativeProperty(FrameworkElement element, string key, out string? value)
	{
		value = null;
		try
		{
			var propertyName = key switch
			{
				"ContentOrText" => null,
				"ToolTip" => null,
				"Canvas.Left" => null,
				"Canvas.Top" => null,
				_ => key
			};

			object? raw = key switch
			{
				"ContentOrText" => element switch
				{
					TextBlock textBlock => textBlock.Text,
					TextBox textBox => textBox.Text,
					PasswordBox passwordBox => passwordBox.Password,
					AutoSuggestBox autoSuggestBox => autoSuggestBox.Text,
					ContentControl contentControl => contentControl.Content,
					_ => null
				},
				"ToolTip" => ToolTipService.GetToolTip(element),
				"Canvas.Left" => Canvas.GetLeft(element),
				"Canvas.Top" => Canvas.GetTop(element),
				_ => propertyName is null
					? null
					: element.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(element)
			};

			if (raw is null) return false;

			value = raw switch
			{
				Thickness thickness => FormatThickness(thickness),
				CornerRadius cornerRadius => $"{cornerRadius.TopLeft:0.##},{cornerRadius.TopRight:0.##},{cornerRadius.BottomRight:0.##},{cornerRadius.BottomLeft:0.##}",
				FontWeight fontWeight => fontWeight.Weight <= 400 ? "Normal" : fontWeight.Weight >= 700 ? "Bold" : fontWeight.Weight.ToString(CultureInfo.InvariantCulture),
				SolidColorBrush brush => $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}",
				FontFamily fontFamily => fontFamily.Source,
				Enum enumValue => enumValue.ToString(),
				bool boolean => boolean ? "True" : "False",
				double number => number.ToString("0.##", CultureInfo.InvariantCulture),
				float number => number.ToString("0.##", CultureInfo.InvariantCulture),
				_ => Convert.ToString(raw, CultureInfo.InvariantCulture)
			};

			return value is not null;
		}
		catch
		{
			value = null;
			return false;
		}
	}

	private string? RootValue(string key) => _document is not null && _document.RootProperties.TryGetValue(key, out var value) ? value : null;

	private static string FormatThickness(Thickness value) => $"{value.Left:0.##},{value.Top:0.##},{value.Right:0.##},{value.Bottom:0.##}";

	private static void SelectCombo(ComboBox combo, string? value) {
		combo.SelectedIndex = 0;
		if (string.IsNullOrWhiteSpace(value)) return;
		for (var i = 0; i < combo.Items.Count; i++)
			if ((combo.Items[i] as ComboBoxItem)?.Content?.ToString() == value) {
				combo.SelectedIndex = i;
				break;
			}
	}

	private string GetPreviewWindowTitle() {
		var configured = RootValue("Title");
		if (!string.IsNullOrWhiteSpace(configured))
			return configured!;
		if (_document is not null) {
			var name = System.IO.Path.GetFileNameWithoutExtension(_document.FilePath);
			if (!string.IsNullOrWhiteSpace(name)) return name;
			if (!string.IsNullOrWhiteSpace(_document.RootTypeName)) return _document.RootTypeName;
		}
		return "Window";
	}

	private void UpdateAppTitleBar()
	{
		AppTitleBar.Title = "XAML Sub";
		if (_document is null)
		{
			AppTitleBar.Subtitle = "就绪";
			return;
		}

		var fileName = System.IO.Path.GetFileNameWithoutExtension(_document.FilePath);
		AppTitleBar.Subtitle = string.IsNullOrWhiteSpace(fileName)
			? "就绪"
			: "正在编辑：" + fileName + ".xaml";
	}

	private void UpdateUi() {
		UpdateAppTitleBar();
		StatusText.Text = _document is null ? "未打开文件" : $"{_document.FilePath}{(_document.IsDirty ? "  -  未保存" : "  -    已保存")}";
		GridStatusText.Text = _document is null
			? "未打开文档"
			: IsGridRelativeStrategy()
				? (_document.GridSizingSupported ? $"Grid {_document.Rows} × {_document.Columns}" : $"Grid {_document.Rows} × {_document.Columns} - 仅预览")
				: $"{_settings.PositioningMode} - {(_settings.PositioningMode == AppSettingsService.Relative ? _settings.RelativeStrategy : _settings.AbsoluteStrategy)}";
		UndoButton.IsEnabled = _undo.Count > 0;
		RedoButton.IsEnabled = _redo.Count > 0;
		UpdateZoomHint();
		RefreshProperties();
		UpdatePositionToolsUi();
	}

	private string MakeUniqueName(string typeName) {
		var i = 1;
		while (_document?.Nodes.Any(n => n.XName == typeName + i) == true) i++;
		return typeName + i;
	}

	private static string DefaultContent(string typeName) {
		if (typeName == "Button") return "Button";
		if (typeName == "TextBlock") return "TextBlock";
		if (typeName == "CheckBox") return "CheckBox";
		if (typeName == "RadioButton") return "RadioButton";
		if (typeName == "ComboBox") return "ComboBox";
		return typeName;
	}

	private static TextBlock CenterText(string text) => new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };

	private static double DefaultHeight(string typeName) => typeName == "TextBlock" ? 28 : 42;
	private static double ParseSize(string? value, double fallback) => double.TryParse(value, out var d) && d > 0 ? d : fallback;

	private async Task ShowErrorAsync(string message) {
		var dialog = CreateDialog(
			title: "发现错误",
			content: message,
			closeButtonText: "确定",
			defaultButton: ContentDialogButton.Close);
		await dialog.ShowAsync();
	}

	private async Task ShowCodeBehindSaveFallbackAsync(string message)
	{
		var dialogContent = message + "\n\n你可以只保存当前 XAML，但这可能导致XAML与C#逻辑文件不同步";
		var dialog = CreateDialog(
			title: "保存失败",
			content: dialogContent,
			primaryButtonText: "仅保存XAML",
			closeButtonText: "确定",
			defaultButton: ContentDialogButton.Close);

		if (await dialog.ShowAsync() != ContentDialogResult.Primary)
			return;

		await SaveXamlOnlyAsync();
	}

	private async Task SaveXamlOnlyAsync()
	{
		if (_document is null)
			return;

		try
		{
			await CommitFocusedEditorAsync();
			await _writeGate.WaitAsync();
			try
			{
				_document.Normalize();
				_document.RelativeFreePositioning = IsCustomPanelStrategy();
				await new WriteBackService(_xaml, _csharp).WriteXamlOnlyAsync(_document);
				_savedSnapshot = _document.Clone();
				StatusText.Text = $"已仅保存 XAML：{_document.FilePath}；未修改 code-behind";
				UpdateUi();
			}
			finally
			{
				_writeGate.Release();
			}
		}
		catch (Exception ex)
		{
			await ShowErrorAsync($"仅保存 XAML 失败：{ex.Message}");
		}
	}

	private ContentDialog CreateDialog(
		string title,
		object content,
		string? primaryButtonText = null,
		string? closeButtonText = null,
		ContentDialogButton defaultButton = ContentDialogButton.None) {
		var style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
		return new ContentDialog {
			XamlRoot = Content.XamlRoot,
			Style = style,
			Title = title,
			Content = content,
			PrimaryButtonText = primaryButtonText,
			CloseButtonText = closeButtonText,
			DefaultButton = defaultButton
		};
	}

}

