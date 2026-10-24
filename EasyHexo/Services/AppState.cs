using EasyHexo.Localization;

namespace EasyHexo.Services;

/// <summary>
/// 应用级状态（单例）：当前设置 + 当前 Hexo 根目录 + 外观主题。
/// 页面通过 <see cref="Current"/> 拿根目录，通过 <see cref="ProjectChanged"/> 感知切换。
/// </summary>
public sealed class AppState
{
    private AppState()
    {
    }

    public static AppState Current { get; } = new();

    public AppSettings Settings { get; private set; } = new();

    public HexoProject? Project { get; private set; }

    public string? RootPath => Project?.RootPath;

    /// <summary>当前外观主题："system" / "light" / "dark"。</summary>
    public string Theme => ThemeService.Normalize(Settings.Theme);

    /// <summary>根目录切换（或失效）时触发。</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>外观主题切换时触发（MainWindow 负责把主题应用到界面）。</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>启动时调用一次：读取设置、套用语言、恢复上次的根目录。</summary>
    public void Initialize()
    {
        // 设置固定在 %APPDATA%\EasyHexo（不在软件目录），升级 / 重启后仍能恢复上次站点
        AppPaths.MigrateLegacySettings();
        Settings = SettingsService.Load();
        Lang.SetLanguage(Settings.Language);

        if (!File.Exists(AppPaths.SettingsFile))
        {
            SaveSettings();
        }

        if (!string.IsNullOrWhiteSpace(Settings.HexoRoot) &&
            HexoProject.TryCreate(Settings.HexoRoot, out var project, out _))
        {
            Project = project;
        }
    }

    /// <summary>设置根目录；校验失败时返回 false 并给出本地化错误 key。</summary>
    public bool TrySetRoot(string? path, out string? errorKey)
    {
        if (!HexoProject.TryCreate(path, out var project, out errorKey))
        {
            return false;
        }

        Project = project;
        Settings.HexoRoot = project!.RootPath;
        SaveSettings();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetLanguage(string? language)
    {
        Settings.Language = Lang.Normalize(language);
        Lang.SetLanguage(Settings.Language);
        SaveSettings();
    }

    /// <summary>保存外观主题并通知界面刷新。</summary>
    public void SetTheme(string? theme)
    {
        Settings.Theme = ThemeService.Normalize(theme);
        SaveSettings();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>记住“图片 / 文件”资源组的上次勾选状态。</summary>
    public void SetAssetGroupRemembered(bool isImage, bool checkedState)
    {
        if (isImage)
        {
            Settings.AssetImagesEnabled = checkedState;
        }
        else
        {
            Settings.AssetFilesEnabled = checkedState;
        }

        SaveSettings();
    }

    public void SaveSettings() => SettingsService.Save(Settings);
}
