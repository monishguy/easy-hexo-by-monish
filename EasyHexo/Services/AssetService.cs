using System.Text;
using System.Text.RegularExpressions;

namespace EasyHexo.Services;

/// <summary>资源链接的分类。</summary>
public enum AssetLinkKind
{
    /// <summary>图片（![alt](...) 或图片扩展名）。</summary>
    Image,

    /// <summary>附件（[text](...)，非图片扩展名）。</summary>
    File,

    /// <summary>本机路径但文件不存在：需要用户“浏览”指定。</summary>
    Unknown,
}

/// <summary>正文里扫描到的一个本机资源链接。</summary>
public sealed class AssetLink
{
    public string Url { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public bool IsImageLink { get; init; }

    public AssetLinkKind Kind { get; init; }

    /// <summary>解析到的本机文件（不存在时为 null）。</summary>
    public string? LocalPath { get; init; }

    public string DisplayName =>
        !string.IsNullOrEmpty(LocalPath)
            ? Path.GetFileName(LocalPath)
            : Path.GetFileName(Url) is { Length: > 0 } name
                ? name
                : Url;
}

/// <summary>发布时对某个资源链接的处理选择。</summary>
public sealed class AssetSelection
{
    public bool Include { get; init; }

    /// <summary>用户为未识别项手动指定的本机文件。</summary>
    public string? SourcePathOverride { get; init; }
}

public sealed class AssetCopyRecord
{
    public string SourcePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string DestinationPath { get; init; } = string.Empty;
}

public sealed class AssetProcessResult
{
    /// <summary>改写链接后的正文。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>资源目录（source\标题）；没有复制任何文件时为空。</summary>
    public string AssetFolder { get; set; } = string.Empty;

    public List<AssetCopyRecord> Copied { get; } = new();

