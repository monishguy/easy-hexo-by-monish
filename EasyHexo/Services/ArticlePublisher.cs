using EasyHexo.Models;

namespace EasyHexo.Services;

/// <summary>发布一篇文章所需的全部输入。</summary>
public sealed class PublishRequest
{
    /// <summary>当前编辑的文件（.md，可能位于临时目录）。</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>文件是否来自临时目录（拖入的 .txt）。</summary>
    public bool IsTemporary { get; init; }

    /// <summary>当前文件是否已经在 source\_posts 里（已发布文章）。</summary>
    public bool IsPublished { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTime Date { get; init; }

    public List<string> Tags { get; init; } = new();

    /// <summary>发布到的分类（_posts 下的一级文件夹名）；空字符串表示未分类。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>正文（不含 front-matter）。</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>原文件的 front-matter，用于保留 title/date/tags 之外的字段。</summary>
    public FrontMatter? OriginalFrontMatter { get; init; }

    /// <summary>资源处理选择（URL → 是否包含 / 手动指定的本机文件）；null 表示全部包含。</summary>
    public IReadOnlyDictionary<string, AssetSelection>? AssetSelections { get; init; }
}

public sealed class PublishResult
{
    public string TargetPath { get; set; } = string.Empty;

    public int AssetsCopied { get; set; }

    public string AssetFolder { get; set; } = string.Empty;

    /// <summary>是否把 front-matter 也写回了用户选择的原始文件。</summary>
    public bool SourceUpdated { get; set; }

    public List<string> Warnings { get; } = new();
}

/// <summary>
/// 发布流程（纯文件操作，不依赖 UI）：
///
///   1. 处理正文里的本机绝对路径图片 / 附件 → 复制到 source\标题\ 并改写链接；
///   2. 生成 front-matter（title / date / tags + 原文件里的其它字段）；
///   3. 写入 source\_posts\标题.md（已发布文章则写回原路径）；
///   4. 若来源是用户选择的外部 .md 文件，也把 front-matter 写回该文件开头。
/// </summary>
public static class ArticlePublisher
{
    public static string ResolveTargetPath(HexoProject project, PublishRequest request)
    {
        if (request.IsPublished && File.Exists(request.SourcePath))
        {
            return request.SourcePath;
        }

        var fileName = PathHelper.SanitizeName(request.Title) + ".md";
        return CategoryService.GetTargetPath(project, request.Category, fileName, isFile: true);
    }

    public static PublishResult Publish(HexoProject project, PublishRequest request)
    {
        var result = new PublishResult();
        Directory.CreateDirectory(project.PostsPath);

        var articleDirectory = string.IsNullOrEmpty(request.SourcePath) ? null : Path.GetDirectoryName(request.SourcePath);
        var assets = AssetService.Process(project.RootPath, request.Title, request.Body, request.AssetSelections, articleDirectory);
        result.AssetsCopied = assets.Copied.Count;
        result.AssetFolder = assets.AssetFolder;
        result.Warnings.AddRange(assets.Warnings);

        var content = FrontMatterService.Compose(
            request.Title,
            request.Date,
            request.Tags,
            assets.Content,
            request.OriginalFrontMatter);

        var targetPath = ResolveTargetPath(project, request);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        TextFileService.WriteAllText(targetPath, content);
        result.TargetPath = targetPath;

        // 需求：外部 .md 文件的 front-matter 要写到文件开头
        if (!request.IsTemporary &&
            !PathHelper.AreSame(targetPath, request.SourcePath) &&
            File.Exists(request.SourcePath))
        {
            try
            {
                TextFileService.WriteAllText(request.SourcePath, content);
                result.SourceUpdated = true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{request.SourcePath}: {ex.Message}");
                AppLog.Write("写回源文件失败", ex);
            }
        }

        return result;
    }
}
