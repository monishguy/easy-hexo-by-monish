using System.ComponentModel;
using EasyHexo.Localization;
using EasyHexo.Services;

namespace EasyHexo.Models;

/// <summary>文章编辑表单的一份草稿状态（标题 / 日期 / 标签）。</summary>
public sealed class DraftState
{
    public string Title { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Now;

    public List<string> Tags { get; } = new();

    /// <summary>是否与文章原始 front-matter 不同（界面显示“草稿”标记）。</summary>
    public bool IsModified { get; set; }

    public DraftState Clone()
    {
        var clone = new DraftState
        {
            Title = Title,
            Date = Date,
            IsModified = IsModified,
        };
        clone.Tags.AddRange(Tags);
        return clone;
    }
}

/// <summary>拖入 / 浏览导入、等待发布的文章（左侧“待发布”列表的一项）。</summary>
public sealed class ImportDraft : INotifyPropertyChanged
{
    public string SourcePath { get; set; } = string.Empty;

    public bool IsTemporary { get; set; }

    public FrontMatter OriginalFrontMatter { get; set; } = new();

    /// <summary>导入时的正文（不含 front-matter），发布前不再变化。</summary>
    public string Body { get; set; } = string.Empty;

    public DraftState State { get; } = new();

    /// <summary>发布到的分类；空字符串表示“不进行分类”（直接放 _posts 下）。</summary>
    public string Category { get; set; } = string.Empty;

    public string FileName => Path.GetFileName(SourcePath);

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(State.Title) ? FileName : State.Title.Trim();

    public string DisplaySubtitle
    {
        get
        {
            if (State.IsModified)
            {
                return Lang.T("publish.draftModified");
            }

            var pending = IsTemporary
                ? Lang.T("publish.draftPlain") + "  ·  " + Lang.T("publish.draftTemporary")
                : Lang.T("publish.draftPlain");

            return string.Equals(DisplayTitle, FileName, StringComparison.Ordinal)
                ? pending
                : FileName + "  ·  " + pending;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>表单内容变化后刷新列表中的标题 / 副标题。</summary>
    public void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySubtitle)));
    }
}

