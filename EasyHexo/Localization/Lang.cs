namespace EasyHexo.Localization;

/// <summary>
/// 运行时语言服务。切换语言时：先更新当前字典，再刷新所有通过
/// <see cref="Trans"/> 附加属性绑定到 XAML 的文案，最后通知订阅者。
/// </summary>
public static class Lang
{
    public const string ZhCn = "zh-CN";
    public const string EnUs = "en-US";

    private static string _current = ZhCn;

    /// <summary>当前语言标识（"zh-CN" / "en-US"）。</summary>
    public static string Current => _current;

    /// <summary>语言变化事件（界面上的动态文案可订阅它刷新）。</summary>
    public static event EventHandler? Changed;

    /// <summary>取文案；找不到 key 时返回 key 本身，方便排查漏配。</summary>
    public static string T(string key)
    {
        var map = _current == EnUs ? StringsEnUs.Map : StringsZhCn.Map;
        return map.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>取文案并格式化，例如 T("publish.postCount", 3)。</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public static void SetLanguage(string? language)
    {
        var normalized = Normalize(language);
        if (normalized == _current)
        {
            return;
        }

        _current = normalized;
        Trans.ReapplyAll();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return ZhCn;
        }

        return language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? EnUs : ZhCn;
    }
}
