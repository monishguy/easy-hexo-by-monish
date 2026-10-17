using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHexo.Helpers;

/// <summary>窗口 / HWND 相关的小工具。</summary>
public static class WindowHelper
{
    public static IntPtr GetHwnd()
    {
        var window = App.MainWindow;
        if (window is null)
        {
            throw new InvalidOperationException("主窗口尚未创建。");
        }

        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    public static XamlRoot? GetXamlRoot() => App.MainWindow?.Content?.XamlRoot;

    /// <summary>打开文件夹选择器，返回所选路径；取消则返回 null。</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <summary>打开文件选择器（.md / .txt，可多选）。</summary>
    public static async Task<IReadOnlyList<Windows.Storage.StorageFile>> PickArticleFilesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".txt");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd());
        return await picker.PickMultipleFilesAsync();
    }
}

/// <summary>对话框 / 外壳跳转。</summary>
public static class DialogHelper
{
    public static async Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText)
    {
        var xamlRoot = WindowHelper.GetXamlRoot();
        if (xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

public static class ShellHelper
{
    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Services.AppLog.Write("打开目录失败", ex);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.AppLog.Write("打开链接失败", ex);
        }
    }
}
