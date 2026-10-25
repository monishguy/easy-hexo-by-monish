using EasyHexo.Models;

namespace EasyHexo.Services;

/// <summary>
/// 分类管理：source\_posts 下的每个一级子文件夹就是一个分类，
/// 直接在 _posts 下的文章属于“未分类”。
///
/// 同时负责分类的创建 / 删除，以及“调整分类”时把文章和它的资源目录一起剪切转移：
///   _posts\标题.md          → _posts\分类1\标题.md
///   _posts\标题\img.png     → _posts\分类1\标题\img.png
/// </summary>
public static class CategoryService
{
    /// <summary>扫描所有分类（含手动创建的空分类）。</summary>
    public static List<CategoryInfo> Scan(HexoProject project)
    {
        var result = new List<CategoryInfo>();
        if (!Directory.Exists(project.PostsPath))
        {
            return result;
        }

        var remembered = AppState.Current.Settings.Categories;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.GetDirectories(project.PostsPath))
        {
            var name = Path.GetFileName(directory);
            var count = CountArticles(directory);
            if (count == 0 && !remembered.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                // 没有文章、也不是手动创建的分类 → 多半是文章的资源目录，不当成分类
                continue;
            }

            names.Add(name);
            result.Add(new CategoryInfo
            {
                Name = name,
                DirectoryPath = directory,
                ArticleCount = count,
            });
        }

        // 记录里存在但文件夹已被删掉的分类：清理记录
        var stale = remembered.Where(name => !names.Contains(name)).ToList();
        if (stale.Count > 0)
        {
            foreach (var name in stale)
            {
                remembered.Remove(name);
            }

            AppState.Current.SaveSettings();
        }

        return result
            .OrderBy(category => category.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>文章所属分类；直接放在 _posts 下返回空字符串（未分类）。</summary>
    public static string GetCategoryOf(HexoProject project, string articlePath)
    {
        if (!PathHelper.IsUnder(articlePath, project.PostsPath))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(project.PostsPath, articlePath);
        var directory = Path.GetDirectoryName(relative);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        var first = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first;
    }

    /// <summary>文章在某个分类下应有的目标路径。</summary>
    public static string GetTargetPath(HexoProject project, string category, string title) =>
        GetTargetPath(project, category, PathHelper.SanitizeName(title) + ".md");

    public static string GetTargetPath(HexoProject project, string category, string fileName, bool isFile = false)
    {
        var directory = string.IsNullOrWhiteSpace(category)
            ? project.PostsPath
            : Path.Combine(project.PostsPath, category);
        return Path.Combine(directory, fileName);
    }

    /// <summary>创建分类（文件夹）；同名、非法名称会失败。</summary>
    public static bool Create(HexoProject project, string? rawName, out string? errorKey)
    {
        errorKey = null;
        var name = (rawName ?? string.Empty).Trim();
        if (!IsValidName(name))
        {
            errorKey = "category.invalid";
            return false;
        }

        var directory = Path.Combine(project.PostsPath, name);
        if (Directory.Exists(directory) || File.Exists(directory))
        {
            errorKey = "category.exists";
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var categories = AppState.Current.Settings.Categories;
            if (!categories.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                categories.Add(name);
                AppState.Current.SaveSettings();
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"创建分类失败：{name}", ex);
            errorKey = "category.createFailed";
            return false;
        }
    }

    /// <summary>删除空分类（下面没有任何文章，也没有残留文件）。</summary>
    public static bool Delete(HexoProject project, string? name, out string? errorKey)
    {
        errorKey = null;
        var category = (name ?? string.Empty).Trim();
        if (category.Length == 0)
        {
            errorKey = "category.notFound";
            return false;
        }

        var directory = Path.Combine(project.PostsPath, category);
        if (!Directory.Exists(directory))
        {
            errorKey = "category.notFound";
            return false;
        }

        if (CountArticles(directory) > 0)
        {
            errorKey = "category.notEmpty";
            return false;
        }

        if (Directory.EnumerateFileSystemEntries(directory).Any())
        {
            errorKey = "category.hasFiles";
            return false;
        }

        try
        {
            Directory.Delete(directory);
            var categories = AppState.Current.Settings.Categories;
            categories.RemoveAll(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase));
            AppState.Current.SaveSettings();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"删除分类失败：{category}", ex);
            errorKey = "category.deleteFailed";
            return false;
        }
    }

    /// <summary>把文章（及其同名资源目录）移动到目标分类；<paramref name="category"/> 为空表示未分类。</summary>
    public static bool MoveArticle(
        HexoProject project,
        string articlePath,
        string? category,
        out string? newPath,
        out string? errorKey)
    {
        newPath = null;
        errorKey = null;

        if (!File.Exists(articlePath))
        {
            errorKey = "category.moveFailed";
            return false;
        }

        var targetCategory = (category ?? string.Empty).Trim();
        var fileName = Path.GetFileName(articlePath);
        var targetPath = GetTargetPath(project, targetCategory, fileName, isFile: true);

        if (PathHelper.AreSame(articlePath, targetPath))
        {
            newPath = articlePath;
            return true;
        }

        try
        {
            var targetDirectory = Path.GetDirectoryName(targetPath)!;
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (File.Exists(targetPath))
            {
                errorKey = "category.conflict";
                return false;
            }

            var sourceDirectory = Path.GetDirectoryName(articlePath)!;
            var baseName = Path.GetFileNameWithoutExtension(articlePath);
            var sourceAssetDir = Path.Combine(sourceDirectory, baseName);
            var targetAssetDir = Path.Combine(targetDirectory, baseName);

            if (Directory.Exists(sourceAssetDir) && !PathHelper.AreSame(sourceAssetDir, targetAssetDir))
            {
                if (Directory.Exists(targetAssetDir))
                {
                    errorKey = "category.conflict";
                    return false;
                }
            }

            File.Move(articlePath, targetPath);
            try
            {
                if (Directory.Exists(sourceAssetDir) && !PathHelper.AreSame(sourceAssetDir, targetAssetDir))
                {
                    Directory.Move(sourceAssetDir, targetAssetDir);
                }
            }
            catch
            {
                // 资源目录转移失败时回滚文章，避免留下半移动状态
                try
                {
                    File.Move(targetPath, articlePath);
                }
                catch (Exception rollbackError)
                {
                    AppLog.Write("回滚文章移动失败", rollbackError);
                }

                throw;
            }

            newPath = targetPath;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"移动文章分类失败：{articlePath}", ex);
            errorKey = "category.moveFailed";
            return false;
        }
    }

    /// <summary>把文件移动到一个新位置，若目标目录不存在则创建；同名文件存在时返回 false。</summary>
    public static bool MoveFile(string sourcePath, string targetPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(targetPath))
            {
                return false;
            }

            File.Move(sourcePath, targetPath);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"移动文件失败：{sourcePath} → {targetPath}", ex);
            return false;
        }
    }

    private static int CountArticles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).Count();
        }
        catch (Exception ex)
        {
            AppLog.Write($"统计分类文章失败：{directory}", ex);
            return 0;
        }
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return false;
        }

        if (name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
        {
            return false;
        }

        return name.Trim().Length > 0;
    }
}
