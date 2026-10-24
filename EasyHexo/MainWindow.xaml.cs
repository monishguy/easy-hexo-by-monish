using EasyHexo.Localization;
using EasyHexo.Services;
using EasyHexo.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo;

public sealed partial class MainWindow : Window
{
    /// <summary>窗口最小尺寸：小于该尺寸时配置列表 / 发布页会被压变形。</summary>
    private const int MinimumWindowWidth = 1040;

    private const int MinimumWindowHeight = 680;

    public MainWindow()
    {
        InitializeComponent();

        Title = Lang.T("app.title");
        AppState.Current.ProjectChanged += OnProjectChanged;
        AppState.Current.ThemeChanged += OnThemeChanged;
        Lang.Changed += OnLanguageChanged;

        // 自绘标题栏 + Win11 风格的窗口尺寸
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            // 给配置列表 / 文章编辑器留出足够空间；失败也不影响启动
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 860));

            // 最小尺寸，避免缩放到很小时界面错乱
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = MinimumWindowWidth;
                presenter.PreferredMinimumHeight = MinimumWindowHeight;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("设置窗口尺寸失败", ex);
        }

        ApplyTheme();
        UpdateRootPath();
        UpdatePaneFooterVisibility();

        // 默认进入“配置编辑”
        NavView.SelectedItem = NavConfig;
        if (ContentFrame.CurrentSourcePageType is null)
        {
            ContentFrame.Navigate(typeof(ConfigEditorPage));
        }
    }

    private void OnProjectChanged(object? sender, EventArgs e) => UpdateRootPath();

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = Lang.T("app.title");
        UpdateRootPath();
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = ThemeService.ToElementTheme(AppState.Current.Theme);
    }

    private void UpdateRootPath()
    {
        RootPathText.Text = AppState.Current.Project?.RootPath ?? Lang.T("root.none");
    }

    /// <summary>侧栏根目录信息：仅在侧栏展开（窗口较宽或手动展开）时显示。</summary>
    private void UpdatePaneFooterVisibility()
    {
        RootInfoPanel.Visibility = NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavPaneChanged(NavigationView sender, object args) => UpdatePaneFooterVisibility();

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        UpdatePaneFooterVisibility();

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
