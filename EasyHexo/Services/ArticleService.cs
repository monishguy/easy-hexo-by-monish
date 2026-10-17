using EasyHexo.Models;

namespace EasyHexo.Services;

/// <summary>读取 source\_posts 下的已发布文章。</summary>
public static class ArticleService
{
    public static List<ArticleInfo> LoadPosts(HexoProject project)
    {
        var posts = new List<ArticleInfo>();
        if (!Directory.Exists(project.PostsPath))
        {
            return posts;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var file in Directory.EnumerateFiles(project.PostsPath, "*.md", options))
        {
            try
            {
                var modified = File.GetLastWriteTime(file);
                var info = new ArticleInfo
                {
                    FullPath = file,
                    RelativePath = Path.GetRelativePath(project.PostsPath, file),
                    Modified = modified,
                    Date = modified,
                };

                var frontMatter = FrontMatterService.Parse(TextFileService.ReadAllText(file));
                info.Title = string.IsNullOrWhiteSpace(frontMatter.Title)
                    ? Path.GetFileNameWithoutExtension(file)
                    : frontMatter.Title.Trim();
                if (frontMatter.Date.HasValue)
                {
                    info.Date = frontMatter.Date.Value;
                }

                info.Tags.AddRange(frontMatter.Tags);
                posts.Add(info);
            }
            catch (Exception ex)
            {
                AppLog.Write($"读取文章失败：{file}", ex);
            }
        }

        // 需求：默认以修改日期（新到旧）排序
        return posts
            .OrderByDescending(post => post.Modified)
            .ThenBy(post => post.Title, StringComparer.CurrentCulture)
            .ToList();
    }
}
