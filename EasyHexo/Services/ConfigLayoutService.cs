namespace EasyHexo.Services;

/// <summary>一个显示分区：把顶层配置项按“常用程度”聚在一起。</summary>
public sealed record ConfigSectionDefinition(string Id, string TitleKey, IReadOnlyList<string> Keys);

/// <summary>构建后的分区（含实际的顶层配置项）。</summary>
public sealed class ConfigSectionLayout
{
    public ConfigSectionLayout(string id, string titleKey)
    {
        Id = id;
        TitleKey = titleKey;
    }

    public string Id { get; }

    public string TitleKey { get; }

    public List<ConfigEntry> Entries { get; } = new();
}

/// <summary>
/// 站点的 _config.yml 显示顺序。
///
/// 文件本身的顺序不会被修改（写回只替换值所在行），这里只影响界面展示：
/// 按“站点信息 → 网址 → 目录 → 写作 → 代码高亮 → 首页/分页 → 分类标签 →
/// 元信息 → 主题与扩展 → 部署 → 其它”的常用度分区，替换掉原来完全跟文件顺序的列表。
/// </summary>
public static class ConfigLayoutService
{
    public static IReadOnlyList<ConfigSectionDefinition> Definitions { get; } = new List<ConfigSectionDefinition>
    {
        new("site", "config.section.site", new[]
        {
            "title", "subtitle", "description", "keywords", "author", "language", "timezone",
        }),
        new("url", "config.section.url", new[]
        {
            "url", "root", "permalink", "permalink_defaults", "pretty_urls",
        }),
        new("directory", "config.section.directory", new[]
        {
            "source_dir", "public_dir", "tag_dir", "archive_dir", "category_dir", "code_dir",
            "i18n_dir", "skip_render",
        }),
        new("writing", "config.section.writing", new[]
        {
            "new_post_name", "default_layout", "titlecase", "external_link", "external_link_new_tab",
            "filename_case", "render_drafts", "post_asset_folder", "relative_link", "future",
            "include", "exclude", "ignore",
        }),
        new("highlight", "config.section.highlight", new[]
        {
            "syntax_highlighter", "highlight", "prismjs",
        }),
        new("home", "config.section.home", new[]
        {
            "index_generator", "per_page", "pagination_dir",
        }),
        new("taxonomy", "config.section.taxonomy", new[]
        {
            "default_category", "category_map", "tag_map",
        }),
        new("meta", "config.section.meta", new[]
        {
            "meta_generator", "date_format", "time_format", "updated_option",
        }),
        new("theme", "config.section.theme", new[]
        {
            "theme", "theme_config", "marked", "toc",
        }),
        new("deploy", "config.section.deploy", new[]
        {
            "deploy",
        }),
    };

    /// <summary>把顶层配置项按分区整理；未收录的键进入“其它配置”。</summary>
    public static List<ConfigSectionLayout> BuildSections(IReadOnlyList<ConfigEntry> roots)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var sections = new List<ConfigSectionLayout>();

        for (var i = 0; i < Definitions.Count; i++)
        {
            var definition = Definitions[i];
            var section = new ConfigSectionLayout(definition.Id, definition.TitleKey);
            sections.Add(section);
            foreach (var key in definition.Keys)
            {
                index[key] = i;
            }
        }

        var other = new ConfigSectionLayout("other", "config.section.other");
        var sectionEntries = sections.Select(section => section.Entries).ToList();

        foreach (var root in roots)
        {
            if (index.TryGetValue(root.Key, out var sectionIndex))
            {
                sectionEntries[sectionIndex].Add(root);
            }
            else
            {
                other.Entries.Add(root);
            }
        }

        // 分区内部按定义顺序排序（未在定义里出现的键排在该分区末尾，保持文件顺序）
        for (var i = 0; i < sections.Count; i++)
        {
            var order = Definitions[i].Keys;
            var orderIndex = order
                .Select((key, position) => (key, position))
                .ToDictionary(pair => pair.key, pair => pair.position, StringComparer.Ordinal);

            sections[i].Entries.Sort((left, right) =>
            {
                var hasLeft = orderIndex.TryGetValue(left.Key, out var leftPosition);
                var hasRight = orderIndex.TryGetValue(right.Key, out var rightPosition);
                if (hasLeft && hasRight)
                {
                    return leftPosition.CompareTo(rightPosition);
                }

                if (hasLeft)
                {
                    return -1;
                }

                return hasRight ? 1 : 0;
            });
        }

        var result = sections.Where(section => section.Entries.Count > 0).ToList();
        if (other.Entries.Count > 0)
        {
            result.Add(other);
        }

        return result;
    }
}
