using EasyHexo.Helpers;
using EasyHexo.Localization;
using EasyHexo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo.Views;

/// <summary>设置页：语言、外观主题、Hexo 根目录切换、关于信息。</summary>
public sealed partial class SettingsPage : Page
{
    private bool _suppressLanguageChange;
    private bool _suppressThemeChange;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => ApplyLocalization();

    private void ApplyLocalization()
    {
        _suppressLanguageChange = true;
        try
        {
            LanguageCombo.SelectedIndex = Lang.Current == Lang.EnUs ? 1 : 0;
        }
        finally
        {
            _suppressLanguageChange = false;
        }

        _suppressThemeChange = true;
        try
        {
            ThemeCombo.SelectedIndex = AppState.Current.Theme switch
            {
                ThemeService.Light => 1,
                ThemeService.Dark => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressThemeChange = false;
        }

        RootPathText.Text = AppState.Current.Project?.RootPath ?? Lang.T("root.none");
        ConfigDirText.Text = AppPaths.AppDataDir;

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = Lang.T("settings.version", version is null ? "1.0" : version.ToString(3));
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange ||
            ThemeCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string theme)
        {
            return;
        }

        AppState.Current.SetTheme(theme);
        ShowMessage(InfoBarSeverity.Success, Lang.T("settings.themeChanged"));
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange ||
            LanguageCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag)
        {
            return;
        }

        AppState.Current.SetLanguage(tag);
        ApplyLocalization();
        ShowMessage(InfoBarSeverity.Success, Lang.T("settings.languageChanged"));
    }

    private async void OnChangeRootClick(object sender, RoutedEventArgs e)
    {
        var path = await WindowHelper.PickFolderAsync();
        if (path is null)
        {
            return;
        }

        if (!AppState.Current.TrySetRoot(path, out var errorKey))
        {
            ShowMessage(
                InfoBarSeverity.Error,
                string.IsNullOrEmpty(errorKey) ? Lang.T("root.invalid") : Lang.T(errorKey));
            return;
        }

        ApplyLocalization();
        ShowMessage(InfoBarSeverity.Success, Lang.T("settings.rootChanged"));
    }

    private void OnOpenConfigDirClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
        }
        catch (Exception ex)
        {
            AppLog.Write("创建用户配置目录失败", ex);
        }

        ShellHelper.OpenFolder(AppPaths.AppDataDir);
    }

    private void OnOpenRootClick(object sender, RoutedEventArgs e)
    {
        var project = AppState.Current.Project;
        if (project is not null)
        {
            ShellHelper.OpenFolder(project.RootPath);
        }
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
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
