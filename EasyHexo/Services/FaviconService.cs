namespace EasyHexo.Services;

/// <summary>当前配置里的站点图标（favicon）信息。</summary>
public sealed class FaviconInfo
{
    /// <summary>配置中的 favicon 项；没有时为 null。</summary>
    public ConfigEntry? Entry { get; init; }

    /// <summary>配置里的原始值（可能是 /favicon.png 或 https://...）。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>解析出来的本机文件路径（无法解析时为 null）。</summary>
    public string? LocalPath { get; init; }

    /// <summary>本机文件是否是支持的图片类型（可用于预览）。</summary>
    public bool IsImage { get; init; }

    public bool HasEntry => Entry is not null;
}

/// <summary>
/// 站点图标的识别与更换：
/// * 从当前配置里找 favicon 项并解析成 source 下的本机文件；
/// * 更换时把图片复制到 Hexo 的 source 目录（如果不在里面），
///   再把配置值改成站点相对路径（如 /favicon.png）。
/// </summary>
public static class FaviconService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".ico", ".gif", ".webp", ".bmp", ".svg",
    };

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant(), StringComparer.Ordinal);

    public static FaviconInfo Find(YamlConfigDocument document, HexoProject project)
    {
        var entry = FindFaviconEntry(document.Roots);
        var value = entry?.EditingValue.Trim() ?? string.Empty;
        var localPath = ResolveLocalPath(project, value);
        return new FaviconInfo
        {
            Entry = entry,
            Value = value,
            LocalPath = localPath,
            IsImage = localPath is not null && IsImageFile(localPath),
        };
    }

    private static ConfigEntry? FindFaviconEntry(IEnumerable<ConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsEditable && entry.Key.Equals("favicon", StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            var nested = FindFaviconEntry(entry.Children);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>把配置值解析成本机文件路径。</summary>
    public static string? ResolveLocalPath(HexoProject project, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (IsLocalAbsolute(text))
        {
            return File.Exists(text) ? text : null;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            return null; // 网页图标
        }

        var relative = text.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

        var inSource = Path.Combine(project.SourcePath, relative);
        if (File.Exists(inSource))
        {
            return inSource;
        }

        var inRoot = Path.Combine(project.RootPath, relative);
        return File.Exists(inRoot) ? inRoot : null;
    }

    /// <summary>
    /// 确保图片位于 source 目录内，并返回站点相对路径（如 /favicon.png）。
    /// 图片不在 Hexo 根目录 / source 内时复制进去。
    /// </summary>
    public static string EnsureSiteCopy(HexoProject project, string sourcePath, out bool copied, out string? destinationPath)
    {
        copied = false;
        destinationPath = null;

        var fullPath = Path.GetFullPath(sourcePath);
        if (PathHelper.IsUnder(fullPath, project.SourcePath))
        {
            var relative = Path.GetRelativePath(project.SourcePath, fullPath).Replace('\\', '/');
            return "/" + relative;
        }

        Directory.CreateDirectory(project.SourcePath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "favicon.png";
        }

        var destination = Path.Combine(project.SourcePath, fileName);
        if (!PathHelper.AreSame(fullPath, destination))
        {
            File.Copy(fullPath, destination, true);
            copied = true;
            destinationPath = destination;
        }

        return "/" + fileName;
    }

    private static bool IsLocalAbsolute(string path)
    {
        if (path.Length < 3)
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }
}
