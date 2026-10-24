using System.Collections.ObjectModel;
using System.ComponentModel;
using EasyHexo.Localization;
using Microsoft.UI.Xaml;

namespace EasyHexo.Models;

/// <summary>资源分组：图片 / 文件 / 未识别项。</summary>
public enum AssetGroupKind
{
    Images,
    Files,
    Unknown,
}

/// <summary>资源处理面板里的一行（一个 Markdown 链接）。</summary>
public sealed class AssetItemVm : INotifyPropertyChanged
{
    private bool? _isChecked = true;
    private string? _localPath;

    public string Url { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>UI 自动化 ID。</summary>
    public string AutomationId { get; set; } = string.Empty;

    public string BrowseAutomationId => AutomationId + "-browse";

    public bool IsImage { get; set; }

    /// <summary>是否属于“未识别项”（本机文件找不到，需要用户浏览指定）。</summary>
    public bool IsUnknown { get; set; }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            Raise(nameof(IsChecked));
        }
    }

    public string? LocalPath
    {
        get => _localPath;
        set
        {
            if (_localPath == value)
            {
                return;
            }

            _localPath = value;
            Raise(nameof(LocalPath));
            Raise(nameof(ResolvedText));
            Raise(nameof(ResolvedVisibility));
        }
    }

    public string ResolvedText =>
        string.IsNullOrEmpty(LocalPath) ? string.Empty : Lang.T("publish.assetsResolved", LocalPath);

    /// <summary>只有“未识别项”在用户指定本机文件后，才显示已选择的路径（常规项不重复显示）。</summary>
    public Visibility ResolvedVisibility =>
        IsUnknown && !string.IsNullOrEmpty(LocalPath) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BrowseVisibility => IsUnknown ? Visibility.Visible : Visibility.Collapsed;

    public string BrowseText => Lang.T("publish.browse");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>资源处理面板中的一个分组（图片 / 文件 / 未识别项）。</summary>
public sealed class AssetGroupVm : INotifyPropertyChanged
{
    private bool? _isChecked;
    private bool _isExpanded = true;

    public AssetGroupKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>UI 自动化 ID。</summary>
    public string AutomationId { get; set; } = string.Empty;

    public ObservableCollection<AssetItemVm> Items { get; } = new();

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            Raise(nameof(IsChecked));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            Raise(nameof(IsExpanded));
            Raise(nameof(ItemsVisibility));
            Raise(nameof(ToggleText));
        }
    }

    public Visibility ItemsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string ToggleText => Lang.T(IsExpanded ? "common.collapse" : "common.expand");

    public string CountText => Items.Count == 0
        ? Lang.T("publish.assetsEmptyGroup")
        : Lang.T("publish.assetsCount", Items.Count);

    /// <summary>“记住设置”提示只对图片 / 文件分组显示。</summary>
    public string RememberText =>
        Kind == AssetGroupKind.Unknown ? string.Empty : Lang.T("publish.assetsRemember");

    public Visibility RememberVisibility =>
        Kind == AssetGroupKind.Unknown ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCheckedSilently(bool? value)
    {
        if (_isChecked == value)
        {
            return;
        }

        _isChecked = value;
        Raise(nameof(IsChecked));
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


