using System.Text;
using System.Text.RegularExpressions;
using EasyHexo.Localization;
using EasyHexo.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EasyHexo.Views;

/// <summary>
/// 简单文本编辑器：显示配置文件全文，支持行号、Tab 缩进、基础 YAML 高亮、保存 / 撤销 / 重做。
/// </summary>
public sealed partial class TextEditorPage : Page
{
    private string _path = string.Empty;
    private bool _hadBom;
    private bool _usesCrLf;
    private bool _trailingNewline;
    private bool _suppressHighlight;

    private static readonly Regex KeyRegex = new(@"^(\s*)([\w.\-]+)(\s*:)", RegexOptions.Multiline);
    private static readonly Regex CommentRegex = new(@"#.*$", RegexOptions.Multiline);
    private static readonly Regex StringRegex = new("'[^'\\r\\n]*'|\"[^\"\\r\\n]*\"", RegexOptions.Multiline);

    public TextEditorPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        TryHookScroll();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _path = e.Parameter as string ?? string.Empty;
        PathText.Text = _path;
        _ = LoadFileAsync();
    }

    private async Task LoadFileAsync()
    {
        if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
        {
            ShowMessage(InfoBarSeverity.Error, Lang.T("textEditor.loadFailed", _path));
            return;
        }

        try
        {
            var path = _path;
            var (text, hadBom) = await Task.Run(() =>
            {
                var content = TextFileService.ReadAllText(path);
                return (content, TextFileService.HasUtf8Bom(path));
            });

            _hadBom = hadBom;
            _usesCrLf = text.Contains("\r\n");
            _trailingNewline = text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal);

            Editor.Document.SetText(TextSetOptions.None, text);
            UpdateLineNumbers();
            ApplyHighlight();
            TryHookScroll();
        }
        catch (Exception ex)
        {
            AppLog.Write("文本编辑器读取失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("textEditor.loadFailed", ex.Message));
        }
    }

    private void TryHookScroll()
    {
        var scrollViewer = FindDescendant<ScrollViewer>(Editor);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ViewChanged += (_, _) =>
        {
            GutterScroll.ChangeView(null, scrollViewer.VerticalOffset, null, true);
        };
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private string GetEditorText()
    {
        Editor.Document.GetText(TextGetOptions.None, out var text);
        return text;
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        UpdateLineNumbers();
        if (!_suppressHighlight)
        {
            ApplyHighlight();
        }
    }

    private void UpdateLineNumbers()
    {
        var text = GetEditorText();
        var lineCount = 1;
        foreach (var ch in text)
        {
            if (ch == '\r')
            {
                lineCount++;
            }
        }

        var builder = new StringBuilder();
        for (var i = 1; i <= lineCount; i++)
        {
            builder.AppendLine(i.ToString());
        }

        LineNumbers.Text = builder.ToString().TrimEnd();
    }

    private void ApplyHighlight()
    {
        var text = GetEditorText();
        var isDark = ActualTheme == ElementTheme.Dark;
        var baseColor = isDark ? Color.FromArgb(255, 230, 230, 230) : Color.FromArgb(255, 30, 30, 30);
        var keyColor = isDark ? Color.FromArgb(255, 86, 156, 214) : Color.FromArgb(255, 0, 70, 180);
        var commentColor = isDark ? Color.FromArgb(255, 106, 153, 85) : Color.FromArgb(255, 0, 128, 0);
        var stringColor = isDark ? Color.FromArgb(255, 206, 145, 120) : Color.FromArgb(255, 163, 21, 21);

        _suppressHighlight = true;
        try
        {
            var all = Editor.Document.GetRange(0, text.Length);
            all.CharacterFormat.ForegroundColor = baseColor;

            foreach (Match match in KeyRegex.Matches(text))
            {
                var key = match.Groups[2];
                Editor.Document.GetRange(key.Index, key.Index + key.Length).CharacterFormat.ForegroundColor = keyColor;
            }

            foreach (Match match in StringRegex.Matches(text))
            {
                Editor.Document.GetRange(match.Index, match.Index + match.Length).CharacterFormat.ForegroundColor = stringColor;
            }

            foreach (Match match in CommentRegex.Matches(text))
            {
                Editor.Document.GetRange(match.Index, match.Index + match.Length).CharacterFormat.ForegroundColor = commentColor;
            }
        }
        finally
        {
            _suppressHighlight = false;
        }
    }

    private void OnEditorKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            e.Handled = true;
            Editor.Document.Selection.Text = "  ";
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        try
        {
            var text = GetEditorText();
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = text.TrimEnd('\n');
            if (_trailingNewline)
            {
                text += "\n";
            }

            var content = _usesCrLf ? text.Replace("\n", "\r\n") : text;
            var path = _path;
            await Task.Run(() =>
            {
                try
                {
                    File.Copy(path, path + ".easyhexo.bak", overwrite: true);
                }
                catch
                {
                    // 备份失败不阻止保存
                }

                File.WriteAllText(path, content, new UTF8Encoding(_hadBom));
            });

            ShowMessage(InfoBarSeverity.Success, Lang.T("textEditor.saved", _path));
        }
        catch (Exception ex)
        {
            AppLog.Write("文本编辑器保存失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("textEditor.saveFailed", ex.Message));
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Editor.Document.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Editor.Document.Redo();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame is null)
        {
            return;
        }

        // 直接回到一个新的配置页，避免 GoBack 恢复出空页面
        Frame.Navigate(typeof(ConfigEditorPage));
        Frame.BackStack.Clear();
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = severity switch
        {
            InfoBarSeverity.Success => Lang.T("common.success"),
            InfoBarSeverity.Warning => Lang.T("common.warning"),
            InfoBarSeverity.Error => Lang.T("common.error"),
            _ => Lang.T("common.info"),
        };
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
