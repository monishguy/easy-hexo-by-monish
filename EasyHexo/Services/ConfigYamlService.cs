using System.Globalization;
using System.Text;

namespace EasyHexo.Services;

/// <summary>
/// _config.yml 中的一个配置项。解析时会记住它所在的行号、行前缀（缩进 + "key:"）
/// 以及行尾注释，写回时只替换“值”那一段，从而最大程度保留原文件的排版与注释。
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>键名；块序列的纯量项用 "-" 作为键名。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>层级（顶层为 0），用于界面缩进。</summary>
    public int Depth { get; set; }

    /// <summary>该行内容在 YAML 中的有效缩进（块序列映射项会额外 +2）。</summary>
    public int Indent { get; set; }

    /// <summary>在文件中的行号（0 基）。</summary>
    public int LineIndex { get; set; } = -1;

    /// <summary>值之前的原始文本，例如 "  title: "。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>值之后的原始文本（行尾注释），例如 " # 站点标题"。</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>原始值的引号类型（'\''、'"' 或 '\0' 表示无引号）。</summary>
    public char QuoteChar { get; set; }

    /// <summary>原始值文本（已去掉行尾注释，可能带引号）。</summary>
    public string RawValue { get; set; } = string.Empty;

    /// <summary>展示给用户的原始值（去掉引号）。</summary>
    public string OriginalValue { get; set; } = string.Empty;

    /// <summary>界面上正在编辑的值。</summary>
    public string EditingValue { get; set; } = string.Empty;

    /// <summary>原有注释（上方整行注释 + 行尾注释），只读展示。</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>是否是块序列（"- xxx"）中的一项。</summary>
    public bool IsSequenceItem { get; set; }

    /// <summary>是否只读（多行块标量等无法安全单行替换的项）。</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>分组是否展开（界面状态）。</summary>
    public bool IsExpanded { get; set; }

    public ConfigEntry? Parent { get; set; }

    public List<ConfigEntry> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    /// <summary>有子项的是分组，不可直接编辑。</summary>
    public bool IsEditable => !HasChildren && !IsReadOnly && LineIndex >= 0;

    public bool IsDirty => IsEditable &&
                           !string.Equals(EditingValue, OriginalValue, StringComparison.Ordinal);

    /// <summary>用当前 EditingValue 生成新的整行文本。</summary>
    public string BuildLine() => Prefix + FormatScalar(EditingValue) + Suffix;

    private string FormatScalar(string value)
    {
        value ??= string.Empty;

        // 保留原来的引号风格
        if (QuoteChar == '\'')
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        if (QuoteChar == '"')
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        if (value.Length == 0)
        {
            return string.Empty;
        }

        // 行内集合 / 映射原样写回
        if (IsFlowCollection(value))
        {
            return value;
        }

        if (IsBoolLiteral(value))
        {
            return value.ToLowerInvariant();
        }

        if (IsNumberLiteral(value))
        {
            return value;
        }

        return NeedsQuotes(value) ? "'" + value.Replace("'", "''") + "'" : value;
    }

    private static bool IsFlowCollection(string value) =>
        (value.StartsWith('[') && value.EndsWith(']')) ||
        (value.StartsWith('{') && value.EndsWith('}'));

    private static bool IsBoolLiteral(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberLiteral(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool NeedsQuotes(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return true;
        }

        if (value.IndexOfAny(new[] { '\n', '\r', '\t' }) >= 0)
        {
            return true;
        }

        var first = value[0];
        if (first is '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|'
            or '>' or '\'' or '"' or '%' or '@' or '`')
        {
            return true;
        }

        return value.Contains(": ", StringComparison.Ordinal) ||
               value.EndsWith(':') ||
               value.Contains(" #", StringComparison.Ordinal);
    }
}

/// <summary>解析后的 _config.yml 文档。</summary>
public sealed class YamlConfigDocument
{
    public List<string> Lines { get; } = new();

    public List<ConfigEntry> Roots { get; } = new();

    public string NewLine { get; set; } = "\n";

    public bool EndsWithNewLine { get; set; } = true;

    public bool HadBom { get; set; }

    /// <summary>无法识别（会原样保留）的行数。</summary>
    public int UnparsedLineCount { get; set; }
}

public sealed class ApplyResult
{
    public int AppliedCount { get; init; }

    public string BackupPath { get; init; } = string.Empty;
}

