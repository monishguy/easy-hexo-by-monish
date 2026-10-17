namespace EasyHexo.Models;

/// <summary>source\_posts 下的一个已发布文章。</summary>
public sealed class ArticleInfo
{
    public string FullPath { get; set; } = string.Empty;

    /// <summary>相对 source\_posts 的路径，用于在列表中区分同名文章。</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    /// <summary>文件最后修改时间（列表默认按它倒序）。</summary>
    public DateTime Modified { get; set; }

    public List<string> Tags { get; } = new();

    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

    public string TagsText => string.Join(", ", Tags);

    public override string ToString() => Title;
}
