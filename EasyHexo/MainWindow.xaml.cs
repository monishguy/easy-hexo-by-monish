using EasyHexo.Localization;
using EasyHexo.Services;
using EasyHexo.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = Lang.T("app.title");
        AppState.Current.ProjectChanged += OnProjectChanged;
        Lang.Changed += OnLanguageChanged;

        // 自绘标题栏 + Win11 风格的窗口尺寸
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            // 给配置列表 / 文章编辑器留出足够空间；失败也不影响启动
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));
        }
        catch (Exception ex)
        {
            AppLog.Write("设置窗口尺寸失败", ex);
        }

        UpdateRootPath();

        // 默认进入“配置编辑”
        NavView.SelectedItem = NavConfig;
        if (ContentFrame.CurrentSourcePageType is null)
        {
            ContentFrame.Navigate(typeof(ConfigEditorPage));
        }
    }

    private void OnProjectChanged(object? sender, EventArgs e) => UpdateRootPath();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = Lang.T("app.title");
        UpdateRootPath();
    }

    private void UpdateRootPath()
    {
        RootPathText.Text = AppState.Current.Project?.RootPath ?? Lang.T("root.none");
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "config" => typeof(ConfigEditorPage),
            "publish" => typeof(PublishPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType);
    }
}