/// <summary>
/// 解析 / 写回 Hexo 的 _config.yml。
///
/// 设计要点：
/// * 不做完整 YAML 语义解析，而是按“缩进 + key: value”逐行解析成树，
///   因此注释、顺序、引号风格都能原样保留；
/// * 写回时只重建被修改的那些行，其余行（包括整行注释、被注释掉的选项）一个字节都不动；
/// * 多行块标量（| / &gt;）标记为只读，避免破坏格式。
/// </summary>
public static class ConfigYamlService
{
    public static YamlConfigDocument Parse(string? text)
    {
        var document = new YamlConfigDocument();
        if (text is null)
        {
            return document;
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
            document.HadBom = true;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var endsWithNewLine = normalized.EndsWith('\n');

        var lines = normalized.Split('\n').ToList();
        if (endsWithNewLine && lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        document.NewLine = newLine;
        document.EndsWithNewLine = endsWithNewLine;
        document.Lines.AddRange(lines);

        var stack = new List<ConfigEntry>();
        var pendingComments = new List<string>();
        var blockScalarIndent = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                if (blockScalarIndent < 0)
                {
                    // 空行隔开注释与后面的项：注释视为“分节标题”，不再挂到下一项
                    pendingComments.Clear();
                }

                continue;
            }

            var contentStart = IndexOfFirstNonSpace(line);
            var indent = MeasureIndent(line, contentStart);

            if (blockScalarIndent >= 0)
            {
                if (indent > blockScalarIndent)
                {
                    continue; // 多行块标量内部的行，原样保留
                }

                blockScalarIndent = -1;
            }

            var content = line[contentStart..];

            if (content.StartsWith('#'))
            {
                pendingComments.Add(CleanComment(content));
                continue;
            }

            if (content is "---" or "...")
            {
                continue;
            }

            // 找到父节点：缩进必须严格小于当前行
            while (stack.Count > 0 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var parent = stack.Count > 0 ? stack[^1] : null;

            // ── 块序列项 ────────────────────────────────────────────
            if (IsSequenceIndicator(content))
            {
                var afterDash = contentStart + 1;
                while (afterDash < line.Length && (line[afterDash] == ' ' || line[afterDash] == '\t'))
                {
                    afterDash++;
                }

                var itemText = line[afterDash..];
                var mappingColon = FindMappingColon(itemText);

                ConfigEntry entry;
                if (mappingColon > 0)
                {
                    // "- key: value" —— 映射项，值从冒号后开始
                    var key = itemText[..mappingColon].Trim();
                    var valueStart = afterDash + mappingColon + 1;
                    while (valueStart < line.Length && (line[valueStart] == ' ' || line[valueStart] == '\t'))
                    {
                        valueStart++;
                    }

                    SplitInlineComment(line[valueStart..], out var valueText, out var suffix);
                    entry = new ConfigEntry
                    {
                        Key = key,
                        LineIndex = i,
                        Prefix = line[..valueStart],
                        Suffix = suffix,
                        IsSequenceItem = true,
                        // 冒号后的键相对 "- " 又缩进了一层
                        Indent = indent + 2,
                    };
                    InitializeValue(entry, valueText);
                }
                else
                {
                    // "- 纯量" —— 纯量项
                    SplitInlineComment(itemText, out var valueText, out var suffix);
                    entry = new ConfigEntry
                    {
                        Key = "-",
                        LineIndex = i,
                        Prefix = line[..afterDash],
                        Suffix = suffix,
                        IsSequenceItem = true,
                        Indent = indent,
                    };
                    InitializeValue(entry, valueText);
                }

                entry.Depth = parent is null ? 0 : parent.Depth + 1;
                Attach(document, parent, entry, pendingComments);
                stack.Add(entry);
                continue;
            }

            // ── 普通 "key: value" ───────────────────────────────────
            var colonIndex = FindMappingColon(content);
            if (colonIndex <= 0)
            {
                document.UnparsedLineCount++;
                continue;
            }

            var keyName = content[..colonIndex].Trim();
            if (keyName.Length == 0)
            {
                document.UnparsedLineCount++;
                continue;
            }

            var valuePosition = contentStart + colonIndex + 1;
            while (valuePosition < line.Length && (line[valuePosition] == ' ' || line[valuePosition] == '\t'))
            {
                valuePosition++;
            }

            SplitInlineComment(line[valuePosition..], out var valuePart, out var commentSuffix);
            var node = new ConfigEntry
            {
                Key = keyName,
                LineIndex = i,
                Prefix = line[..valuePosition],
                Suffix = commentSuffix,
                Indent = indent,
            };
            InitializeValue(node, valuePart);

            if (valuePart.StartsWith('|') || valuePart.StartsWith('>'))
            {
                // 多行块标量：内容无法用单行替换安全表达，只读
                node.IsReadOnly = true;
                blockScalarIndent = indent;
            }

            node.Depth = parent is null ? 0 : parent.Depth + 1;
            Attach(document, parent, node, pendingComments);
            stack.Add(node);
        }

        return document;
    }

    /// <summary>
    /// 把 entries 的修改写回 _config.yml（写前自动备份为 *.easyhexo.bak）。
    /// </summary>
    public static ApplyResult Apply(YamlConfigDocument document, IEnumerable<ConfigEntry> entries, string configPath)
    {
        var lines = new List<string>(document.Lines);
        var applied = 0;

        foreach (var entry in entries)
        {
            if (!entry.IsEditable || entry.LineIndex < 0 || entry.LineIndex >= lines.Count)
            {
                continue;
            }

            lines[entry.LineIndex] = entry.BuildLine();
            applied++;
        }

        if (applied == 0)
        {
            return new ApplyResult();
        }

        var backupPath = configPath + ".easyhexo.bak";
        try
        {
            File.Copy(configPath, backupPath, true);
        }
        catch (Exception ex)
        {
            AppLog.Write("备份 _config.yml 失败", ex);
            backupPath = string.Empty;
        }

        var text = string.Join(document.NewLine, lines);
        if (document.EndsWithNewLine)
        {
            text += document.NewLine;
        }

        TextFileService.WriteAllText(configPath, text, document.HadBom);
        return new ApplyResult { AppliedCount = applied, BackupPath = backupPath };
    }

    /// <summary>把 ConfigEntry 树压平成列表，便于统计脏项数量。</summary>
    public static List<ConfigEntry> Flatten(IEnumerable<ConfigEntry> roots)
    {
        var result = new List<ConfigEntry>();
        foreach (var root in roots)
        {
            Walk(root, result);
        }

        return result;

        static void Walk(ConfigEntry entry, List<ConfigEntry> target)
        {
            target.Add(entry);
            foreach (var child in entry.Children)
            {
                Walk(child, target);
            }
        }
    }

    // ── 解析辅助 ─────────────────────────────────────────────────────

    private static void Attach(YamlConfigDocument document, ConfigEntry? parent, ConfigEntry entry, List<string> pendingComments)
    {
        if (pendingComments.Count > 0)
        {
            entry.Comment = string.Join("\n", pendingComments).Trim();
            pendingComments.Clear();
        }

        if (parent is null)
        {
            document.Roots.Add(entry);
        }
        else
        {
            entry.Parent = parent;
            parent.Children.Add(entry);
        }
    }

    private static void InitializeValue(ConfigEntry entry, string raw)
    {
        entry.RawValue = raw.Trim();
        if (entry.RawValue.Length >= 2 &&
            (entry.RawValue[0] == '\'' || entry.RawValue[0] == '"') &&
            entry.RawValue[^1] == entry.RawValue[0])
        {
            entry.QuoteChar = entry.RawValue[0];
            entry.OriginalValue = Unquote(entry.RawValue);
        }
        else
        {
            entry.QuoteChar = '\0';
            entry.OriginalValue = entry.RawValue;
        }

        entry.EditingValue = entry.OriginalValue;
    }

    private static int IndexOfFirstNonSpace(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is not (' ' or '\t'))
            {
                return i;
            }
        }

