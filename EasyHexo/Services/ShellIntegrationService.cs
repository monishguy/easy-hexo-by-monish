using Microsoft.Win32;

namespace EasyHexo.Services;

/// <summary>
/// 资源管理器右键菜单：给 .md / .txt 添加“发布到 Hexo 博客”。
/// 使用经典的 HKCU shell verb（Win10 直接显示；Win11 在“显示更多选项”中显示）。
/// </summary>
public static class ShellIntegrationService
{
    private static readonly string[] Extensions = { ".md", ".txt" };

    private static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "EasyHexo.exe");

    private static string CommandLine => $"\"{ExecutablePath}\" --publish \"%1\"";

    private static string VerbKey(string extension) =>
        $"Software\\Classes\\SystemFileAssociations\\{extension}\\shell\\EasyHexoPublish";

    public static bool IsRegistered()
    {
        try
        {
            foreach (var extension in Extensions)
            {
                using var key = Registry.CurrentUser.OpenSubKey($"{VerbKey(extension)}\\command");
                if (key?.GetValue(null) as string != CommandLine)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Register(out string? error)
    {
        error = null;
        try
        {
            var title = Localization.Lang.T("shell.publishToHexo");
            foreach (var extension in Extensions)
            {
                using var verb = Registry.CurrentUser.CreateSubKey(VerbKey(extension));
                if (verb is null)
                {
                    error = "无法创建注册表项";
                    return false;
                }

                verb.SetValue(null, title);
                verb.SetValue("Icon", $"\"{ExecutablePath}\"");
                verb.SetValue("MultiSelectModel", "Single");

                using var command = verb.CreateSubKey("command");
                command?.SetValue(null, CommandLine);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("注册右键菜单失败", ex);
            error = ex.Message;
            return false;
        }
    }

    public static bool Unregister(out string? error)
    {
        error = null;
        try
        {
            foreach (var extension in Extensions)
            {
                using var baseKey = Registry.CurrentUser.OpenSubKey(
                    $"Software\\Classes\\SystemFileAssociations\\{extension}\\shell",
                    writable: true);
                baseKey?.DeleteSubKeyTree("EasyHexoPublish", throwOnMissingSubKey: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("移除右键菜单失败", ex);
            error = ex.Message;
            return false;
        }
    }
}
