using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EasyHexo.Models;

namespace EasyHexo.Services;

/// <summary>
/// Hexo 文章 front-matter 的识别与生成。
///
/// 识别规则（与需求一致）：
///   * 文件第一行必须是 ---；
///   * 向下找到下一个 ---；
///   * 两个 --- 之间解析 title / date / tags（支持行内数组与块序列），其余行原样保留；
///   * 不符合该结构时，整个文件都当作正文，由用户在界面填标题 / 日期 / 标签。
/// </summary>
public static partial class FrontMatterService
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd",
        "yyyy-MM-dd'T'HH:mm:ss",
    };

    public static FrontMatter Parse(string? content)
    {
        var result = new FrontMatter();
        if (content is null)
        {
            return result;
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            result.Body = normalized.TrimStart('\n');
            return result;
        }

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed is "---" or "...")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            // 只有开头一个 ---：不认为有 front-matter
            result.Body = normalized.TrimStart('\n');
            return result;
        }

        result.HasFrontMatter = true;

        for (var i = 1; i < end; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var match = KeyLineRegex().Match(raw);
            if (!match.Success)
            {
                result.ExtraLines.Add(raw);
                continue;
            }

            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();

            switch (key.ToLowerInvariant())
            {
                case "title":
                    result.Title = ConfigYamlService.Unquote(value).Trim();
                    break;
                case "date":
                    result.Date = ParseDate(value);
                    break;
                case "tags":
                case "tag":
                    ParseTags(value, lines, ref i, end, result.Tags);
                    break;
                default:
                    result.ExtraLines.Add(raw);
                    // 保留其后的缩进续行（例如 categories 的块序列）
                    while (i + 1 < end && lines[i + 1].Length > 0 &&
                           (lines[i + 1][0] == ' ' || lines[i + 1][0] == '\t'))
                    {
                        i++;
                        result.ExtraLines.Add(lines[i]);
                    }

                    break;
            }
        }

        var body = string.Join("\n", lines.Skip(end + 1));
        result.Body = body.TrimStart('\n');
        return result;
    }

    /// <summary>生成完整文章内容（front-matter + 正文）。</summary>
    public static string Compose(
        string title,
        DateTime date,
        IEnumerable<string>? tags,
        string? body,
        FrontMatter? original)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("title: ").Append(FormatScalar(title)).Append('\n');
        builder.Append("date: ")
            .Append(date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append('\n');

        var tagList = (tags ?? Enumerable.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tagList.Count > 0)
        {
            builder.Append("tags: [")
                .Append(string.Join(", ", tagList.Select(FormatFlowScalar)))
                .Append("]\n");
        }

        if (original is not null)
        {
            foreach (var line in original.ExtraLines)
            {
                builder.Append(line).Append('\n');
            }
        }

        builder.Append("---\n\n");

        var normalizedBody = (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\n');
        builder.Append(normalizedBody);
        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.Append('\n');
        }

        return builder.ToString();
    }

    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = ConfigYamlService.Unquote(value.Trim());
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
        {
            return offset.DateTime;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    // ── 内部实现 ─────────────────────────────────────────────────────

    private static void ParseTags(string value, string[] lines, ref int index, int end, List<string> tags)
    {
        if (value.StartsWith('['))
        {
            var inner = value.Trim();
            if (inner.EndsWith(']'))
            {
                inner = inner[1..^1];
            }

            foreach (var part in inner.Split(','))
            {
                var tag = ConfigYamlService.Unquote(part.Trim());
                if (tag.Length > 0)
                {
                    tags.Add(tag);
                }
            }

            return;
        }

        if (value.Length > 0)
        {
            var single = ConfigYamlService.Unquote(value);
            if (single.Length > 0)
            {
                tags.Add(single);
            }

            return;
        }

        // tags: 后面的块序列
        while (index + 1 < end)
        {
            var next = lines[index + 1];
            var trimmed = next.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                index++;
                var tag = ConfigYamlService.Unquote(trimmed[2..].Trim());
                if (tag.Length > 0)
                {
                    tags.Add(tag);
                }
            }
            else if (trimmed.Length == 0)
            {
                index++;
            }
            else
            {
                break;
            }
        }
    }

    private static string FormatScalar(string value)
    {
        value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (value.Length == 0)
        {
            return "''";
        }

        return NeedsQuotes(value) ? "'" + value.Replace("'", "''") + "'" : value;
    }

    private static string FormatFlowScalar(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "''";
        }

        if (NeedsQuotes(value) || value.Contains(',') || value.Contains('[') || value.Contains(']'))
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        return value;
    }

    private static bool NeedsQuotes(string value)
    {
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
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
               value.Contains(" #", StringComparison.Ordinal) ||
               value.EndsWith(':');
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z_][A-Za-z0-9_\-]*)\s*:\s*(?<value>.*)$")]
    private static partial Regex KeyLineRegex();
}
