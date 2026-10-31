namespace EasyHexo.Models;

/// <summary>标签及其被文章引用的次数（用于“全部标签”和快速导入）。</summary>
public sealed class TagStat
{
    /// <summary>标签名；“*” 表示“全部标签”。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>被文章引用的次数。</summary>
    public int Count { get; set; }

    public override string ToString() => Display;

    public string Display => Name == "*"
        ? Localization.Lang.T("publish.tagAll")
        : Count > 0 ? $"{Name} ({Count})" : Name;
}
