using System.Globalization;
using EasyHexo.Localization;
using EasyHexo.Services;
using Microsoft.UI.Xaml;

namespace EasyHexo.Models;

/// <summary>
/// 配置列表里的一行（界面 ViewModel）。每次展开 / 收起都会重新生成行对象，
/// 当前输入内容保存在 <see cref="ConfigEntry.EditingValue"/> 里，因此不会丢。
///
/// 简体中文界面下，键名通过 <see cref="ConfigKeyTranslator"/> 显示中文译名；
/// 英文原名只作为提示（描述行 / ToolTip）展示，写回文件时仍使用原始英文键名。
/// </summary>
public sealed class ConfigRow
{
    public ConfigRow(ConfigEntry entry)
    {
        Entry = entry;
        Indent = new Thickness(entry.Depth * 18, 0, 0, 0);

        var path = BuildPath(entry);
        var displayName = ConfigKeyTranslator.Translate(path, entry.Key);
        var isTranslated = !string.Equals(displayName, entry.Key, StringComparison.Ordinal);

        Key = entry.IsSequenceItem && entry.Key != "-" ? "– " + displayName : displayName;
        KeyToolTip = isTranslated ? entry.Key : null;

        // 原有注释作为描述（只读）；没有注释时，用英文原名提示对应关系
        Comment = string.IsNullOrWhiteSpace(entry.Comment)
            ? (isTranslated ? entry.Key : string.Empty)
            : entry.Comment;
        CommentVisibility = string.IsNullOrWhiteSpace(Comment) ? Visibility.Collapsed : Visibility.Visible;

        ChevronGlyph = entry.HasChildren ? (entry.IsExpanded ? "\uE70D" : "\uE76C") : string.Empty;
        ChevronVisibility = entry.HasChildren ? Visibility.Visible : Visibility.Collapsed;
        HasChildren = entry.HasChildren;
        CanExpand = entry.HasChildren;

        if (entry.HasChildren)
        {
            ValueText = string.Empty;
            TypeText = Lang.T("config.groupWithCount", entry.Children.Count);
            EditorVisibility = Visibility.Collapsed;
            StaticValueVisibility = Visibility.Collapsed;
        }
        else if (entry.IsReadOnly)
        {
            ValueText = entry.RawValue;
            TypeText = Lang.T("config.readonly");
            EditorVisibility = Visibility.Collapsed;
            StaticValueVisibility = Visibility.Visible;
        }
        else
        {
            ValueText = entry.EditingValue;
            TypeText = DescribeType(entry.OriginalValue);
            EditorVisibility = Visibility.Visible;
            StaticValueVisibility = Visibility.Collapsed;
        }
    }

    public ConfigEntry Entry { get; }

    public Thickness Indent { get; }

    public string Key { get; }

    /// <summary>中文译名下显示的英文原名（未翻译时为 null）。</summary>
    public string? KeyToolTip { get; }

    public string ValueText { get; }

    public string TypeText { get; }

    public string Comment { get; }

    public string ChevronGlyph { get; }

    public Visibility ChevronVisibility { get; }

    public bool HasChildren { get; }

    public bool CanExpand { get; }

    public Visibility EditorVisibility { get; }

    public Visibility StaticValueVisibility { get; }

    public Visibility CommentVisibility { get; }

    private static string BuildPath(ConfigEntry entry)
    {
        var keys = new List<string>();
        for (var current = entry; current is not null; current = current.Parent)
        {
            keys.Add(current.Key);
        }

        keys.Reverse();
        // 与 ConfigKeyTranslator 的键路径一致，用 "." 连接（例如 pretty_urls.trailing_index）
        return string.Join(".", keys);
    }

    private static string DescribeType(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Lang.T("config.type.text");
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
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
