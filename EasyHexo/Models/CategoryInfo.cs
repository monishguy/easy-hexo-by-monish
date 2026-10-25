namespace EasyHexo.Models;

/// <summary>source\_posts 下的一个分类（一个子文件夹）。</summary>
public sealed class CategoryInfo
{
    /// <summary>分类名（文件夹名）；空字符串表示“未分类”（文章直接放在 _posts 下）。</summary>
    public string Name { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>该分类下的文章数（递归统计 .md）。</summary>
    public int ArticleCount { get; set; }

    public bool IsEmpty => ArticleCount == 0;

    public string CountText => ArticleCount.ToString();

    public override string ToString() => Name;
}
