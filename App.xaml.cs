using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace XamlSub;

public partial class App : Application
{
    public const int DefaultWindowWidth = 1426;
    public const int DefaultWindowHeight = 869;

    public static Window? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();

        // Match the reference startup window size. This is the Win32/AppWindow
        // client window size used for the initial desktop window; users can
        // still resize or maximize it normally afterwards.
        var hwnd = WindowNative.GetWindowHandle(MainWindowInstance);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
    }
}
