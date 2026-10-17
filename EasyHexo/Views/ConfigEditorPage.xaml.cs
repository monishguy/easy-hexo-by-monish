using System.Collections.ObjectModel;
using EasyHexo.Helpers;
using EasyHexo.Localization;
using EasyHexo.Models;
using EasyHexo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo.Views;

/// <summary>
/// 配置编辑页：_config.yml → 可折叠的分层列表 → 修改 → 应用到配置。
/// </summary>
public sealed partial class ConfigEditorPage : Page
{
    private readonly List<ConfigEntry> _roots = new();
    private readonly List<ConfigEntry> _entries = new();
    private YamlConfigDocument? _document;
    private bool _loadedOnce;

    public ConfigEditorPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    /// <summary>列表当前显示的行（展开状态变化时会重建）。</summary>
    public ObservableCollection<ConfigRow> Rows { get; } = new();

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await LoadConfigAsync();
    }

    // ── 载入 ─────────────────────────────────────────────────────────

    private async Task LoadConfigAsync()
    {
        var expandedPaths = CollectExpandedPaths();

        _document = null;
        _roots.Clear();
        _entries.Clear();
        Rows.Clear();

        var project = AppState.Current.Project;
        if (project is null)
        {
            ShowEmpty(Lang.T("root.none"), showPickButton: true);
            UpdateStats();
            return;
        }

        if (!File.Exists(project.ConfigPath))
        {
            ShowEmpty(Lang.T("root.error.config"), showPickButton: true);
            UpdateStats();
            return;
        }

        try
        {
            var configPath = project.ConfigPath;
            var (text, hadBom) = await Task.Run(() =>
            {
                var content = TextFileService.ReadAllText(configPath);
                return (content, TextFileService.HasUtf8Bom(configPath));
            });

            var document = ConfigYamlService.Parse(text);
            document.HadBom = hadBom;

            _document = document;
            _roots.AddRange(document.Roots);
            _entries.AddRange(ConfigYamlService.Flatten(document.Roots));

            // 顶层分组默认展开；重新载入时保留原来的展开状态
            foreach (var entry in _entries)
            {
                if (!entry.HasChildren)
                {
                    continue;
                }

                entry.IsExpanded = expandedPaths.Count > 0
                    ? expandedPaths.Contains(BuildPath(entry))
                    : entry.Depth == 0;
            }

            EmptyPanel.Visibility = Visibility.Collapsed;
            RebuildVisible();

            var message = Lang.T("config.loaded", _entries.Count);
            if (document.UnparsedLineCount > 0)
            {
                message += " " + Lang.T("config.unparsed", document.UnparsedLineCount);
            }

            ShowMessage(InfoBarSeverity.Success, message);
        }
        catch (Exception ex)
        {
            AppLog.Write("载入 _config.yml 失败", ex);
            ShowEmpty(Lang.T("config.loadError", ex.Message), showPickButton: false);
            UpdateStats();
        }
    }

    // ── 列表构建 ─────────────────────────────────────────────────────

    private void RebuildVisible()
    {
        Rows.Clear();
        foreach (var root in _roots)
        {
            AddRow(root);
        }

        UpdateStats();
    }

    private void AddRow(ConfigEntry entry)
    {
        Rows.Add(new ConfigRow(entry));
        if (!entry.IsExpanded)
        {
            return;
        }

        foreach (var child in entry.Children)
        {
            AddRow(child);
        }
    }

    private void OnToggleExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConfigRow row ||
            !row.Entry.HasChildren)
        {
            return;
        }

        row.Entry.IsExpanded = !row.Entry.IsExpanded;
        RebuildVisible();
    }

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries)
        {
            if (entry.HasChildren)
            {
                entry.IsExpanded = true;
            }
        }

        RebuildVisible();
    }

    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries)
        {
            entry.IsExpanded = false;
        }

        RebuildVisible();
    }

    // ── 编辑 ─────────────────────────────────────────────────────────

    private void OnValueTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not ConfigRow row ||
            !row.Entry.IsEditable)
        {
            return;
        }

        var value = textBox.Text ?? string.Empty;
        if (row.Entry.EditingValue == value)
        {
            return;
        }

        row.Entry.EditingValue = value;
        UpdateStats();
    }

    private void UpdateStats()
    {
        var editable = _entries.Count(entry => entry.IsEditable);
        var dirty = _entries.Count(entry => entry.IsDirty);

        StatsText.Text = _entries.Count == 0
            ? string.Empty
            : Lang.T("config.stats", editable, dirty);
        ApplyButton.IsEnabled = dirty > 0;
    }

    // ── 操作 ─────────────────────────────────────────────────────────

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var project = AppState.Current.Project;
        var document = _document;
        if (project is null || document is null)
        {
            return;
        }

        var changed = _entries.Where(entry => entry.IsDirty).ToList();
        if (changed.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Informational, Lang.T("config.noChanges"));
            return;
        }

        ApplyButton.IsEnabled = false;
        try
        {
            var configPath = project.ConfigPath;
            var result = await Task.Run(() => ConfigYamlService.Apply(document, changed, configPath));
            var backup = string.IsNullOrEmpty(result.BackupPath) ? "-" : result.BackupPath;
            await LoadConfigAsync();
            ShowMessage(InfoBarSeverity.Success, Lang.T("config.applied", result.AppliedCount, backup));
        }
        catch (Exception ex)
        {
            AppLog.Write("应用配置失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("config.applyFailed", ex.Message));
            ApplyButton.IsEnabled = _entries.Any(entry => entry.IsDirty);
        }
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        var dirty = _entries.Count(entry => entry.IsDirty);
        if (dirty > 0 &&
            !await DialogHelper.ConfirmAsync(
                Lang.T("config.discardTitle"),
                Lang.T("config.discardMessage", dirty),
                Lang.T("config.discardConfirm"),
                Lang.T("common.cancel")))
        {
            return;
        }

        await LoadConfigAsync();
    }

    private async void OnPickRootClick(object sender, RoutedEventArgs e)
    {
        var path = await WindowHelper.PickFolderAsync();
        if (path is null)
        {
            return;
        }

        if (!AppState.Current.TrySetRoot(path, out var errorKey))
        {
            ShowMessage(
                InfoBarSeverity.Error,
                string.IsNullOrEmpty(errorKey) ? Lang.T("root.invalid") : Lang.T(errorKey));
            return;
        }

        await LoadConfigAsync();
    }

    // ── 辅助 ─────────────────────────────────────────────────────────

    private HashSet<string> CollectExpandedPaths()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            if (entry.HasChildren && entry.IsExpanded)
            {
                result.Add(BuildPath(entry));
            }
        }

        return result;
    }

    private static string BuildPath(ConfigEntry entry)
    {
        var parts = new List<string>();
        for (var current = entry; current is not null; current = current.Parent)
        {
            parts.Insert(0, current.Key);
        }

        return string.Join("/", parts);
    }

    private void ShowEmpty(string message, bool showPickButton)
    {
        EmptyMessageText.Text = message;
        EmptyPanel.Visibility = Visibility.Visible;
        PickRootButton.Visibility = showPickButton ? Visibility.Visible : Visibility.Collapsed;
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
