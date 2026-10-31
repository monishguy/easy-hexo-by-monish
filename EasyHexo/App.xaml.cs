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
        StartupPublishPaths = ParsePublishArguments();
    }

    /// <summary>主窗口（WinUI 3 里没有 UWP 的 Window.Current，这里自己保存一份）。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>启动参数 --publish &lt;files...&gt; 指定的文件（支持多选）。</summary>
    public static IReadOnlyList<string> StartupPublishPaths { get; private set; } = Array.Empty<string>();

    private static List<string> ParsePublishArguments()
    {
        var result = new List<string>();
        var arguments = Environment.GetCommandLineArgs();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (!string.Equals(arguments[i], "--publish", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var j = i + 1; j < arguments.Length; j++)
            {
                if (arguments[j].StartsWith("--", StringComparison.Ordinal))
                {
                    break;
                }

                if (File.Exists(arguments[j]))
                {
                    result.Add(arguments[j]);
                }
            }

            break;
        }

        return result;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();

        if (StartupPublishPaths.Count > 0)
        {
            AppLog.Write("启动参数 --publish: " + string.Join(" | ", StartupPublishPaths));
            window.OpenPublishFiles(StartupPublishPaths);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Write("未处理异常", e.Exception);
        e.Handled = true;
    }
}
