using System.Text.Json;

namespace EasyHexo.Services;

/// <summary>应用本机的可写目录（设置、日志）。</summary>
public static class AppPaths
{
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyHexo");

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    public static string LogDir => Path.Combine(AppDataDir, "logs");

    public static string LogFile => Path.Combine(LogDir, "easyhexo.log");

    public static void EnsureAppDataDir() => Directory.CreateDirectory(AppDataDir);

    public static void EnsureLogDir() => Directory.CreateDirectory(LogDir);
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

/// <summary>用户设置（保存在 %LOCALAPPDATA%\EasyHexo\settings.json）。</summary>
public sealed class AppSettings
{
    /// <summary>上次选择的 Hexo 根目录。</summary>
    public string? HexoRoot { get; set; }

    /// <summary>界面语言："zh-CN" 或 "en-US"。</summary>
    public string Language { get; set; } = Localization.Lang.ZhCn;
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
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