    public List<string> Warnings { get; } = new();
}

/// <summary>
/// 处理文章正文里的“本机绝对路径”图片 / 附件。
///
/// * <see cref="Scan"/>：发布页使用，列出所有候选项并按图片 / 文件 / 未识别分类；
/// * <see cref="Process(string,string,string?,IReadOnlyDictionary{string,AssetSelection}?)"/>：
///   按用户在界面上的勾选复制文件并改写链接，未勾选的保持原样。
///
/// 例：![image.png](E:\images\image.png) → ![image.png](/标题示例/image.png)
/// 网页 URL、相对路径、站点内路径一律跳过。
/// </summary>
public static partial class AssetService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".svg", ".avif",
    };

    /// <summary>扫描正文中的本机绝对路径资源（按出现顺序，URL 去重）。</summary>
    public static List<AssetLink> Scan(string? body)
    {
        var result = new List<AssetLink>();
        if (string.IsNullOrEmpty(body))
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkRegex().Matches(body))
        {
            var lead = match.Groups["lead"].Value;
            var rawUrl = match.Groups["url"].Value.Trim();
            if (rawUrl.Length == 0)
            {
                continue;
            }

            var url = CleanUrl(rawUrl);
            if (!IsLocalAbsolutePath(url) || !seen.Add(url))
            {
                continue;
            }

            var localPath = ResolveExistingFile(url);
            var isImageLink = lead.StartsWith('!');
            var extension = Path.GetExtension(localPath ?? url).ToLowerInvariant();
            var isImage = isImageLink || ImageExtensions.Contains(extension, StringComparer.Ordinal);

            result.Add(new AssetLink
            {
                Url = url,
                Label = ExtractLabel(lead),
                IsImageLink = isImageLink,
                Kind = localPath is null
                    ? AssetLinkKind.Unknown
                    : isImage ? AssetLinkKind.Image : AssetLinkKind.File,
                LocalPath = localPath,
            });
        }

        return result;
    }

    /// <summary>兼容旧调用：不传选择时处理所有能解析到的本机资源。</summary>
    public static AssetProcessResult Process(string hexoRoot, string title, string? body) =>
        Process(hexoRoot, title, body, null);

    /// <summary>
    /// 按 selections（URL → 是否包含 / 替换路径）复制资源并改写链接。
    /// selections 为 null 时表示“全部包含”。
    /// </summary>
    public static AssetProcessResult Process(
        string hexoRoot,
        string title,
        string? body,
        IReadOnlyDictionary<string, AssetSelection>? selections)
    {
        var result = new AssetProcessResult { Content = body ?? string.Empty };
        if (string.IsNullOrEmpty(result.Content))
        {
            return result;
        }

        var folderName = PathHelper.SanitizeName(title);
        var assetFolder = Path.Combine(hexoRoot, "source", folderName);
        var bySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        result.Content = MarkdownLinkRegex().Replace(result.Content, match =>
        {
            var lead = match.Groups["lead"].Value;
            var rawUrl = match.Groups["url"].Value.Trim();
            if (rawUrl.Length == 0)
            {
                return match.Value;
            }

            var url = CleanUrl(rawUrl);
            if (!IsLocalAbsolutePath(url))
            {
                return match.Value; // 网页链接 / 相对路径：跳过
            }

            AssetSelection? selection = null;
            if (selections is not null)
            {
                if (!selections.TryGetValue(url, out selection) || !selection.Include)
                {
                    return match.Value; // 用户没有勾选：保持原样
                }
            }

            var sourcePath = selection?.SourcePathOverride;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                sourcePath = ResolveExistingFile(url);
            }

            if (sourcePath is null)
            {
                result.Warnings.Add(Localization.Lang.T("publish.warnMissingAsset", url));
                return match.Value;
            }

            if (!bySource.TryGetValue(sourcePath, out var fileName))
            {
                fileName = MakeUniqueFileName(Path.GetFileName(sourcePath), usedNames);
                usedNames.Add(fileName);
                bySource[sourcePath] = fileName;

                try
                {
                    Directory.CreateDirectory(assetFolder);
                    var destination = Path.Combine(assetFolder, fileName);
                    if (!PathHelper.AreSame(sourcePath, destination))
                    {
                        File.Copy(sourcePath, destination, true);
                    }

                    result.AssetFolder = assetFolder;
                    result.Copied.Add(new AssetCopyRecord
                    {
                        SourcePath = sourcePath,
                        FileName = fileName,
                        DestinationPath = destination,
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Write($"复制资源失败：{sourcePath}", ex);
                    result.Warnings.Add(Localization.Lang.T("publish.warnCopyFailed", sourcePath, ex.Message));
                    return match.Value;
                }
            }

            var newUrl = EncodeUrl("/" + folderName + "/" + fileName);
            return lead + newUrl + ")";
        });

        return result;
    }

    // ── 解析辅助 ─────────────────────────────────────────────────────

    private static string? ResolveExistingFile(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        var unescaped = SafeUnescape(path);
        return unescaped is not null && File.Exists(unescaped) ? unescaped : null;
    }

    private static string ExtractLabel(string lead)
    {
        var start = lead.IndexOf('[');
        var end = lead.IndexOf(']');
        return start >= 0 && end > start ? lead[(start + 1)..end] : string.Empty;
    }

    /// <summary>去掉 &lt; &gt; 包裹与 markdown 的 "title" 部分。</summary>
    private static string CleanUrl(string url)
    {
        url = url.Trim();
        if (url.Length >= 2 && url[0] == '<' && url[^1] == '>')
        {
            url = url[1..^1].Trim();
        }

        var titleStart = url.IndexOf(" \"", StringComparison.Ordinal);
        if (titleStart < 0)
        {
            titleStart = url.IndexOf(" '", StringComparison.Ordinal);
        }

        if (titleStart > 0)
        {
            url = url[..titleStart].Trim();
        }

        return url;
    }

    private static bool IsLocalAbsolutePath(string path)
    {
        if (path.Length < 3)
        {
            return false;
        }

        if (path.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        // UNC：\\server\share
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return path.Length >= 3 &&
               char.IsAsciiLetter(path[0]) &&
               path[1] == ':' &&
               (path[2] == '\\' || path[2] == '/');
    }

    private static string? SafeUnescape(string url)
    {
        try
        {
            return url.Contains('%') ? Uri.UnescapeDataString(url) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string MakeUniqueFileName(string fileName, HashSet<string> usedNames)
    {
        if (fileName.Length == 0)
        {
            fileName = "asset";
        }

        var candidate = fileName;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 2;
        while (usedNames.Contains(candidate))
        {
            candidate = $"{name}-{index++}{extension}";
        }

        return candidate;
    }

    /// <summary>把站点内 URL 里会破坏 Markdown 的字符转义，其余（含中文）保持原样。</summary>
    private static string EncodeUrl(string url)
    {
        var builder = new StringBuilder(url.Length);
        foreach (var ch in url)
        {
            switch (ch)
            {
                case ' ':
                    builder.Append("%20");
                    break;
                case '(':
                    builder.Append("%28");
                    break;
                case ')':
                    builder.Append("%29");
                    break;
                case '#':
                    builder.Append("%23");
                    break;
                case '?':
                    builder.Append("%3F");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>匹配 ![alt](url) / [text](url)。</summary>
    [GeneratedRegex(@"(?<lead>!?\[[^\]\r\n]*\]\()(?<url>[^)\r\n]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();
}
