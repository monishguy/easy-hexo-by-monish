using System.Text;

namespace EasyHexo.Services;

/// <summary>
/// 文本文件读写。Hexo 的源文件与 _config.yml 约定为 UTF-8；
/// 写回时保留原有 BOM 状态，避免 diff 出现整文件变化。
/// </summary>
public static class TextFileService
{
    public static string ReadAllText(string path)
    {
        // File.ReadAllText 会检测并剥离 BOM
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static bool HasUtf8Bom(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            var b3 = stream.ReadByte();
            return b1 == 0xEF && b2 == 0xBB && b3 == 0xBF;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteAllText(string path, string text, bool withBom = false)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, text, new UTF8Encoding(withBom));
    }
}
