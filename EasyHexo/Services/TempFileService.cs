using Windows.Storage;

namespace EasyHexo.Services;

/// <summary>
/// 临时文件区。拖入的 .txt 会先复制到这里并改名成 .md，再按 Markdown 流程处理，
/// 不修改用户原始 .txt 文件。
/// </summary>
public static class TempFileService
{
    private static string? _sessionDirectory;

    public static string SessionDirectory
    {
        get
        {
            if (_sessionDirectory is null)
            {
                _sessionDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "EasyHexo",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(_sessionDirectory);
            }

            return _sessionDirectory;
        }
    }

    /// <summary>把任意 StorageFile 复制到临时目录并改名为 .md，返回新路径。</summary>
    public static async Task<string> ImportAsMarkdownAsync(StorageFile file)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(SessionDirectory);
        var baseName = PathHelper.SanitizeName(Path.GetFileNameWithoutExtension(file.Name), "article");
        var copied = await file.CopyAsync(folder, baseName + ".md", NameCollisionOption.GenerateUniqueName);
        return copied.Path;
    }

    public static bool IsInSessionDirectory(string path) =>
        PathHelper.IsUnder(path, SessionDirectory);
}
