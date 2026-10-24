using System.ComponentModel;
using System.Globalization;
using EasyHexo.Localization;
using EasyHexo.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace EasyHexo.Models;

/// <summary>
/// 配置列表里的一行（界面 ViewModel）。
///
/// 通用行有三种形态：
/// * 分区标题行（IsSection = true）：用于“站点信息 / 网址与链接 / …”这类视觉分区；
/// * 分组行：有子项，可展开 / 收起；
/// * 配置项行：按 <see cref="ConfigEditorMetadata"/> 决定用文本框 / 开关 / 下拉 / 数字框 / 目录选择。
///
/// 当前输入内容保存在 <see cref="ConfigEntry.EditingValue"/> 里，行对象在展开 / 收起 /
/// 撤回时会重建，所以编辑内容不会丢。
/// </summary>
public sealed class ConfigRow : INotifyPropertyChanged
{
    private static readonly Brush DirtyBackgroundBrush =
        new SolidColorBrush(ColorHelper.FromArgb(0x2E, 0xFF, 0xC1, 0x07));

    private static readonly Brush DirtyBorderBrush =
        new SolidColorBrush(ColorHelper.FromArgb(0xA0, 0xF5, 0x9E, 0x0B));

    private ConfigRow()
    {
    }

    public ConfigRow(ConfigEntry entry, string path, bool translateKeys, IReadOnlyList<string>? themeOptions)
    {
        Entry = entry;
        EditorAutomationId = "cfg-" + path;
        IsSection = false;
        Indent = new Thickness(entry.Depth * 18, 0, 0, 0);
        SectionVisibility = Visibility.Collapsed;
        ContentVisibility = Visibility.Visible;

        var displayName = translateKeys ? ConfigKeyTranslator.Translate(path, entry.Key) : entry.Key;
        var isTranslated = translateKeys && !string.Equals(displayName, entry.Key, StringComparison.Ordinal);

        Key = entry.IsSequenceItem && entry.Key != "-" ? "– " + displayName : displayName;
        KeyToolTip = isTranslated ? entry.Key : null;

        Comment = string.IsNullOrWhiteSpace(entry.Comment)
            ? (isTranslated ? entry.Key : string.Empty)
            : entry.Comment;
        CommentVisibility = string.IsNullOrWhiteSpace(Comment) ? Visibility.Collapsed : Visibility.Visible;

        ChevronGlyph = entry.HasChildren ? (entry.IsExpanded ? "\uE70D" : "\uE76C") : string.Empty;
        ChevronVisibility = entry.HasChildren ? Visibility.Visible : Visibility.Collapsed;
        CanExpand = entry.HasChildren;

        if (entry.HasChildren)
        {
            TypeText = Lang.T("config.groupWithCount", entry.Children.Count);
        }
        else if (entry.IsReadOnly)
        {
            ValueText = entry.RawValue;
            StaticValueVisibility = Visibility.Visible;
            TypeText = Lang.T("config.readonly");
        }
        else
        {
            var spec = ConfigEditorMetadata.Resolve(path, entry.OriginalValue, themeOptions);
            Kind = spec.Kind;

            switch (spec.Kind)
            {
                case ConfigEditorKind.Bool:
                    BoolValue = !entry.EditingValue.Equals("false", StringComparison.OrdinalIgnoreCase);
                    BoolVisibility = Visibility.Visible;
                    TypeText = Lang.T("config.type.bool");
                    break;

                case ConfigEditorKind.Number:
                    NumberValue = double.TryParse(
                        entry.EditingValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number)
                        ? number
                        : 0;
                    NumberVisibility = Visibility.Visible;
                    TypeText = Lang.T("config.type.number");
                    break;

                case ConfigEditorKind.Choice:
                    ComboItems = spec.Options;
                    ComboText = entry.EditingValue;
                    ComboEditable = spec.ComboEditable;
                    ComboVisibility = Visibility.Visible;
                    TypeText = Lang.T("config.type.choice");
                    break;

                case ConfigEditorKind.Directory:
                    ValueText = entry.EditingValue;
                    TextVisibility = Visibility.Visible;
                    DirButtonVisibility = Visibility.Visible;
                    TypeText = Lang.T("config.type.dir");
                    break;

                default:
                    ValueText = entry.EditingValue;
                    TextVisibility = Visibility.Visible;
                    TypeText = DescribeType(entry.OriginalValue);
                    break;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Thickness DirtyBorderThickness { get; } = new(1);

    private static Thickness NoBorderThickness { get; } = new(0);

    public ConfigEntry? Entry { get; }

    public bool IsSection { get; private set; }

    // ── 分区标题行 ───────────────────────────────────────────────

    public string SectionId { get; private set; } = string.Empty;

    public string SectionTitle { get; private set; } = string.Empty;

    public string SectionChevron { get; private set; } = string.Empty;

    public string SectionCountText { get; private set; } = string.Empty;

    public Visibility SectionVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility ContentVisibility { get; private set; } = Visibility.Visible;

    // ── 配置项行 ─────────────────────────────────────────────────

    public Thickness Indent { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string? KeyToolTip { get; private set; }

    public string ValueText { get; private set; } = string.Empty;

    public string ComboText { get; private set; } = string.Empty;

    public IReadOnlyList<string> ComboItems { get; private set; } = new List<string>();

    public bool ComboEditable { get; private set; } = true;

    public bool BoolValue { get; private set; }

    public double NumberValue { get; private set; }

    public string TypeText { get; private set; } = string.Empty;

    public string Comment { get; private set; } = string.Empty;

    public Visibility CommentVisibility { get; private set; } = Visibility.Collapsed;

    public string ChevronGlyph { get; private set; } = string.Empty;

    public Visibility ChevronVisibility { get; private set; } = Visibility.Collapsed;

    public bool CanExpand { get; private set; }

    public Visibility TextVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility ComboVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility BoolVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility NumberVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility StaticValueVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility DirButtonVisibility { get; private set; } = Visibility.Collapsed;

    public ConfigEditorKind Kind { get; private set; } = ConfigEditorKind.Text;

    /// <summary>供 UI 自动化 / 测试定位控件使用的 ID（cfg-键路径）。</summary>
    public string EditorAutomationId { get; private set; } = string.Empty;

    public bool IsDirty => Entry?.IsDirty ?? false;

    public Visibility DirtyDotVisibility => IsDirty ? Visibility.Visible : Visibility.Collapsed;

    public Brush? RowBackground => IsDirty ? DirtyBackgroundBrush : null;

    public Brush? RowBorderBrush => IsDirty ? DirtyBorderBrush : null;

    public Thickness RowBorderThickness => IsDirty ? DirtyBorderThickness : NoBorderThickness;

    public static ConfigRow CreateSection(string sectionId, string title, int count, bool expanded)
    {
        return new ConfigRow
        {
            IsSection = true,
            SectionId = sectionId,
            SectionTitle = title,
            SectionChevron = expanded ? "\uE70D" : "\uE76C",
            SectionCountText = Lang.T("config.section.count", count),
            SectionVisibility = Visibility.Visible,
            ContentVisibility = Visibility.Collapsed,
        };
    }

    /// <summary>配置项值被修改后刷新高亮相关的绑定。</summary>
    public void RefreshDirtyState()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(DirtyDotVisibility));
        Raise(nameof(RowBackground));
        Raise(nameof(RowBorderBrush));
        Raise(nameof(RowBorderThickness));
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string DescribeType(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Lang.T("config.type.text");
        }

        if (ConfigEditorMetadata.IsBoolValue(value))
        {
            return Lang.T("config.type.bool");
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return Lang.T("config.type.number");
        }

        if ((value.StartsWith('[') && value.EndsWith(']')) ||
            (value.StartsWith('{') && value.EndsWith('}')))
        {
            return Lang.T("config.type.list");
        }

        return Lang.T("config.type.text");
    }
}
