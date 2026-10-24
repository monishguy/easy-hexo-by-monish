using System.Diagnostics;
using System.Text;
using EasyHexo.Localization;
using EasyHexo.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo.Views;

/// <summary>
/// 运行页：把原来 Hexo.bat 的常用操作搬到界面里（生成 / 清理 / 预览 / 部署 / 新建文章…）。
/// 执行时显示进度圈，并把命令输出实时显示在下方。
/// </summary>
public sealed partial class RunPage : Page
{
    private readonly StringBuilder _output = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private Process? _process;
    private CancellationTokenSource? _cancellation;
    private bool _running;

    public RunPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => UpdateWorkingState();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        // 离开页面时结束仍在运行的命令，避免遗留 hexo server 进程
        StopProcess();
    }

    private void UpdateWorkingState()
    {
        var project = AppState.Current.Project;
        WorkDirText.Text = project?.RootPath ?? Lang.T("run.noSite");

        var available = project is not null && !_running;
        CommandHost.IsEnabled = available;
        ParamHost.IsEnabled = available;
    }

    private void SetRunning(bool running, string? arguments)
    {
        _running = running;
        BusyRing.IsActive = running;
        BusyRing.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunStatusText.Text = running
            ? Lang.T("run.running", arguments ?? string.Empty)
            : string.Empty;

        var project = AppState.Current.Project;
        var available = project is not null && !running;
        CommandHost.IsEnabled = available;
        ParamHost.IsEnabled = available;
        StopButton.IsEnabled = running;

        if (!running)
        {
            UpdateWorkingState();
        }
    }

    // ── 命令按钮 ────────────────────────────────────────────────────

    private async void OnGenerateClick(object sender, RoutedEventArgs e) => await RunAsync("hexo generate");

    private async void OnCleanClick(object sender, RoutedEventArgs e) => await RunAsync("hexo clean");

    private async void OnServerClick(object sender, RoutedEventArgs e) => await RunAsync("hexo server");

    private async void OnDeployClick(object sender, RoutedEventArgs e) => await RunAsync("hexo deploy");

    private async void OnGenerateDeployClick(object sender, RoutedEventArgs e) => await RunAsync("hexo generate --deploy");

    private async void OnGenerateForceClick(object sender, RoutedEventArgs e) => await RunAsync("hexo generate --force");

    private async void OnGenerateWatchClick(object sender, RoutedEventArgs e) => await RunAsync("hexo generate --watch");

    private async void OnListPostsClick(object sender, RoutedEventArgs e) => await RunAsync("hexo list post");

    private async void OnVersionClick(object sender, RoutedEventArgs e) => await RunAsync("hexo version");

    private async void OnNewPostClick(object sender, RoutedEventArgs e)
    {
        var value = ValidateInput(NewPostBox.Text, "run.newPostHint");
        if (value is null)
        {
            return;
        }

        await RunAsync($"hexo new \"{value}\"");
    }

    private async void OnNewPageClick(object sender, RoutedEventArgs e)
    {
        var value = ValidateInput(NewPageBox.Text, "run.newPageHint");
        if (value is null)
        {
            return;
        }

        await RunAsync($"hexo new page \"{value}\"");
    }

    private async void OnPublishDraftClick(object sender, RoutedEventArgs e)
    {
        var value = ValidateInput(DraftBox.Text, "run.publishDraftHint");
        if (value is null)
        {
            return;
        }

        await RunAsync($"hexo publish \"{value}\"");
    }

    private string? ValidateInput(string? text, string labelKey)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            ShowInfo(InfoBarSeverity.Warning, Lang.T("run.needInput", Lang.T(labelKey)));
            return null;
        }

        // 参数里不能带引号，避免破坏命令行
        return value.Replace("\"", string.Empty);
    }

    // ── 执行命令 ────────────────────────────────────────────────────

    private async Task RunAsync(string commandLine)
    {
        if (_running)
        {
            return;
        }

        var project = AppState.Current.Project;
        if (project is null)
        {
            ShowInfo(InfoBarSeverity.Error, Lang.T("run.noSite"));
            return;
        }

        _output.Clear();
        OutputBox.Text = string.Empty;
        Append($"> {commandLine}\r\n\r\n");

        SetRunning(true, commandLine);
        _cancellation = new CancellationTokenSource();
        var cancellation = _cancellation;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // 强制 UTF-8 输出，避免中文乱码
                Arguments = "/d /c chcp 65001>nul & " + commandLine,
                WorkingDirectory = project.RootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Append(args.Data + "\r\n");
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Append(args.Data + "\r\n");
                }
            };

            _process = process;
            if (!process.Start())
            {
                ShowInfo(InfoBarSeverity.Error, Lang.T("run.failed", commandLine));
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var cancelledTask = Task.Delay(Timeout.Infinite, cancellation.Token);
            var completed = await Task.WhenAny(exitTask, cancelledTask);

            if (completed != exitTask)
            {
                KillProcess();
                await exitTask;
            }

            // 确保异步输出流已经读完
            process.WaitForExit(2000);

            var cancelled = cancellation.IsCancellationRequested;
            Append(cancelled
                ? "\r\n" + Lang.T("run.stopped") + "\r\n"
                : "\r\n" + Lang.T("run.finished", process.ExitCode) + "\r\n");

            ShowInfo(
                cancelled || process.ExitCode == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                cancelled
                    ? Lang.T("run.stopped")
                    : Lang.T("run.finished", process.ExitCode));
        }
        catch (Exception ex)
        {
            AppLog.Write("运行命令失败", ex);
            Append(ex.Message + "\r\n");
            ShowInfo(InfoBarSeverity.Error, Lang.T("run.failed", ex.Message));
        }
        finally
        {
            _process = null;
            cancellation.Dispose();
            _cancellation = null;
            SetRunning(false, null);
        }
    }

    private void StopProcess()
    {
        _cancellation?.Cancel();
        KillProcess();
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopProcess();

    private void KillProcess()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程可能已经退出
        }
    }

    private void OnClearOutputClick(object sender, RoutedEventArgs e)
    {
        _output.Clear();
        OutputBox.Text = string.Empty;
        StatusBar.IsOpen = false;
    }

    private void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _output.Append(text);
        var content = _output.ToString();
        _dispatcher.TryEnqueue(() =>
        {
            OutputBox.Text = content;
            OutputBox.SelectionStart = OutputBox.Text.Length;
        });
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = severity switch
        {
            InfoBarSeverity.Success => Lang.T("common.success"),
            InfoBarSeverity.Warning => Lang.T("common.warning"),
            InfoBarSeverity.Error => Lang.T("common.error"),
            _ => Lang.T("common.info"),
        };
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
