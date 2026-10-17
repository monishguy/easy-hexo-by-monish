namespace EasyHexo.Services;

/// <summary>一个已经通过校验的 Hexo 站点根目录。</summary>
public sealed class HexoProject
{
    public HexoProject(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>Hexo 根目录（绝对路径）。</summary>
    public string RootPath { get; }

    /// <summary>_config.yml</summary>
    public string ConfigPath => Path.Combine(RootPath, "_config.yml");

    /// <summary>source 目录</summary>
    public string SourcePath => Path.Combine(RootPath, "source");

    /// <summary>source\_posts</summary>
    public string PostsPath => Path.Combine(SourcePath, "_posts");

    /// <summary>
    /// 校验目录是否是 Hexo 根目录。要求：目录存在、包含 _config.yml、包含 source 文件夹。
    /// 失败时 <paramref name="errorKey"/> 返回可直接本地化的错误 key。
    /// </summary>
    public static bool TryCreate(string? rootPath, out HexoProject? project, out string? errorKey)
    {
        project = null;
        errorKey = null;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            errorKey = "root.error.notFound";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rootPath);
        }
        catch
        {
            errorKey = "root.error.notFound";
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            errorKey = "root.error.notFound";
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, "_config.yml")))
        {
            errorKey = "root.error.config";
            return false;
        }

        if (!Directory.Exists(Path.Combine(fullPath, "source")))
        {
            errorKey = "root.error.source";
            return false;
        }

        project = new HexoProject(fullPath);
        return true;
    }
}

/// <summary>小工具：路径比较 / 文件名与 URL 清理。</summary>
public static class PathHelper
{
    public static bool AreSame(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsUnder(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把标题变成安全的文件名 / 文件夹名（保留中文，只替换 Windows 非法字符）。</summary>
    public static string SanitizeName(string? name, string fallback = "post")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
