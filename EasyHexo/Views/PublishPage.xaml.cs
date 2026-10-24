using System.Collections.ObjectModel;
using EasyHexo.Helpers;
using EasyHexo.Localization;
using EasyHexo.Models;
using EasyHexo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace EasyHexo.Views;

/// <summary>
/// 发布文章页。
///
/// * 左侧“待发布”列表保存导入的 .md / .txt（最新导入的在最上面），可逐个选中编辑；
///   切换到别的文章时未发布的修改会保存成草稿（标题 / 日期 / 标签）；
/// * 左侧“已发布文章”按修改时间从新到旧，选中可重新编辑发布；
/// * 资源文件处理按 图片 / 文件 / 未识别项 分组，每项可勾选是否复制并改写链接，
///   未识别项可以“浏览”指定本机文件；图片 / 文件组的勾选状态会被记住；
/// * 点击“发布文章”才真正写入 _posts，并把勾选的资源复制到 source\标题\。
/// </summary>
public sealed partial class PublishPage : Page
{
    private sealed class EditingTarget
    {
        public string SourcePath { get; init; } = string.Empty;

        public bool IsTemporary { get; init; }

        public bool IsPublished { get; init; }

        public FrontMatter FrontMatter { get; init; } = new();

        public string Body { get; init; } = string.Empty;
    }

    private readonly Dictionary<string, DraftState> _publishedDrafts = new(StringComparer.OrdinalIgnoreCase);
    private EditingTarget? _editing;
    private ImportDraft? _currentDraft;
    private bool _suppressListSelection;
    private bool _suppressAssetEvents;
    private bool _suppressFormEvents;
    private bool _loadedOnce;
    private bool _busy;

    public PublishPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    public ObservableCollection<ArticleInfo> Posts { get; } = new();

    public ObservableCollection<ImportDraft> Drafts { get; } = new();

    public ObservableCollection<TagItem> Tags { get; } = new();

    public ObservableCollection<AssetGroupVm> AssetGroups { get; } = new();

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;

        DateSelector.Date = new DateTimeOffset(DateTime.Today);
        TimeSelector.SelectedTime = DateTime.Now.TimeOfDay;
        SourcePathText.Text = Lang.T("publish.noFile");

