using System.Collections.ObjectModel;
using System.Globalization;
using EasyHexo.Helpers;
using EasyHexo.Localization;
using EasyHexo.Models;
using EasyHexo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace EasyHexo.Views;

/// <summary>
/// 配置编辑页：_config.yml / 主题 _config.yml → 分区化可折叠列表 → 可视化编辑 → 应用到配置。
///
/// 支持：
/// * 按常用度分区的站点配置；主题配置按文件缩进直接展开（不翻译键名）；
/// * 布尔 / 选项 / 数字 / 目录等可视化编辑控件；
/// * 修改项高亮 + 撤回上一步；
/// * 识别 themes/ 并一键切换主题、编辑主题配置；
/// * 识别 / 更换站点图标（favicon），自动把图片复制进 source 并改写为相对路径。
/// </summary>
public sealed partial class ConfigEditorPage : Page
{
    private sealed class UndoStep
    {
        public UndoStep(ConfigEntry entry, string oldValue, string label)
        {
            Entry = entry;
            OldValue = oldValue;
            Label = label;
        }

        public ConfigEntry Entry { get; }

        public string OldValue { get; }

        public string Label { get; }
    }

    private readonly List<ConfigEntry> _entries = new();
    private readonly List<HexoTheme> _themes = new();
    private readonly List<string> _themeOptionNames = new();
    private readonly List<ConfigSectionLayout> _sections = new();
    private readonly HashSet<string> _collapsedSections = new(StringComparer.Ordinal);
    private readonly Stack<UndoStep> _undo = new();

    private YamlConfigDocument? _document;
    private string _configPath = string.Empty;
    private bool _translateKeys = true;
    private HexoTheme? _editingTheme;
    private FaviconInfo? _favicon;
    private bool _loadedOnce;
    private ConfigEntry? _focusEntry;
    private string _focusOldValue = string.Empty;
    private ComboBox? _focusedCombo;
    private bool _suppressHeroEvents;

