using System.Collections.Generic;

namespace EasyHexo.Localization;

/// <summary>
/// 极简运行时多语言表。没有使用 .resw / MRT，原因是：
/// 1) 非打包（unpackaged）应用里 MRT Core 的语言覆盖行为不稳定；
/// 2) 本项目只需要中/英两套界面文案，字典 + 附加属性就足够做到“切换后立即生效”。
/// 新增文案时，两个字典都要加同一个 key。
/// </summary>
internal static class StringsZhCn
{
    internal static readonly Dictionary<string, string> Map = new()
    {
        // ── 通用 ──────────────────────────────────────────────
        ["app.title"] = "Hexo 配置易",
        ["nav.config"] = "配置编辑",
        ["nav.publish"] = "发布文章",
        ["nav.settings"] = "设置",

        ["common.success"] = "成功",
        ["common.error"] = "出错",
        ["common.warning"] = "提示",
        ["common.info"] = "提示",
        ["common.reload"] = "重新载入",
        ["common.expandAll"] = "全部展开",
        ["common.collapseAll"] = "全部收起",
        ["common.cancel"] = "取消",
        ["common.overwrite"] = "覆盖",
        ["common.browse"] = "浏览…",
        ["common.openFolder"] = "打开目录",
        ["common.refresh"] = "刷新",
        ["common.clear"] = "清空",

        // ── Hexo 根目录 ───────────────────────────────────────
        ["root.current"] = "当前 Hexo 根目录",
        ["root.none"] = "尚未选择",
        ["root.choose"] = "选择 Hexo 根目录",
        ["root.invalid"] = "所选目录不是有效的 Hexo 根目录：需要包含 _config.yml 文件和 source 文件夹。",
        ["root.error.notFound"] = "目录不存在",
        ["root.error.config"] = "缺少 _config.yml",
        ["root.error.source"] = "缺少 source 文件夹",
        ["root.loadError"] = "读取 Hexo 根目录失败：{0}",

        // ── 配置编辑 ──────────────────────────────────────────
        ["config.title"] = "配置编辑",
        ["config.subtitle"] = "依据 _config.yml 的缩进自动生成分层列表：分组可展开 / 收起，可编辑项直接输入新值，注释会显示为对应项的描述（不可修改）。修改完成后点击右上角“应用到配置”写回文件。",
        ["config.apply"] = "应用到配置",
        ["config.stats"] = "共 {0} 个配置项 · 已修改 {1} 项",
        ["config.noChanges"] = "没有需要应用的修改。",
        ["config.applied"] = "已应用 {0} 项修改。原文件已备份为：{1}",
        ["config.applyFailed"] = "应用失败：{0}",
        ["config.loaded"] = "已载入 {0} 个配置项。",
        ["config.loadError"] = "解析 _config.yml 失败：{0}",
        ["config.empty"] = "这个 _config.yml 里没有可显示的配置项。",
        ["config.discardTitle"] = "放弃未应用的修改？",
        ["config.discardMessage"] = "当前有 {0} 项修改尚未写回 _config.yml，继续将丢失这些修改。",
        ["config.discardConfirm"] = "放弃修改",
        ["config.groupWithCount"] = "分组 · {0} 项",
        ["config.readonly"] = "只读",
        ["config.type.text"] = "文本",
        ["config.type.number"] = "数字",
        ["config.type.bool"] = "布尔",
        ["config.type.list"] = "列表",
        ["config.type.other"] = "其它",
        ["config.unparsed"] = "另有 {0} 行无法解析，已原样保留。",

        // ── 发布文章 ──────────────────────────────────────────
        ["publish.title"] = "发布文章",
        ["publish.subtitle"] = "从左侧打开已发布文章（按修改日期从新到旧），或把 .md / .txt 文件拖入窗口。标题、日期为必填项，标签输入后按 Enter 锁定。",
        ["publish.posts"] = "已发布文章",
        ["publish.postCount"] = "共 {0} 篇",
        ["publish.postsEmpty"] = "source\\_posts 目录中还没有文章。",
        ["publish.info"] = "文章信息",
        ["publish.titleField"] = "标题（必填）",
        ["publish.dateField"] = "日期（必填）",
        ["publish.timeField"] = "时间",
        ["publish.tagsField"] = "标签",
        ["publish.tagsPlaceholder"] = "输入一个标签后按 Enter 确认",
        ["publish.dropHint"] = "把 .md / .txt 文件拖到窗口中，或点击右侧“浏览…”",
        ["publish.browse"] = "浏览…",
        ["publish.button"] = "发布文章",
        ["publish.sourceFile"] = "当前文件",
        ["publish.noFile"] = "（未选择文件）",
        ["publish.queue"] = "待发布队列还有 {0} 个文件",
        ["publish.assets"] = "资源文件处理",
        ["publish.assetsFolderPlaceholder"] = "标题",
        ["publish.assetsInfo"] = "文中形如 ![图片](E:\\images\\a.png) 或 [附件](D:\\files\\a.zip) 的本机绝对路径，会在发布时被复制到 source\\{0}\\，链接改写为 /{0}/文件名。http(s) 等网页链接会被跳过。",
        ["publish.needTitle"] = "请填写文章标题。",
        ["publish.needFile"] = "请先浏览或拖入要发布的 .md / .txt 文件。",
        ["publish.success"] = "发布成功：{0}",
        ["publish.assetsCopied"] = "已复制 {0} 个资源文件到 {1}",
        ["publish.failed"] = "发布失败：{0}",
        ["publish.overwriteTitle"] = "覆盖已有文章？",
        ["publish.overwriteMessage"] = "目标文件已存在，继续发布将覆盖它：\n{0}",
        ["publish.loaded"] = "已载入：{0}",
        ["publish.loadFailed"] = "读取文件失败：{0}",
        ["publish.warnMissingAsset"] = "未找到该本地文件，已保留原链接：{0}",
        ["publish.warnCopyFailed"] = "复制失败，已保留原链接：{0}（{1}）",        ["publish.txtImported"] = ".txt 文件已复制到临时目录并改名为 .md。",
        ["publish.skipped"] = "已跳过 {0} 个不支持的文件（仅支持 .md / .txt）。",
        ["publish.dropCaption"] = "松开以载入文章文件",
        ["publish.frontMatterFound"] = "已识别文件开头的 front-matter。",
        ["publish.frontMatterMissing"] = "未找到标准 front-matter，填写以下内容后将在发布时写入文件开头。",
        ["publish.nextQueued"] = "已载入队列中的下一篇文章，剩余 {0} 篇。",
        ["publish.editHint"] = "点击左侧文章可重新编辑发布",

        // ── 设置 ──────────────────────────────────────────────
        ["settings.title"] = "设置",
        ["settings.subtitle"] = "语言、Hexo 根目录与关于信息。",
        ["settings.language"] = "语言 / Language",
        ["settings.languageHint"] = "切换后界面文案立即生效，设置会保存在本机。",
        ["settings.languageChanged"] = "界面语言已切换。",
        ["settings.root"] = "Hexo 根目录",
        ["settings.changeRoot"] = "更换根目录",
        ["settings.rootChanged"] = "已切换 Hexo 根目录。",
        ["settings.openFolder"] = "在资源管理器中打开",
        ["settings.about"] = "关于",
        ["settings.version"] = "版本 {0}",
        ["settings.repo"] = "软件仓库",
        ["settings.description"] = "Hexo 配置易（Easy Hexo）是一个面向 Windows 10 / 11 的 WinUI 3 桌面工具：可视化编辑 Hexo 的 _config.yml，并通过表单化流程发布 Markdown 文章、自动整理本地图片与附件。",
        ["settings.license"] = "许可证：MIT",
        ["settings.tech"] = "技术栈：WinUI 3 (Windows App SDK) + .NET 8 + C#",
    };
}
