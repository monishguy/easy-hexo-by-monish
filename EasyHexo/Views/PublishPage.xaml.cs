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
/// 支持：
/// * 浏览 / 拖入一个或多个 .md / .txt 文件（.txt 先复制到临时目录并改名为 .md；多选进入队列逐篇发布）；
/// * 识别文件开头的 front-matter，把 title / date / tags 填进各自的输入框；
/// * 标题、日期必填；日期用日期 + 时间选择器，写回时固定为 yyyy-MM-dd HH:mm:ss；
/// * 标签输入后按 Enter 锁定成标签块，可删除；
/// * 发布时把正文里本机绝对路径的图片 / 附件复制到 source\标题\ 并改写链接；
/// * 点击左侧已发布文章可重新编辑发布。
/// </summary>
public sealed partial class PublishPage : Page
{
    private sealed class EditingArticle
    {
        public string SourcePath { get; init; } = string.Empty;

        public bool IsTemporary { get; init; }

        public bool IsPublished { get; init; }

        public FrontMatter FrontMatter { get; init; } = new();

        public string OriginalContent { get; init; } = string.Empty;
    }

    private readonly Queue<(string Path, bool IsTemporary)> _pending = new();
    private EditingArticle? _editing;
    private bool _loadedOnce;
    private bool _busy;

    public PublishPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    public ObservableCollection<ArticleInfo> Posts { get; } = new();

    public ObservableCollection<TagItem> Tags { get; } = new();

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

        UpdateAssetsInfo();
        UpdateQueueHint();
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
        if (_busy || e.AddedItems.Count == 0 || e.AddedItems[0] is not ArticleInfo info)
        {
            return;
        }

        // 选择已发布文章时，放弃尚在队列中的外部文件
        _pending.Clear();
        UpdateQueueHint();
        await LoadArticleAsync(info.FullPath, isTemporary: false);
    }

    // ── 载入待编辑的文章 ─────────────────────────────────────────────

    private async Task LoadArticleAsync(string path, bool isTemporary)
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
            _editing = new EditingArticle
            {
                SourcePath = path,
                IsTemporary = isTemporary,
                IsPublished = project is not null && PathHelper.IsUnder(path, project.PostsPath),
                FrontMatter = frontMatter,
                OriginalContent = content,
            };

            TitleBox.Text = string.IsNullOrWhiteSpace(frontMatter.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : frontMatter.Title.Trim();

            // 需求：日期以文件本身的修改日期填入
            var date = frontMatter.Date ?? modified;
            DateSelector.Date = new DateTimeOffset(date.Date);
            TimeSelector.SelectedTime = date.TimeOfDay;

            Tags.Clear();
            foreach (var tag in frontMatter.Tags)
            {
                Tags.Add(new TagItem(tag));
            }

            SourcePathText.Text = path;
            UpdateAssetsInfo();
            UpdateQueueHint();

            var detail = Lang.T("publish.loaded", path);
            if (isTemporary)
            {
                detail += "  " + Lang.T("publish.txtImported");
            }

            detail += "  " + (frontMatter.HasFrontMatter
                ? Lang.T("publish.frontMatterFound")
                : Lang.T("publish.frontMatterMissing"));
            ShowMessage(InfoBarSeverity.Informational, detail);
        }
        catch (Exception ex)
        {
            AppLog.Write("载入文章失败", ex);
            ShowMessage(InfoBarSeverity.Error, Lang.T("publish.loadFailed", ex.Message));
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
        var skipped = 0;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension is not (".md" or ".txt"))
            {
                skipped++;
                continue;
            }

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

            _pending.Enqueue((path, isTemporary));
        }

        if (skipped > 0)
        {
            ShowMessage(InfoBarSeverity.Warning, Lang.T("publish.skipped", skipped));
        }

        if (_editing is null)
        {
            await LoadNextFromQueueAsync();
        }

        UpdateQueueHint();
    }

    private async Task LoadNextFromQueueAsync()
    {
        UpdateQueueHint();
        if (_pending.Count == 0)
        {
            return;
        }

        var (path, isTemporary) = _pending.Dequeue();
        await LoadArticleAsync(path, isTemporary);
        UpdateQueueHint();
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

        ClearEditor();
    }

    private void ClearEditor()
    {
        _editing = null;
        _pending.Clear();
        TitleBox.Text = string.Empty;
        Tags.Clear();
        DateSelector.Date = new DateTimeOffset(DateTime.Today);
        TimeSelector.SelectedTime = DateTime.Now.TimeOfDay;
        SourcePathText.Text = Lang.T("publish.noFile");
        UpdateQueueHint();
        UpdateAssetsInfo();
    }

    // ── 标题 / 标签 ─────────────────────────────────────────────────

    private void OnTitleTextChanged(object sender, TextChangedEventArgs e) => UpdateAssetsInfo();

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

        var request = new PublishRequest
        {
            SourcePath = editing.SourcePath,
            IsTemporary = editing.IsTemporary || TempFileService.IsInSessionDirectory(editing.SourcePath),
            IsPublished = editing.IsPublished,
            Title = title,
            Date = BuildDate(),
            Tags = Tags.Select(tag => tag.Name).ToList(),
            Body = editing.FrontMatter.Body,
            OriginalFrontMatter = editing.FrontMatter,
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
            await LoadPostsAsync();

            var detail = Lang.T("publish.success", result.TargetPath);
            if (result.AssetsCopied > 0)
            {
                detail += "  " + Lang.T("publish.assetsCopied", result.AssetsCopied, result.AssetFolder);
            }

            if (_pending.Count > 0)
            {
                await LoadNextFromQueueAsync();
                detail += "  " + Lang.T("publish.nextQueued", _pending.Count);
            }
            else
            {
                await LoadArticleAsync(result.TargetPath, isTemporary: false);
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
        PublishButton.IsEnabled = !busy;
    }

    private void UpdateQueueHint()
    {
        if (_pending.Count == 0)
        {
            QueueText.Text = string.Empty;
            QueueText.Visibility = Visibility.Collapsed;
            return;
        }

        QueueText.Text = Lang.T("publish.queue", _pending.Count);
        QueueText.Visibility = Visibility.Visible;
    }

    private void UpdateAssetsInfo()
    {
        var title = (TitleBox.Text ?? string.Empty).Trim();
        var folder = PathHelper.SanitizeName(
            title.Length == 0 ? Lang.T("publish.assetsFolderPlaceholder") : title);
        AssetsInfoText.Text = Lang.T("publish.assetsInfo", folder);
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
