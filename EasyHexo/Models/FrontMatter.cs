namespace EasyHexo.Models;

/// <summary>
/// Markdown 文件开头的 front-matter（两个 --- 之间的内容）。
/// 除 title / date / tags 以外的行会原样保留在 <see cref="ExtraLines"/> 中，发布时写回。
/// </summary>
public sealed class FrontMatter
{
    public bool HasFrontMatter { get; set; }

    public string? Title { get; set; }

    public DateTime? Date { get; set; }

    public List<string> Tags { get; } = new();

    /// <summary>其它 front-matter 行（categories、updated、自定义字段等），原样保留。</summary>
    public List<string> ExtraLines { get; } = new();

    /// <summary>正文（不含 front-matter）。</summary>
    public string Body { get; set; } = string.Empty;

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasDate => Date.HasValue;
}
