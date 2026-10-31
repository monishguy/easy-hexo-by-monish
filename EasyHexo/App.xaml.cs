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

        // 资源管理器右键菜单 / 命令行传入的“发布到 Hexo 博客”文件
        StartupPublishPath = ParsePublishArgument();
    }

    /// <summary>主窗口（WinUI 3 里没有 UWP 的 Window.Current，这里自己保存一份）。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>启动参数 --publish &lt;file&gt; 指定的文件。</summary>
    public static string? StartupPublishPath { get; private set; }

    private static string? ParsePublishArgument()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], "--publish", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[i + 1];
            }
        }

        return null;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();

        if (!string.IsNullOrEmpty(StartupPublishPath))
        {
            AppLog.Write("启动参数 --publish: " + StartupPublishPath);
            window.OpenPublishFile(StartupPublishPath);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Write("未处理异常", e.Exception);
        e.Handled = true;
    }
}