        return line.Length;
    }

    private static int MeasureIndent(string line, int contentStart)
    {
        var indent = 0;
        for (var i = 0; i < contentStart; i++)
        {
            indent += line[i] == '\t' ? 2 : 1;
        }

        return indent;
    }

    private static bool IsSequenceIndicator(string content) =>
        content.Length >= 1 &&
        content[0] == '-' &&
        (content.Length == 1 || content[1] == ' ' || content[1] == '\t');

    /// <summary>找到 "key:" 里的那个冒号；URL、行内集合等不算。找不到返回 -1。</summary>
    private static int FindMappingColon(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            switch (ch)
            {
                case '\'':
                case '"':
                case '[':
                case '{':
                case '#':
                    return -1;
                case ':':
                    if (i == 0)
                    {
                        return -1;
                    }

                    if (i + 1 >= content.Length || content[i + 1] == ' ' || content[i + 1] == '\t')
                    {
                        return i;
                    }

                    // 冒号后没有空格（例如 http://）不是键值分隔
                    return -1;
            }
        }

        return -1;
    }

    private static void SplitInlineComment(string rest, out string value, out string suffix)
    {
        value = rest.TrimEnd();
        suffix = string.Empty;

        if (rest.Length == 0)
        {
            return;
        }

        var quote = '\0';
        var depth = 0;

        for (var i = 0; i < rest.Length; i++)
        {
            var ch = rest[i];

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '\'':
                case '"':
                    quote = ch;
                    continue;
                case '[':
                case '{':
                    depth++;
                    continue;
                case ']':
                case '}':
                    if (depth > 0)
                    {
                        depth--;
                    }

                    continue;
                case '#':
                    if (depth == 0 && (i == 0 || rest[i - 1] == ' ' || rest[i - 1] == '\t'))
                    {
                        var valueEnd = i;
                        while (valueEnd > 0 && (rest[valueEnd - 1] == ' ' || rest[valueEnd - 1] == '\t'))
                        {
                            valueEnd--;
                        }

                        value = rest[..valueEnd];
                        suffix = rest[valueEnd..];
                        return;
                    }

                    continue;
            }
        }
    }

    private static string CleanComment(string content)
    {
        var trimmed = content.TrimStart('#').Trim();
        return trimmed;
    }

    public static string Unquote(string raw)
    {
        if (raw.Length >= 2)
        {
            var quote = raw[0];
            if ((quote == '\'' || quote == '"') && raw[^1] == quote)
            {
                var inner = raw[1..^1];
                return quote == '\'' ? inner.Replace("''", "'") : inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
        }

        return raw;
    }
}
