namespace EasyHexo.Services;

/// <summary>配置项在界面上使用的编辑控件类型。</summary>
public enum ConfigEditorKind
{
    /// <summary>普通文本输入框。</summary>
    Text,

    /// <summary>开关（写回 true / false）。</summary>
    Bool,

    /// <summary>数字输入（NumberBox）。</summary>
    Number,

    /// <summary>下拉选择（可编辑，允许输入列表外的值）。</summary>
    Choice,

    /// <summary>目录路径：文本输入 + 文件夹选择。</summary>
    Directory,
}

/// <summary>某个配置项对应的编辑方式与候选项。</summary>
public sealed class ConfigEditorSpec
{
    public ConfigEditorKind Kind { get; init; } = ConfigEditorKind.Text;

    public List<string> Options { get; init; } = new();

    public bool ComboEditable { get; init; } = true;

    public static ConfigEditorSpec Text { get; } = new();

    public static ConfigEditorSpec Bool { get; } = new() { Kind = ConfigEditorKind.Bool };

    public static ConfigEditorSpec Number { get; } = new() { Kind = ConfigEditorKind.Number };

    public static ConfigEditorSpec Directory { get; } = new() { Kind = ConfigEditorKind.Directory };
}

/// <summary>
/// 决定哪些配置项用可视化控件编辑。
///
/// 原则：只对“Hexo 语义明确”的固定项做映射；用户自定义键（比如主题配置里的任意键）
/// 如果没有命中规则，仍然使用文本框，避免猜错语义。值的写回仍然交给
/// <see cref="ConfigYamlService"/>，因此 true/false、数字等都会按 YAML 规范输出。
/// </summary>
public static class ConfigEditorMetadata
{
    private static readonly HashSet<string> BoolPaths = new(StringComparer.Ordinal)
    {
        "titlecase",
        "render_drafts",
        "post_asset_folder",
        "relative_link",
        "future",
        "meta_generator",
        "external_link.enable",
        "external_link_new_tab",
        "pretty_urls.trailing_index",
        "pretty_urls.trailing_slash",
        "highlight.line_number",
        "highlight.auto_detect",
        "highlight.wrap",
        "highlight.hljs",
        "prismjs.preprocess",
        "prismjs.line_number",
        "toc.list_number",
        "marked.prependRoot",
        "marked.modifyAnchors",
        "marked.lazyload",
        "deploy.ignore_hidden",
    };

    private static readonly HashSet<string> NumberPaths = new(StringComparer.Ordinal)
    {
        "per_page",
        "index_generator.per_page",
        "toc.max_depth",
        "toc.min_depth",
    };

    private static readonly HashSet<string> DirectoryPaths = new(StringComparer.Ordinal)
    {
        "source_dir",
        "public_dir",
        "tag_dir",
        "archive_dir",
        "category_dir",
        "code_dir",
        "i18n_dir",
    };

    private static readonly Dictionary<string, string[]> ChoicePaths = new(StringComparer.Ordinal)
    {
        ["language"] = new[]
        {
            "zh-CN", "zh-TW", "en", "ja", "ko", "fr-FR", "de-DE", "es-ES", "pt-BR", "ru-RU", "default",
        },
        ["timezone"] = new[]
        {
            "", "Asia/Shanghai", "Asia/Hong_Kong", "Asia/Taipei", "Asia/Tokyo", "Asia/Seoul",
            "Asia/Singapore", "Europe/London", "Europe/Paris", "Europe/Berlin", "Europe/Moscow",
            "America/New_York", "America/Chicago", "America/Los_Angeles", "Australia/Sydney", "UTC",
        },
        ["date_format"] = new[]
        {
            "YYYY-MM-DD", "YYYY/MM/DD", "MM-DD", "DD/MM/YYYY", "YYYY年MM月DD日",
        },
        ["time_format"] = new[]
        {
            "HH:mm:ss", "HH:mm", "HH:mm:ss.SSS", "h:mm a",
        },
        ["syntax_highlighter"] = new[] { "highlight.js", "prismjs", "false" },
        ["default_layout"] = new[] { "post", "page", "draft" },
        ["updated_option"] = new[] { "mtime", "date", "" },
        ["index_generator.order_by"] = new[] { "-date", "date", "-updated", "updated" },
        ["external_link.field"] = new[] { "site", "post" },
        ["filename_case"] = new[] { "0", "1", "2" },
        ["permalink"] = new[]
        {
            ":year/:month/:day/:title/",
            ":year/:month/:title/",
            ":year/:month/:day/:title.html",
            ":title/",
            ":category/:title/",
        },
        ["deploy.type"] = new[] { "git", "heroku", "rsync", "open", "netlify" },
    };

    /// <summary>
    /// 解析某个配置项该用的编辑控件。themeOptions 为 /themes/ 下识别到的主题名。
    /// </summary>
    public static ConfigEditorSpec Resolve(string path, string originalValue, IReadOnlyList<string>? themeOptions)
    {
        if (path.Equals("theme", StringComparison.Ordinal))
        {
            return new ConfigEditorSpec
            {
                Kind = ConfigEditorKind.Choice,
                Options = themeOptions?.ToList() ?? new List<string>(),
                ComboEditable = true,
            };
        }

        if (ChoicePaths.TryGetValue(path, out var options))
        {
            return new ConfigEditorSpec
            {
                Kind = ConfigEditorKind.Choice,
                Options = options.ToList(),
                ComboEditable = true,
            };
        }

        if (BoolPaths.Contains(path))
        {
            return ConfigEditorSpec.Bool;
        }

        if (NumberPaths.Contains(path))
        {
            return ConfigEditorSpec.Number;
        }

        if (DirectoryPaths.Contains(path))
        {
            return ConfigEditorSpec.Directory;
        }

        // 未知键（含主题配置的自定义键）：值恰好是 true/false 时给个开关，方便操作
        if (IsBoolValue(originalValue))
        {
            return ConfigEditorSpec.Bool;
        }

        return ConfigEditorSpec.Text;
    }

    public static bool IsBoolValue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
