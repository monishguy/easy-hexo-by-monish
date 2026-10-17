using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo.Localization;

/// <summary>
/// 让 XAML 里的静态文案可以被运行时语言切换刷新的附加属性。
///
/// 用法：
///   &lt;TextBlock l:Trans.Text="config.title"/&gt;
///   &lt;Button l:Trans.Content="common.reload"/&gt;
///   &lt;TextBox l:Trans.Header="publish.titleField" l:Trans.Placeholder="publish.tagsPlaceholder"/&gt;
///
/// 每次设置 key 时都会登记目标控件，<see cref="Lang.SetLanguage"/> 会重新套用全部登记项。
/// </summary>
public static class Trans
{
    private sealed class Registration
    {
        public Registration(DependencyObject target, string key, Action<DependencyObject, string> apply)
        {
            Target = new WeakReference<DependencyObject>(target);
            Key = key;
            Apply = apply;
        }

        public WeakReference<DependencyObject> Target { get; }

        public string Key { get; }

        public Action<DependencyObject, string> Apply { get; }
    }

    private static readonly List<Registration> Registrations = new();

    // ── Text：TextBlock.Text ──────────────────────────────────────────
    public static readonly DependencyProperty TextProperty = Register(
        "Text",
        static (target, value) =>
        {
            if (target is TextBlock textBlock)
            {
                textBlock.Text = value;
            }
        });

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    // ── Content：Button / NavigationViewItem / CheckBox 等 ContentControl ──
    public static readonly DependencyProperty ContentProperty = Register(
        "Content",
        static (target, value) =>
        {
            if (target is ContentControl contentControl)
            {
                contentControl.Content = value;
            }
        });

    public static string? GetContent(DependencyObject element) => (string?)element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, string? value) => element.SetValue(ContentProperty, value);

    // ── Header：TextBox / ComboBox / DatePicker / TimePicker 的 Header ──
    public static readonly DependencyProperty HeaderProperty = Register(
        "Header",
        static (target, value) =>
        {
            switch (target)
            {
                case TextBox textBox:
                    textBox.Header = value;
                    break;
                case ComboBox comboBox:
                    comboBox.Header = value;
                    break;
                case DatePicker datePicker:
                    datePicker.Header = value;
                    break;
                case TimePicker timePicker:
                    timePicker.Header = value;
                    break;
            }
        });

    public static string? GetHeader(DependencyObject element) => (string?)element.GetValue(HeaderProperty);

    public static void SetHeader(DependencyObject element, string? value) => element.SetValue(HeaderProperty, value);

    // ── Placeholder：TextBox / AutoSuggestBox 的占位文本 ───────────────
    public static readonly DependencyProperty PlaceholderProperty = Register(
        "Placeholder",
        static (target, value) =>
        {
            switch (target)
            {
                case TextBox textBox:
                    textBox.PlaceholderText = value;
                    break;
                case AutoSuggestBox autoSuggestBox:
                    autoSuggestBox.PlaceholderText = value;
                    break;
            }
        });

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    // ── ToolTip：任意 FrameworkElement 的工具提示 ─────────────────────
    public static readonly DependencyProperty ToolTipProperty = Register(
        "ToolTip",
        static (target, value) =>
        {
            if (target is FrameworkElement element)
            {
                ToolTipService.SetToolTip(element, value);
            }
        });

    public static string? GetToolTip(DependencyObject element) => (string?)element.GetValue(ToolTipProperty);

    public static void SetToolTip(DependencyObject element, string? value) => element.SetValue(ToolTipProperty, value);

    /// <summary>语言变化后重新套用所有已登记的文案。</summary>
    public static void ReapplyAll()
    {
        for (var i = Registrations.Count - 1; i >= 0; i--)
        {
            var registration = Registrations[i];
            if (!registration.Target.TryGetTarget(out var target))
            {
                Registrations.RemoveAt(i);
                continue;
            }

            registration.Apply(target, Lang.T(registration.Key));
        }
    }

    private static DependencyProperty Register(string name, Action<DependencyObject, string> apply)
    {
        return DependencyProperty.RegisterAttached(
            name,
            typeof(string),
            typeof(Trans),
            new PropertyMetadata(null, (d, e) => OnChanged(d, e.NewValue, apply)));
    }

    private static void OnChanged(DependencyObject target, object? newValue, Action<DependencyObject, string> apply)
    {
        if (newValue is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var value = Lang.T(key);
        apply(target, value);
        AddRegistration(target, key, apply);
    }

    private static void AddRegistration(DependencyObject target, string key, Action<DependencyObject, string> apply)
    {
        // 防止同一个控件的同一个附加属性被重复登记。
        foreach (var registration in Registrations)
        {
            if (registration.Key == key &&
                ReferenceEquals(registration.Apply, apply) &&
                registration.Target.TryGetTarget(out var existing) &&
                ReferenceEquals(existing, target))
            {
                return;
            }
        }

        Registrations.Add(new Registration(target, key, apply));
    }
}
