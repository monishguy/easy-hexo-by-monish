using System.Text.Json;

namespace EasyHexo.Services;

/// <summary>
/// 用户数据目录（设置、日志），固定在 %APPDATA%\EasyHexo，不在软件目录里。
/// 这样替换 / 升级软件目录后，上次的 Hexo 站点仍会自动恢复。
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "EasyHexo";

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    public static string LogDir => Path.Combine(AppDataDir, "logs");

    public static string LogFile => Path.Combine(LogDir, "easyhexo.log");

    /// <summary>旧版本使用的 %LOCALAPPDATA%\EasyHexo（仅用于迁移）。</summary>
    private static string LegacyLocalDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static void EnsureAppDataDir() => Directory.CreateDirectory(AppDataDir);

    public static void EnsureLogDir() => Directory.CreateDirectory(LogDir);

    /// <summary>
    /// 首次使用新版本时，把旧位置（%LOCALAPPDATA% 或软件目录）里的 settings.json
    /// 迁移到 %APPDATA%\EasyHexo，避免升级后丢失上次选择的 Hexo 目录。
    /// </summary>
    public static void MigrateLegacySettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                return;
            }

            EnsureAppDataDir();

            var candidates = new[]
            {
                Path.Combine(LegacyLocalDir, "settings.json"),
                Path.Combine(AppContext.BaseDirectory, "settings.json"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, SettingsFile, overwrite: false);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("迁移旧设置失败", ex);
        }
    }
}

/// <summary>极简文件日志：出现未处理异常或读写失败时写一行，便于排查。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureLogDir();
                File.AppendAllText(
                    AppPaths.LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不应影响主流程
        }
    }

    public static void Write(string message, Exception exception) =>
        Write($"{message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
}

/// <summary>用户设置（保存在 %APPDATA%\EasyHexo\settings.json，不在软件目录）。</summary>
public sealed class AppSettings
{
    /// <summary>上次选择的 Hexo 根目录。</summary>
    public string? HexoRoot { get; set; }

    /// <summary>界面语言："zh-CN" 或 "en-US"。</summary>
    public string Language { get; set; } = Localization.Lang.ZhCn;

    /// <summary>外观主题："system" / "light" / "dark"。</summary>
    public string Theme { get; set; } = ThemeService.System;

    /// <summary>发布文章时“图片”资源组默认是否勾选（记住设置）。</summary>
    public bool AssetImagesEnabled { get; set; } = true;

    /// <summary>发布文章时“文件”资源组默认是否勾选（记住设置）。</summary>
    public bool AssetFilesEnabled { get; set; } = true;

    /// <summary>手动创建过的分类名（用于保留空分类）。</summary>
    public List<string> Categories { get; set; } = new();
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        try
        {
            var path = AppPaths.SettingsFile;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Theme = ThemeService.Normalize(settings.Theme);
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Write("读取设置失败", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureAppDataDir();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex)
        {
            AppLog.Write("保存设置失败", ex);
        }
    }
}