    public ConfigEditorPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    /// <summary>列表当前显示的行（展开 / 收起 / 撤回时会重建）。</summary>
    public ObservableCollection<ConfigRow> Rows { get; } = new();

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await LoadAsync();
    }

    // ── 载入 ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        var expandedPaths = CollectExpandedPaths();

        _document = null;
        _entries.Clear();
        _sections.Clear();
        Rows.Clear();
        _undo.Clear();
        _focusEntry = null;
        UpdateUndoButton();

        var project = AppState.Current.Project;
        if (project is null)
        {
            HeroCard.Visibility = Visibility.Collapsed;
            ShowEmpty(Lang.T("root.none"), showPickButton: true);
            UpdateStats();
            UpdateThemeCard();
            UpdateFaviconCard();
            return;
        }

        if (_editingTheme is not null && !_editingTheme.HasConfig)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("config.theme.noConfig"));
            _editingTheme = null;
        }

        _translateKeys = _editingTheme is null;
        _configPath = _editingTheme?.ConfigPath ?? project.ConfigPath;

        if (!File.Exists(_configPath))
        {
            HeroCard.Visibility = Visibility.Collapsed;
            ShowEmpty(Lang.T("root.error.config"), showPickButton: true);
            UpdateStats();
            UpdateThemeCard();
            UpdateFaviconCard();
            return;
        }

        try
        {
            var path = _configPath;
            var (text, hadBom) = await Task.Run(() =>
            {
                var content = TextFileService.ReadAllText(path);
                return (content, TextFileService.HasUtf8Bom(path));
            });

            var document = ConfigYamlService.Parse(text);
            document.HadBom = hadBom;
            _document = document;
            _entries.AddRange(ConfigYamlService.Flatten(document.Roots));

            if (_translateKeys)
            {
                _sections.AddRange(ConfigLayoutService.BuildSections(document.Roots));
                // 站点信息已经移到顶部“基本信息”，列表里不再重复显示
                _sections.RemoveAll(section => section.Id == "site");

                var themes = await Task.Run(() => HexoThemeService.Detect(project));
                _themes.Clear();
                _themes.AddRange(themes);
                _themeOptionNames.Clear();
                _themeOptionNames.AddRange(_themes.Select(theme => theme.Name));
            }

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
            RebuildVisible(preserveScroll: false);
            UpdateThemeCard();
            UpdateFaviconCard();
            SyncHeroFromEntries();

            var message = Lang.T("config.loaded", _entries.Count);
            if (document.UnparsedLineCount > 0)
            {
                message += " " + Lang.T("config.unparsed", document.UnparsedLineCount);
            }

            if (_editingTheme is not null)
            {
                message = Lang.T("config.theme.modeLoaded", _editingTheme.RelativeConfigPath);
            }

            ShowMessage(InfoBarSeverity.Success, message);
        }
        catch (Exception ex)
        {
            AppLog.Write("载入配置文件失败", ex);
            ShowEmpty(Lang.T("config.loadError", ex.Message), showPickButton: false);
            UpdateStats();
        }
    }

    // ── 列表构建 ─────────────────────────────────────────────────────

    private void RebuildVisible(bool preserveScroll = true)
    {
        var offset = preserveScroll ? GetListVerticalOffset() : 0;
        Rows.Clear();

        if (_editingTheme is null && _sections.Count > 0)
        {
            foreach (var section in _sections)
            {
                var expanded = !_collapsedSections.Contains(section.Id);
                Rows.Add(ConfigRow.CreateSection(
                    section.Id,
                    Lang.T(section.TitleKey),
                    section.Entries.Count,
                    expanded));

                if (!expanded)
                {
                    continue;
                }

                foreach (var entry in section.Entries)
                {
                    AddRow(entry);
                }
            }
        }
        else if (_document is not null)
        {
            foreach (var root in _document.Roots)
            {
                AddRow(root);
            }
        }

        if (preserveScroll)
        {
            RestoreListOffset(offset);
        }

        UpdateStats();
    }

    private double GetListVerticalOffset()
    {
        var scrollViewer = FindDescendant<ScrollViewer>(ConfigListView);
        return scrollViewer?.VerticalOffset ?? 0;
    }

    private void RestoreListOffset(double offset)
    {
        if (offset <= 0.5)
        {
            return;
        }

        ConfigListView.UpdateLayout();
        FindDescendant<ScrollViewer>(ConfigListView)?.ChangeView(null, offset, null, true);

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        dispatcher?.TryEnqueue(() =>
        {
            ConfigListView.UpdateLayout();
            FindDescendant<ScrollViewer>(ConfigListView)?.ChangeView(null, offset, null, true);
        });
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

    private void AddRow(ConfigEntry entry)
    {
        Rows.Add(new ConfigRow(entry, BuildPath(entry), _translateKeys, _themeOptionNames));
        if (!entry.IsExpanded)
        {
            return;
        }

        foreach (var child in entry.Children)
        {
            AddRow(child);
        }
    }

    private void OnSectionToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConfigRow row ||
            !row.IsSection)
        {
            return;
        }

        if (!_collapsedSections.Remove(row.SectionId))
        {
            _collapsedSections.Add(row.SectionId);
        }

        RebuildVisible();
    }

    private void OnToggleExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConfigRow row ||
            row.Entry is not { HasChildren: true } entry)
        {
            return;
        }

        entry.IsExpanded = !entry.IsExpanded;
        RebuildVisible();
    }

    private void OnOpenWithClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_configPath) || !File.Exists(_configPath))
        {
            return;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _configPath,
                Verb = "openas",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            AppLog.Write("在其他应用打开失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("config.openFailed", ex.Message));
        }
    }

    private void OnOpenTextEditorClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_configPath) || !File.Exists(_configPath))
        {
            return;
        }

        Frame?.Navigate(typeof(TextEditorPage), _configPath);
    }

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        _collapsedSections.Clear();
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

        // 站点配置模式下把分区也收起，列表只剩分区标题，便于快速定位
        if (_editingTheme is null)
        {
            _collapsedSections.Clear();
            foreach (var section in _sections)
            {
                _collapsedSections.Add(section.Id);
            }
        }

        RebuildVisible();
    }

    // ── 编辑：文本 ───────────────────────────────────────────────────

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is ConfigRow { Entry: { } entry })
        {
            _focusEntry = entry;
            _focusOldValue = entry.EditingValue;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not ConfigRow row ||
            row.Entry is not { IsEditable: true } entry)
        {
            return;
        }

        if (row.Kind is not (ConfigEditorKind.Text or ConfigEditorKind.Directory))
        {
            return;
        }

        // 列表滚动/回收时模板会先把 Text 清空再赋值，未获得焦点的变化不是用户输入
        if (textBox.FocusState == FocusState.Unfocused)
        {
            return;
        }

        var value = textBox.Text ?? string.Empty;
        if (entry.EditingValue == value)
        {
            return;
        }

        entry.EditingValue = value;
        row.RefreshDirtyState();
        UpdateStats();
        UpdateUndoButton();
    }

    private void OnTextLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box &&
            box.DataContext is ConfigRow { Entry: { IsEditable: true } entry } row &&
            row.Kind is ConfigEditorKind.Text or ConfigEditorKind.Directory)
        {
            var text = box.Text ?? string.Empty;
            if (entry.EditingValue != text)
            {
                entry.EditingValue = text;
                row.RefreshDirtyState();
                UpdateStats();
            }
        }

        if (_focusEntry is not null && _focusEntry.EditingValue != _focusOldValue)
        {
            PushUndo(_focusEntry, _focusOldValue, _focusEntry.Key);
        }

        _focusEntry = null;
        UpdateUndoButton();
    }

    // ── 编辑：开关 / 下拉 / 数字 ─────────────────────────────────────

    private void OnBoolToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle &&
            toggle.DataContext is ConfigRow row &&
            row.Entry is { IsEditable: true } entry)
        {
            if (row.Kind != ConfigEditorKind.Bool)
            {
                return;
            }

            // 列表回收时 IsOn 会被重置，未获得焦点的变化不是用户操作
            if (toggle.FocusState == FocusState.Unfocused)
            {
                return;
            }

            var value = toggle.IsOn ? "true" : "false";
            if (entry.EditingValue == value)
            {
                return;
            }

            CommitValue(entry, value, row);
        }
    }

    private void OnComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo ||
            combo.DataContext is not ConfigRow row ||
            row.Entry is not { IsEditable: true } entry)
        {
            return;
        }

        if (row.Kind != ConfigEditorKind.Choice)
        {
            return;
        }

        if (combo.SelectedItem is string selected && entry.EditingValue != selected)
        {
            CommitValue(entry, selected, row);
        }
    }

    private void OnComboGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo &&
            combo.DataContext is ConfigRow row &&
            row.Kind == ConfigEditorKind.Choice &&
            row.Entry is not null)
        {
            _focusedCombo = combo;
        }
    }

    private void OnComboLostFocus(object sender, RoutedEventArgs e)
    {
        // 只有真正获得过焦点的下拉框才提交文本，避免列表重建时的伪 LostFocus 把值清空
        if (sender is not ComboBox combo || !ReferenceEquals(combo, _focusedCombo))
        {
            return;
        }

        _focusedCombo = null;

        if (combo.DataContext is not ConfigRow row ||
            row.Entry is not { IsEditable: true } entry)
        {
            return;
        }

        if (row.Kind != ConfigEditorKind.Choice)
        {
            return;
        }

        var text = (combo.Text ?? string.Empty).Trim();
        if (entry.EditingValue != text)
        {
            CommitValue(entry, text, row);
        }
    }

    private void OnNumberValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.FocusState == FocusState.Unfocused)
        {
            return;
        }

        if (sender.DataContext is not ConfigRow row || row.Kind != ConfigEditorKind.Number ||
            row.Entry is not { IsEditable: true } entry ||
            double.IsNaN(args.NewValue))
        {
            return;
        }

        var text = FormatNumber(args.NewValue);
        if (entry.EditingValue != text)
        {
            CommitValue(entry, text, row);
        }
    }

    private static string FormatNumber(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.000001
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private async void OnBrowseDirClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConfigRow row ||
            row.Entry is not { IsEditable: true } entry)
        {
            return;
        }

        var project = AppState.Current.Project;
        if (project is null)
        {
            return;
        }

        try
        {
            var picked = await WindowHelper.PickFolderAsync();
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            if (!PathHelper.AreSame(picked, project.RootPath) && !PathHelper.IsUnder(picked, project.RootPath))
            {
                ShowMessage(InfoBarSeverity.Warning, Lang.T("config.editor.dirOutside"));
                return;
            }

            var value = PathHelper.AreSame(picked, project.RootPath)
                ? "."
                : Path.GetRelativePath(project.RootPath, picked).Replace('\\', '/');

            CommitValue(entry, value, row);
        }
        catch (Exception ex)
        {
            AppLog.Write("选择目录失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("config.editor.dirPickFailed", ex.Message));
        }
    }

    // ── 撤回 ─────────────────────────────────────────────────────────

    private void CommitValue(ConfigEntry entry, string newValue, ConfigRow row)
    {
        CommitValue(entry, newValue, row.Key);
        row.RefreshDirtyState();
    }

    private void CommitValue(ConfigEntry entry, string newValue, string label)
    {
        var oldValue = entry.EditingValue;
        if (oldValue == newValue)
        {
            return;
        }

        entry.EditingValue = newValue;
        PushUndo(entry, oldValue, label);
        UpdateStats();
    }

    private void PushUndo(ConfigEntry entry, string oldValue, string label)
    {
        _undo.Push(new UndoStep(entry, oldValue, label));
        UpdateUndoButton();
    }

    /// <summary>文本框里刚输入、还没失焦的修改也算一步可撤回的改动。</summary>
    private bool HasPendingTextChange =>
        _focusEntry is not null && _focusEntry.EditingValue != _focusOldValue;

    private void UpdateUndoButton() => UndoButton.IsEnabled = _undo.Count > 0 || HasPendingTextChange;

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        // 正在输入、尚未失焦的文本框：直接把该框恢复成编辑前的值
        if (HasPendingTextChange)
        {
            var pendingEntry = _focusEntry!;
            var label = pendingEntry.Key;
            pendingEntry.EditingValue = _focusOldValue;
            _focusEntry = null;
            RebuildVisible();
            SyncHeroFromEntries();
            UpdateUndoButton();
            ShowMessage(InfoBarSeverity.Informational, Lang.T("config.undone", label));
            return;
        }

        if (_undo.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Informational, Lang.T("config.undoEmpty"));
            return;
        }

        var step = _undo.Pop();
        step.Entry.EditingValue = step.OldValue;
        _focusEntry = null;
        RebuildVisible();
        SyncHeroFromEntries();
        UpdateUndoButton();
        ShowMessage(InfoBarSeverity.Informational, Lang.T("config.undone", step.Label));
    }

    // ── 统计 / 应用 / 重载 ───────────────────────────────────────────

    private void UpdateStats()
    {
        var editable = _entries.Count(entry => entry.IsEditable);
        var dirty = _entries.Count(entry => entry.IsDirty);

        StatsText.Text = _entries.Count == 0
            ? string.Empty
            : Lang.T("config.stats", editable, dirty);
        ApplyButton.IsEnabled = dirty > 0;
        UpdateHeroDirtyState();
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var document = _document;
        if (document is null)
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
            var path = _configPath;
            var result = await Task.Run(() => ConfigYamlService.Apply(document, changed, path));
            var backup = string.IsNullOrEmpty(result.BackupPath) ? "-" : result.BackupPath;
            await LoadAsync();
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

        await LoadAsync();
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

        _editingTheme = null;
        await LoadAsync();
    }

    // ── 主题 ─────────────────────────────────────────────────────────

    private void UpdateThemeCard()
    {
        var inThemeMode = _editingTheme is not null;
        var names = _themeOptionNames;

        ThemeCombo.ItemsSource = names;
        var current = _document is null
            ? null
            : HexoThemeService.ReadCurrentThemeName(_document);

        if (inThemeMode)
        {
            ThemeCombo.SelectedItem = _editingTheme!.Name;
        }
        else
        {
            ThemeCombo.SelectedItem = names.FirstOrDefault(name =>
                                        string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                                    ?? (names.Count > 0 ? names[0] : null);
        }

        ThemeModeText.Visibility = inThemeMode ? Visibility.Visible : Visibility.Collapsed;
        ThemeModeText.Text = inThemeMode
            ? Lang.T("config.theme.mode", _editingTheme!.RelativeConfigPath)
            : string.Empty;

        BackToSiteButton.Visibility = inThemeMode ? Visibility.Visible : Visibility.Collapsed;
        ThemeCombo.IsEnabled = !inThemeMode;
        SwitchThemeButton.IsEnabled = !inThemeMode && names.Count > 0;

        var selected = SelectedTheme();
        EditThemeConfigButton.IsEnabled = !inThemeMode && selected?.HasConfig == true;

        ThemeHintText.Text = names.Count == 0 ? Lang.T("config.theme.noThemes") : string.Empty;
        ThemeHintText.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private HexoTheme? SelectedTheme()
    {
        var name = ThemeCombo.SelectedItem as string;
        return string.IsNullOrEmpty(name)
            ? null
            : _themes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnThemeComboChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedTheme();
        EditThemeConfigButton.IsEnabled = _editingTheme is null && selected?.HasConfig == true;
    }

    private async void OnSwitchThemeClick(object sender, RoutedEventArgs e)
    {
        var project = AppState.Current.Project;
        var document = _document;
        if (project is null || document is null || _editingTheme is not null)
        {
            return;
        }

        var name = ThemeCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("config.theme.notSelected"));
            return;
        }

        var themeEntry = document.Roots.FirstOrDefault(entry =>
            entry.Key.Equals("theme", StringComparison.Ordinal) && entry.IsEditable);
        if (themeEntry is null)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("config.theme.none"));
            return;
        }

        var pending = CapturePendingEdits();
        themeEntry.EditingValue = name;
        SwitchThemeButton.IsEnabled = false;

        try
        {
            var path = _configPath;
            await Task.Run(() => ConfigYamlService.Apply(document, new[] { themeEntry }, path));
            await LoadAsync();
            RestorePendingEdits(pending, "theme");
            ShowMessage(InfoBarSeverity.Success, Lang.T("config.theme.switched", name));
        }
        catch (Exception ex)
        {
            AppLog.Write("切换主题失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("config.theme.switchFailed", ex.Message));
        }
        finally
        {
            UpdateThemeCard();
        }
    }

    private async void OnEditThemeConfigClick(object sender, RoutedEventArgs e)
    {
        var theme = SelectedTheme();
        if (theme is null)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("config.theme.notSelected"));
            return;
        }

        if (!theme.HasConfig)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("config.theme.noConfig"));
            return;
        }

        var dirty = _entries.Count(entry => entry.IsDirty);
        if (dirty > 0 &&
            !await DialogHelper.ConfirmAsync(
                Lang.T("config.discardTitle"),
                Lang.T("config.theme.discard", dirty),
                Lang.T("config.discardConfirm"),
                Lang.T("common.cancel")))
        {
            return;
        }

        _editingTheme = theme;
        _collapsedSections.Clear();
        await LoadAsync();
    }

    private async void OnBackToSiteClick(object sender, RoutedEventArgs e)
    {
        var dirty = _entries.Count(entry => entry.IsDirty);
        if (dirty > 0 &&
            !await DialogHelper.ConfirmAsync(
                Lang.T("config.discardTitle"),
                Lang.T("config.theme.discard", dirty),
                Lang.T("config.discardConfirm"),
                Lang.T("common.cancel")))
        {
            return;
        }

        _editingTheme = null;
        _collapsedSections.Clear();
        await LoadAsync();
    }

    // ── 站点图标 ─────────────────────────────────────────────────────

    private void UpdateFaviconCard()
    {
        var project = AppState.Current.Project;
        var document = _document;
        if (project is null || document is null)
        {
            _favicon = null;
            FaviconValueText.Text = string.Empty;
            ChangeFaviconButton.IsEnabled = false;
            FaviconImage.Source = null;
            FaviconImage.Visibility = Visibility.Collapsed;
            FaviconPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        ChangeFaviconButton.IsEnabled = true;
        _favicon = FaviconService.Find(document, project);

        if (!_favicon.HasEntry || string.IsNullOrWhiteSpace(_favicon.Value))
        {
            FaviconValueText.Text = Lang.T("config.favicon.none");
        }
        else if (_favicon.LocalPath is null)
        {
            FaviconValueText.Text = Lang.T("config.favicon.current", _favicon.Value)
                                    + "  ·  " + Lang.T("config.favicon.unknown");
        }
        else
        {
            FaviconValueText.Text = Lang.T("config.favicon.current", _favicon.Value);
        }

        UpdateFaviconPreviewAsync(_favicon);
    }

    private async void UpdateFaviconPreviewAsync(FaviconInfo info)
    {
        if (info.IsImage && info.LocalPath is not null)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(info.LocalPath);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                FaviconImage.Source = bitmap;
                FaviconImage.Visibility = Visibility.Visible;
                FaviconPlaceholder.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception ex)
            {
                AppLog.Write("加载图标预览失败", ex);
            }
        }

        FaviconImage.Source = null;
        FaviconImage.Visibility = Visibility.Collapsed;
        FaviconPlaceholder.Visibility = Visibility.Visible;
    }

    private async void OnChangeFaviconClick(object sender, RoutedEventArgs e)
    {
        var project = AppState.Current.Project;
        var document = _document;
        if (project is null || document is null)
        {
            return;
        }

        try
        {
            var picked = await WindowHelper.PickImageFileAsync();
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            if (!FaviconService.IsImageFile(picked))
            {
                ShowMessage(InfoBarSeverity.Warning, Lang.T("config.favicon.notImage"));
                return;
            }

            var url = FaviconService.EnsureSiteCopy(project, picked, out var copied, out var destination);
            var pending = CapturePendingEdits();
            var entry = FaviconService.Find(document, project).Entry;

            string detail;
            var path = _configPath;
            if (entry is not null)
            {
                entry.EditingValue = url;
                await Task.Run(() => ConfigYamlService.Apply(document, new[] { entry }, path));
                detail = Lang.T("config.favicon.updated", url);
            }
            else
            {
                await Task.Run(() => ConfigYamlService.AppendTopLevelKey(path, "favicon", url));
                detail = Lang.T("config.favicon.added", url);
            }

            if (copied && destination is not null)
            {
                detail += "  " + Lang.T("config.favicon.copied", destination);
            }

            await LoadAsync();
            RestorePendingEdits(pending, "favicon");
            ShowMessage(InfoBarSeverity.Success, detail);
        }
        catch (Exception ex)
        {
            AppLog.Write("更换站点图标失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("config.favicon.pickFailed", ex.Message));
        }
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

    private Dictionary<string, string> CapturePendingEdits()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in _entries.Where(entry => entry.IsDirty))
        {
            result[BuildPath(entry)] = entry.EditingValue;
        }

        return result;
    }

    private void RestorePendingEdits(Dictionary<string, string> pending, params string[] excludeKeys)
    {
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (!entry.IsEditable)
            {
                continue;
            }

            var path = BuildPath(entry);
            if (excludeKeys.Contains(entry.Key, StringComparer.Ordinal) ||
                excludeKeys.Contains(path, StringComparer.Ordinal))
            {
                continue;
            }

            if (pending.TryGetValue(path, out var value))
            {
                entry.EditingValue = value;
            }
        }

        RebuildVisible();
    }

    private static string BuildPath(ConfigEntry entry)
    {
        var keys = new List<string>();
        for (var current = entry; current is not null; current = current.Parent)
        {
            keys.Insert(0, current.Key);
        }

        return string.Join(".", keys);
    }

    // ── 顶部基本信息 ─────────────────────────────────────────────────

    private ConfigEntry? FindEntry(string path) =>
        _entries.FirstOrDefault(entry => entry.IsEditable && BuildPath(entry) == path);

    private void SyncHeroFromEntries()
    {
        var siteMode = _editingTheme is null && _document is not null;
        HeroCard.Visibility = siteMode ? Visibility.Visible : Visibility.Collapsed;
        if (!siteMode)
        {
            return;
        }

        _suppressHeroEvents = true;
        try
        {
            SyncHeroText(HeroTitleBox, "title");
            SyncHeroText(HeroAuthorBox, "author");
            SyncHeroText(HeroDescriptionBox, "description");
            SyncHeroText(HeroKeywordsBox, "keywords");
            SyncHeroCombo(HeroLanguageCombo, "language");
            SyncHeroCombo(HeroTimezoneCombo, "timezone");
        }
        finally
        {
            _suppressHeroEvents = false;
        }

        UpdateHeroDirtyState();
    }

    private void SyncHeroText(TextBox box, string path)
    {
        var entry = FindEntry(path);
        box.IsEnabled = entry is not null;
        box.Text = entry?.EditingValue ?? string.Empty;
    }

    private void SyncHeroCombo(ComboBox combo, string path)
    {
        if (combo.ItemsSource is null)
        {
            combo.ItemsSource = ConfigEditorMetadata.Resolve(path, string.Empty, _themeOptionNames).Options;
        }

        var entry = FindEntry(path);
        combo.IsEnabled = entry is not null;
        combo.Text = entry?.EditingValue ?? string.Empty;
    }

    private void UpdateHeroDirtyState()
    {
        SetHeroDirtyDot(HeroTitleDirty, "title");
        SetHeroDirtyDot(HeroAuthorDirty, "author");
        SetHeroDirtyDot(HeroDescriptionDirty, "description");
        SetHeroDirtyDot(HeroKeywordsDirty, "keywords");
        SetHeroDirtyDot(HeroLanguageDirty, "language");
        SetHeroDirtyDot(HeroTimezoneDirty, "timezone");
    }

    private void SetHeroDirtyDot(TextBlock dot, string path)
    {
        var entry = FindEntry(path);
        dot.Visibility = entry?.IsDirty == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHeroTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHeroEvents || sender is not TextBox box || box.Tag is not string path)
        {
            return;
        }

        var entry = FindEntry(path);
        if (entry is null || entry.EditingValue == (box.Text ?? string.Empty))
        {
            return;
        }

        entry.EditingValue = box.Text ?? string.Empty;
        UpdateHeroDirtyState();
        UpdateStats();
        UpdateUndoButton();
    }

    private void OnHeroGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string path)
        {
            var entry = FindEntry(path);
            if (entry is not null)
            {
                _focusEntry = entry;
                _focusOldValue = entry.EditingValue;
            }
        }
    }

    private void OnHeroTextLostFocus(object sender, RoutedEventArgs e)
    {
        if (_focusEntry is not null && _focusEntry.EditingValue != _focusOldValue)
        {
            PushUndo(_focusEntry, _focusOldValue, _focusEntry.Key);
        }

        _focusEntry = null;
        UpdateUndoButton();
    }

    private void OnHeroComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHeroEvents || sender is not ComboBox combo || combo.Tag is not string path)
        {
            return;
        }

        if (combo.SelectedItem is not string selected)
        {
            return;
        }

        var entry = FindEntry(path);
        if (entry is null || entry.EditingValue == selected)
        {
            return;
        }

        CommitValue(entry, selected, entry.Key);
        UpdateHeroDirtyState();
    }

    private void OnHeroComboLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressHeroEvents || sender is not ComboBox combo || combo.Tag is not string path)
        {
            return;
        }

        var entry = FindEntry(path);
        if (entry is null)
        {
            return;
        }

        var text = (combo.Text ?? string.Empty).Trim();
        if (entry.EditingValue != text)
        {
            CommitValue(entry, text, entry.Key);
            UpdateHeroDirtyState();
        }
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


