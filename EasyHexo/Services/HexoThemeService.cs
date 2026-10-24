namespace EasyHexo.Services;

/// <summary>/themes/ 下的一个主题。</summary>
public sealed class HexoTheme
{
    public string Name { get; init; } = string.Empty;

    /// <summary>主题文件夹绝对路径。</summary>
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>主题配置文件（_config.yml / _config.yaml），不存在时为 null。</summary>
    public string? ConfigPath { get; init; }

    public bool HasConfig => !string.IsNullOrEmpty(ConfigPath);

    /// <summary>相对站点根目录的配置文件路径（用于界面提示）。</summary>
    public string RelativeConfigPath { get; init; } = string.Empty;
}

/// <summary>识别 Hexo 站点根目录下 themes/ 里的主题，并读取当前使用的主题。</summary>
public static class HexoThemeService
{
    private static readonly string[] ConfigFileNames = { "_config.yml", "_config.yaml" };

    public static List<HexoTheme> Detect(HexoProject project)
    {
        var result = new List<HexoTheme>();
        var themesDirectory = Path.Combine(project.RootPath, "themes");
        if (!Directory.Exists(themesDirectory))
        {
            return result;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(themesDirectory))
            {
                string? configPath = null;
                foreach (var fileName in ConfigFileNames)
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate))
                    {
                        configPath = candidate;
                        break;
                    }
                }

                var name = Path.GetFileName(directory);
                result.Add(new HexoTheme
                {
                    Name = name,
                    DirectoryPath = directory,
                    ConfigPath = configPath,
                    RelativeConfigPath = configPath is null
                        ? string.Empty
                        : Path.GetRelativePath(project.RootPath, configPath),
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("扫描主题目录失败", ex);
        }

        return result.OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>读取 _config.yml 里的 theme 值（当前使用的主题名）。</summary>
    public static string? ReadCurrentThemeName(YamlConfigDocument document)
    {
        var entry = document.Roots.FirstOrDefault(root =>
            root.Key.Equals("theme", StringComparison.Ordinal) && root.IsEditable);
        return entry?.EditingValue.Trim();
    }
}
