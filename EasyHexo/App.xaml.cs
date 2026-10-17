using EasyHexo.Services;
using Microsoft.UI.Xaml;

namespace EasyHexo;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        // 启动最早时机：读取设置 + 应用语言 + 恢复上次的 Hexo 根目录
        AppState.Current.Initialize();
    }

    /// <summary>主窗口（WinUI 3 里没有 UWP 的 Window.Current，这里自己保存一份）。</summary>
    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Write("未处理异常", e.Exception);
        e.Handled = true;
    }
}
