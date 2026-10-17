namespace EasyHexo.Localization;

/// <summary>
/// 简体中文界面下，把 Hexo 固定配置项的英文键名翻译成中文显示。
///
/// 重要：这里只影响“界面显示”。写回 _config.yml 时始终使用原始英文键名
/// （ConfigEntry.Key 不会被修改），因此不会破坏 Hexo 配置。
///
/// 匹配方式：优先按“完整键路径”（如 pretty_urls/trailing_index）匹配，
/// 未收录的键（用户自定义项、主题私有项等）回退显示英文原名。
/// </summary>
public static class ConfigKeyTranslator
{
    private static readonly Dictionary<string, string> PathNames = new(StringComparer.Ordinal)
    {
        // ── Site ────────────────────────────────────────────────
        ["title"] = "站点标题",
        ["subtitle"] = "站点副标题",
        ["description"] = "站点描述",
        ["keywords"] = "站点关键词",
        ["author"] = "作者",
        ["language"] = "语言",
        ["timezone"] = "时区",

        // ── URL ─────────────────────────────────────────────────
        ["url"] = "站点网址",
        ["root"] = "站点根路径",
        ["permalink"] = "文章永久链接格式",
        ["permalink_defaults"] = "永久链接默认值",
        ["permalink_defaults.lang"] = "永久链接默认语言",
        ["pretty_urls"] = "美化 URL",
        ["pretty_urls.trailing_index"] = "去掉链接中的 index.html",
        ["pretty_urls.trailing_slash"] = "链接末尾保留斜杠",

        // ── Directory ───────────────────────────────────────────
        ["source_dir"] = "源文件目录",
        ["public_dir"] = "静态文件输出目录",
        ["tag_dir"] = "标签目录",
        ["archive_dir"] = "归档目录",
        ["category_dir"] = "分类目录",
        ["code_dir"] = "代码目录",
        ["i18n_dir"] = "多语言目录",
        ["skip_render"] = "跳过渲染的文件",

        // ── Writing ─────────────────────────────────────────────
        ["new_post_name"] = "新文章默认文件名",
        ["default_layout"] = "默认布局",
        ["titlecase"] = "标题转为首字母大写",
        ["external_link"] = "外部链接",
        ["external_link.enable"] = "在新标签页打开",
        ["external_link.field"] = "生效范围",
        ["external_link.exclude"] = "排除的域名",
        ["external_link_new_tab"] = "外部链接在新标签页打开",
        ["filename_case"] = "文件名大小写转换",
        ["render_drafts"] = "渲染草稿",
        ["post_asset_folder"] = "启用文章资源文件夹",
        ["relative_link"] = "使用相对链接",
        ["future"] = "发布未来文章",
        ["syntax_highlighter"] = "语法高亮器",
        ["highlight"] = "代码高亮",
        ["highlight.line_number"] = "显示行号",
        ["highlight.auto_detect"] = "自动检测语言",
        ["highlight.tab_replace"] = "Tab 替换字符",
        ["highlight.wrap"] = "自动换行",
        ["highlight.hljs"] = "使用 hljs 主题",
        ["prismjs"] = "代码高亮（PrismJS）",
        ["prismjs.preprocess"] = "预处理",
        ["prismjs.line_number"] = "显示行号",
        ["prismjs.tab_replace"] = "Tab 替换字符",

        // ── Home page setting ──────────────────────────────────
        ["index_generator"] = "首页生成器",
        ["index_generator.path"] = "首页路径",
        ["index_generator.per_page"] = "每页文章数",
        ["index_generator.order_by"] = "排序方式",

        // ── Category & Tag ─────────────────────────────────────
        ["default_category"] = "默认分类",
        ["category_map"] = "分类别名映射",
        ["tag_map"] = "标签别名映射",

        // ── Metadata elements ──────────────────────────────────
        ["meta_generator"] = "生成 generator 元标签",

        // ── Date / Time format ─────────────────────────────────
        ["date_format"] = "日期格式",
        ["time_format"] = "时间格式",
        ["updated_option"] = "更新时间取值方式",

        // ── Pagination ─────────────────────────────────────────
        ["per_page"] = "每页文章数",
        ["pagination_dir"] = "分页目录",

        // ── Include / Exclude file(s) ──────────────────────────
        ["include"] = "强制包含的文件",
        ["exclude"] = "排除的文件",
        ["ignore"] = "忽略的文件",

        // ── Extensions ─────────────────────────────────────────
        ["theme"] = "主题",
        ["theme_config"] = "主题配置",
        ["marked"] = "Markdown 渲染器",
        ["marked.prependRoot"] = "预处理根路径",
        ["marked.modifyAnchors"] = "处理标题锚点",
        ["marked.lazyload"] = "图片懒加载",
        ["marked.prependRootPath"] = "预处理根路径",
        ["toc"] = "目录",
        ["toc.max_depth"] = "最大标题层级",
        ["toc.min_depth"] = "最小标题层级",
        ["toc.list_number"] = "显示标题序号",

        // ── Deployment ─────────────────────────────────────────
        ["deploy"] = "部署配置",
        ["deploy.type"] = "部署类型",
        ["deploy.repo"] = "仓库地址",
        ["deploy.branch"] = "分支",
        ["deploy.message"] = "提交信息",
        ["deploy.name"] = "用户名",
        ["deploy.email"] = "邮箱",
        ["deploy.ignore_hidden"] = "忽略隐藏文件",
        ["deploy.ignore_pattern"] = "忽略的文件模式",
    };

    /// <summary>键名是否已收录中文译名（用于决定是否附带英文原名提示）。</summary>
    public static bool HasChineseName(string path) => PathNames.ContainsKey(path);

    /// <summary>
    /// 取界面显示名。非简体中文时始终返回英文原名。
    /// </summary>
    public static string Translate(string path, string key)
    {
        if (Lang.Current != Lang.ZhCn)
        {
            return key;
        }

        if (string.IsNullOrEmpty(key) || key == "-")
        {
            return key;
        }

        return PathNames.TryGetValue(path, out var name) ? name : key;
    }
}