        UpdateDraftsSummary();
        BuildAssetGroups(null);
        await LoadPostsAsync();
    }

    // ── 已发布文章列表 ───────────────────────────────────────────────

    private async Task LoadPostsAsync()
    {
        var project = AppState.Current.Project;
        Posts.Clear();

        if (project is null)
        {
            PostsCountText.Text = Lang.T("root.none");
            return;
        }

        try
        {
            var posts = await Task.Run(() => ArticleService.LoadPosts(project));
            foreach (var post in posts)
            {
                Posts.Add(post);
            }

            PostsCountText.Text = posts.Count == 0
                ? Lang.T("publish.postsEmpty")
                : Lang.T("publish.postCount", posts.Count);
        }
        catch (Exception ex)
        {
            AppLog.Write("载入已发布文章失败", ex);
            PostsCountText.Text = Lang.T("publish.postsEmpty");
        }
    }

    private async void OnRefreshPostsClick(object sender, RoutedEventArgs e) => await LoadPostsAsync();

    private async void OnPostSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressListSelection || _busy || e.AddedItems.Count == 0 || e.AddedItems[0] is not ArticleInfo info)
        {
            return;
        }

        await SwitchToPostAsync(info);
    }

    private async void OnDraftSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressListSelection || _busy || e.AddedItems.Count == 0 || e.AddedItems[0] is not ImportDraft draft)
        {
            return;
        }

        SwitchToDraft(draft);
        await Task.CompletedTask;
    }

    private async Task SwitchToPostAsync(ArticleInfo info)
    {
        SaveCurrentEditorState();

        _suppressListSelection = true;
        try
        {
            DraftListView.SelectedItem = null;
        }
        finally
        {
            _suppressListSelection = false;
        }

        _currentDraft = null;
        var restore = _publishedDrafts.TryGetValue(info.FullPath, out var state) ? state : null;
        await LoadFromFileAsync(info.FullPath, restore);
    }

    private void SwitchToDraft(ImportDraft draft)
    {
        SaveCurrentEditorState();

        _suppressListSelection = true;
        try
        {
            PostsListView.SelectedItem = null;
        }
        finally
        {
            _suppressListSelection = false;
        }

        _currentDraft = draft;
        _editing = new EditingTarget
        {
            SourcePath = draft.SourcePath,
            IsTemporary = draft.IsTemporary,
            IsPublished = false,
            FrontMatter = draft.OriginalFrontMatter,
            Body = draft.Body,
        };

        ApplyForm(draft.State.Title, draft.State.Date, draft.State.Tags);
        SourcePathText.Text = draft.SourcePath;
        BuildAssetGroups(draft.Body);
        UpdatePublishState();

        var detail = Lang.T("publish.loaded", draft.SourcePath);
        if (draft.IsTemporary)
        {
            detail += "  " + Lang.T("publish.txtImported");
        }

        detail += "  " + (draft.OriginalFrontMatter.HasFrontMatter
            ? Lang.T("publish.frontMatterFound")
            : Lang.T("publish.frontMatterMissing"));
        ShowMessage(InfoBarSeverity.Informational, detail);
    }

    private async Task LoadFromFileAsync(string path, DraftState? restore)
    {
        var project = AppState.Current.Project;
        try
        {
            var (content, modified) = await Task.Run(() =>
            {
                var text = TextFileService.ReadAllText(path);
                var lastWrite = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
                return (text, lastWrite);
            });

            var frontMatter = FrontMatterService.Parse(content);
            _editing = new EditingTarget
            {
                SourcePath = path,
                IsTemporary = false,
                IsPublished = project is not null && PathHelper.IsUnder(path, project.PostsPath),
                FrontMatter = frontMatter,
                Body = frontMatter.Body,
            };

            var title = restore?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = string.IsNullOrWhiteSpace(frontMatter.Title)
                    ? Path.GetFileNameWithoutExtension(path)
                    : frontMatter.Title.Trim();
            }

            var date = restore?.Date ?? frontMatter.Date ?? modified;
            var tags = restore?.Tags ?? frontMatter.Tags;

            ApplyForm(title, date, tags);
            SourcePathText.Text = path;
            BuildAssetGroups(frontMatter.Body);
            UpdatePublishState();

            var detail = Lang.T("publish.loaded", path);
            detail += "  " + (frontMatter.HasFrontMatter
                ? Lang.T("publish.frontMatterFound")
                : Lang.T("publish.frontMatterMissing"));
            if (restore is not null && restore.IsModified)
            {
                detail += "  " + Lang.T("publish.draftRestored");
            }

            ShowMessage(InfoBarSeverity.Informational, detail);
        }
        catch (Exception ex)
        {
            AppLog.Write("载入文章失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.loadFailed", ex.Message));
        }
    }

    // ── 草稿状态（切换文章时保存 / 恢复表单） ────────────────────────

    private void SaveCurrentEditorState()
    {
        if (_editing is null || _suppressFormEvents)
        {
            return;
        }

        var state = CaptureFormState();

        if (_currentDraft is not null)
        {
            _currentDraft.State.Title = state.Title;
            _currentDraft.State.Date = state.Date;
            _currentDraft.State.Tags.Clear();
            _currentDraft.State.Tags.AddRange(state.Tags);
            _currentDraft.State.IsModified = state.IsModified;
            _currentDraft.NotifyStateChanged();
        }
        else if (_editing.IsPublished)
        {
            _publishedDrafts[_editing.SourcePath] = state;
        }
    }

    private DraftState CaptureFormState()
    {
        var state = new DraftState
        {
            Title = (TitleBox.Text ?? string.Empty).Trim(),
            Date = BuildDate(),
        };
        state.Tags.AddRange(Tags.Select(tag => tag.Name));
        state.IsModified = IsFormModified(state);
        return state;
    }

    private bool IsFormModified(DraftState state)
    {
        if (_editing is null)
        {
            return false;
        }

        var baselineTitle = string.IsNullOrWhiteSpace(_editing.FrontMatter.Title)
            ? Path.GetFileNameWithoutExtension(_editing.SourcePath)
            : _editing.FrontMatter.Title.Trim();
        if (!string.Equals(state.Title, baselineTitle, StringComparison.Ordinal))
        {
            return true;
        }

        var baselineDate = _editing.FrontMatter.Date
                           ?? (File.Exists(_editing.SourcePath) ? File.GetLastWriteTime(_editing.SourcePath) : DateTime.Now);
        if (Math.Abs((state.Date - baselineDate).TotalSeconds) >= 1)
        {
            return true;
        }

        if (state.Tags.Count != _editing.FrontMatter.Tags.Count)
        {
            return true;
        }

        for (var i = 0; i < state.Tags.Count; i++)
        {
            if (!string.Equals(state.Tags[i], _editing.FrontMatter.Tags[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyForm(string title, DateTime date, IEnumerable<string> tags)
    {
        _suppressFormEvents = true;
        try
        {
            TitleBox.Text = title;
            DateSelector.Date = new DateTimeOffset(date.Date);
            TimeSelector.SelectedTime = date.TimeOfDay;
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(new TagItem(tag));
            }
        }
        finally
        {
            _suppressFormEvents = false;
        }
    }

    // ── 文件拖放 / 浏览 ─────────────────────────────────────────────

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = await WindowHelper.PickArticleFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            await HandleFilesAsync(files);
        }
        catch (Exception ex)
        {
            AppLog.Write("打开文件选择器失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.loadFailed", ex.Message));
        }
    }

    private async Task HandleFilesAsync(IReadOnlyList<StorageFile> files)
    {
        var imported = new List<ImportDraft>();
        var skipped = 0;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension is not (".md" or ".txt"))
            {
                skipped++;
                continue;
            }

            try
            {
                string path;
                var isTemporary = false;

                if (extension == ".txt" || string.IsNullOrEmpty(file.Path))
                {
                    // .txt：复制到临时文件夹并改名为 .md，不改动用户原文件
                    path = await TempFileService.ImportAsMarkdownAsync(file);
                    isTemporary = true;
                }
                else
                {
                    path = file.Path;
                }

                var (content, modified) = await Task.Run(() =>
                {
                    var text = TextFileService.ReadAllText(path);
                    var lastWrite = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
                    return (text, lastWrite);
                });

                var frontMatter = FrontMatterService.Parse(content);
                var draft = new ImportDraft
                {
                    SourcePath = path,
                    IsTemporary = isTemporary,
                    OriginalFrontMatter = frontMatter,
                    Body = frontMatter.Body,
                };

                draft.State.Title = string.IsNullOrWhiteSpace(frontMatter.Title)
                    ? Path.GetFileNameWithoutExtension(path)
                    : frontMatter.Title.Trim();
                draft.State.Date = frontMatter.Date ?? modified;
                draft.State.Tags.AddRange(frontMatter.Tags);
                draft.State.IsModified = false;

                imported.Add(draft);
            }
            catch (Exception ex)
            {
                AppLog.Write($"导入文件失败：{file.Name}", ex);
                skipped++;
            }
        }

        // 最新导入的一批放在最上面（保持文件选择顺序），并默认显示第一篇
        for (var i = imported.Count - 1; i >= 0; i--)
        {
            Drafts.Insert(0, imported[i]);
        }

        UpdateDraftsSummary();

        if (imported.Count > 0)
        {
            var first = imported[0];
            _suppressListSelection = true;
            try
            {
                DraftListView.SelectedItem = first;
            }
            finally
            {
                _suppressListSelection = false;
            }

            SwitchToDraft(first);
            ShowMessage(InfoBarSeverity.Informational, Lang.T("publish.imported", imported.Count));
        }

        if (skipped > 0)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("publish.skipped", skipped));
        }
    }

    private void OnRemoveDraftClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ImportDraft draft)
        {
            return;
        }

        var wasCurrent = ReferenceEquals(draft, _currentDraft);
        Drafts.Remove(draft);
        UpdateDraftsSummary();

        if (wasCurrent)
        {
            ClearEditor(resetSelection: true);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = Lang.T("publish.dropCaption");
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || _busy)
        {
            return;
        }

        e.Handled = true;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().ToList();
            if (files.Count == 0)
            {
                return;
            }

            await HandleFilesAsync(files);
        }
        catch (Exception ex)
        {
            AppLog.Write("处理拖入文件失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.loadFailed", ex.Message));
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        ClearEditor(resetSelection: true);
    }

    private void ClearEditor(bool resetSelection)
    {
        // 清空表单前先把当前修改存进草稿，避免误清空丢内容
        SaveCurrentEditorState();

        if (resetSelection)
        {
            _suppressListSelection = true;
            try
            {
                DraftListView.SelectedItem = null;
                PostsListView.SelectedItem = null;
            }
            finally
            {
                _suppressListSelection = false;
            }
        }

        _currentDraft = null;
        _editing = null;
        ApplyForm(string.Empty, DateTime.Now, Array.Empty<string>());
        SourcePathText.Text = Lang.T("publish.noFile");
        BuildAssetGroups(null);
        UpdatePublishState();
    }

    // ── 标题 / 标签 ─────────────────────────────────────────────────

    private void OnTitleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFormEvents || _currentDraft is null)
        {
            return;
        }

        _currentDraft.State.Title = (TitleBox.Text ?? string.Empty).Trim();
        _currentDraft.NotifyStateChanged();
    }

    private void OnTagInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        CommitTag();
    }

    private void CommitTag()
    {
        var text = (TagInput.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        foreach (var part in text.Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = part.Trim();
            if (tag.Length == 0)
            {
                continue;
            }

            if (!Tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.OrdinalIgnoreCase)))
            {
                Tags.Add(new TagItem(tag));
            }
        }

        TagInput.Text = string.Empty;
        TagInput.Focus(FocusState.Programmatic);
    }

    private void OnRemoveTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TagItem tag)
        {
            Tags.Remove(tag);
        }
    }

    // ── 资源分组 ────────────────────────────────────────────────────

    private void BuildAssetGroups(string? body)
    {
        AssetGroups.Clear();
        _suppressAssetEvents = true;

        try
        {
            var links = AssetService.Scan(body);
            var imageItems = new List<AssetItemVm>();
            var fileItems = new List<AssetItemVm>();
            var unknownItems = new List<AssetItemVm>();

            var itemIndex = 0;
            foreach (var link in links)
            {
                var item = new AssetItemVm
                {
                    Url = link.Url,
                    DisplayName = link.DisplayName,
                    IsImage = link.Kind == AssetLinkKind.Image,
                    IsUnknown = link.Kind == AssetLinkKind.Unknown,
                    LocalPath = link.LocalPath,
                    AutomationId = $"asset-item-{itemIndex++}",
                };

                switch (link.Kind)
                {
                    case AssetLinkKind.Image:
                        imageItems.Add(item);
                        break;
                    case AssetLinkKind.File:
                        fileItems.Add(item);
                        break;
                    default:
                        unknownItems.Add(item);
                        break;
                }
            }

            AddGroupIfAny(CreateGroup(
                AssetGroupKind.Images,
                Lang.T("publish.assetsImages"),
                imageItems,
                AppState.Current.Settings.AssetImagesEnabled));

            AddGroupIfAny(CreateGroup(
                AssetGroupKind.Files,
                Lang.T("publish.assetsFiles"),
                fileItems,
                AppState.Current.Settings.AssetFilesEnabled));

            // 未识别项默认不勾选，且不记住设置
            AddGroupIfAny(CreateGroup(
                AssetGroupKind.Unknown,
                Lang.T("publish.assetsUnknown"),
                unknownItems,
                false));

            AssetsEmptyText.Visibility = AssetGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _suppressAssetEvents = false;
        }
    }

    private void AddGroupIfAny(AssetGroupVm group)
    {
        if (group.Items.Count > 0)
        {
            AssetGroups.Add(group);
        }
    }

    private static AssetGroupVm CreateGroup(
        AssetGroupKind kind,
        string title,
        List<AssetItemVm> items,
        bool defaultChecked)
    {
        var group = new AssetGroupVm
        {
            Kind = kind,
            Title = title,
            IsChecked = defaultChecked,
            AutomationId = "asset-group-" + kind,
        };

        foreach (var item in items)
        {
            item.IsChecked = defaultChecked;
            group.Items.Add(item);
        }

        return group;
    }

    private void OnAssetGroupCheck(object sender, RoutedEventArgs e)
    {
        if (_suppressAssetEvents ||
            sender is not CheckBox check ||
            check.DataContext is not AssetGroupVm group)
        {
            return;
        }

        // 用控件的实际状态（TwoWay 绑定更新顺序不确定），再同步回 ViewModel 与子项
        var state = check.IsChecked;
        group.SetCheckedSilently(state);
        foreach (var item in group.Items)
        {
            item.IsChecked = state;
        }

        if (group.Kind == AssetGroupKind.Images)
        {
            AppState.Current.SetAssetGroupRemembered(true, state == true);
        }
        else if (group.Kind == AssetGroupKind.Files)
        {
            AppState.Current.SetAssetGroupRemembered(false, state == true);
        }
    }

    private void OnAssetGroupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AssetGroupVm group)
        {
            group.IsExpanded = !group.IsExpanded;
        }
    }

    private async void OnAssetBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AssetItemVm item)
        {
            return;
        }

        try
        {
            var path = await WindowHelper.PickImageFileAsync(anyFile: true);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            item.LocalPath = path;
            item.IsChecked = true;
        }
        catch (Exception ex)
        {
            AppLog.Write("选择未识别资源失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.assetsPickFailed", ex.Message));
        }
    }

    private Dictionary<string, AssetSelection> BuildAssetSelections()
    {
        var selections = new Dictionary<string, AssetSelection>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in AssetGroups)
        {
            foreach (var item in group.Items)
            {
                var include = item.IsChecked == true;
                string? overridePath = item.IsUnknown ? item.LocalPath : null;

                // 未识别项：用户勾选了但没指定本机文件时，保持原样
                if (item.IsUnknown && include && string.IsNullOrEmpty(overridePath))
                {
                    include = false;
                }

                selections[item.Url] = new AssetSelection
                {
                    Include = include,
                    SourcePathOverride = overridePath,
                };
            }
        }

        return selections;
    }

    // ── 发布 ────────────────────────────────────────────────────────

    private async void OnPublishClick(object sender, RoutedEventArgs e) => await PublishAsync();

    private async Task PublishAsync()
    {
        if (_busy)
        {
            return;
        }

        var project = AppState.Current.Project;
        var editing = _editing;

        if (project is null)
        {
            ShowMessage(InfoBarSeverity.Error, Lang.T("root.none"));
            return;
        }

        if (editing is null)
        {
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.needFile"));
            return;
        }

        var title = (TitleBox.Text ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.needTitle"));
            TitleBox.Focus(FocusState.Programmatic);
            return;
        }

        SaveCurrentEditorState();

        var request = new PublishRequest
        {
            SourcePath = editing.SourcePath,
            IsTemporary = editing.IsTemporary || TempFileService.IsInSessionDirectory(editing.SourcePath),
            IsPublished = editing.IsPublished,
            Title = title,
            Date = BuildDate(),
            Tags = Tags.Select(tag => tag.Name).ToList(),
            Body = editing.Body,
            OriginalFrontMatter = editing.FrontMatter,
            AssetSelections = BuildAssetSelections(),
        };

        var targetPath = ArticlePublisher.ResolveTargetPath(project, request);
        if (!PathHelper.AreSame(targetPath, request.SourcePath) && File.Exists(targetPath))
        {
            var confirmed = await DialogHelper.ConfirmAsync(
                Lang.T("publish.overwriteTitle"),
                Lang.T("publish.overwriteMessage", targetPath),
                Lang.T("common.overwrite"),
                Lang.T("common.cancel"));
            if (!confirmed)
            {
                return;
            }
        }

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => ArticlePublisher.Publish(project, request));

            if (_currentDraft is not null)
            {
                Drafts.Remove(_currentDraft);
                _currentDraft = null;
            }

            _publishedDrafts.Remove(editing.SourcePath);
            await LoadPostsAsync();

            var detail = Lang.T("publish.success", result.TargetPath);
            if (result.AssetsCopied > 0)
            {
                detail += "  " + Lang.T("publish.assetsCopied", result.AssetsCopied, result.AssetFolder);
            }

            UpdateDraftsSummary();

            if (Drafts.Count > 0)
            {
                var next = Drafts[0];
                _suppressListSelection = true;
                try
                {
                    DraftListView.SelectedItem = next;
                }
                finally
                {
                    _suppressListSelection = false;
                }

                SwitchToDraft(next);
                detail += "  " + Lang.T("publish.nextDraft", Drafts.Count);
            }
            else
            {
                await LoadFromFileAsync(result.TargetPath, restore: null);
            }

            ShowMessage(InfoBarSeverity.Success, detail);
        }
        catch (Exception ex)
        {
            AppLog.Write("发布失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.failed", ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private DateTime BuildDate()
    {
        var selected = DateSelector.Date;
        var time = TimeSelector.SelectedTime ?? TimeSpan.Zero;
        return new DateTime(
            selected.Year,
            selected.Month,
            selected.Day,
            time.Hours,
            time.Minutes,
            time.Seconds,
            DateTimeKind.Local);
    }

    // ── 杂项 UI ─────────────────────────────────────────────────────

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdatePublishState();
    }

    private void UpdatePublishState() => PublishButton.IsEnabled = !_busy && _editing is not null;

    private void UpdateDraftsSummary()
    {
        DraftsCountText.Text = Drafts.Count == 0 ? string.Empty : Lang.T("publish.draftsCount", Drafts.Count);
        DraftsEmptyText.Visibility = Drafts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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


