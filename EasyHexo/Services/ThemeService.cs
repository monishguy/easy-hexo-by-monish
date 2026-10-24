using Microsoft.UI.Xaml;

namespace EasyHexo.Services;

/// <summary>外观主题（黑 / 亮 / 跟随系统）的取值与转换。</summary>
public static class ThemeService
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static string Normalize(string? theme) => theme switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };

    /// <summary>转成 WinUI 的 <see cref="ElementTheme"/>；跟随系统为 Default。</summary>
    public static ElementTheme ToElementTheme(string? theme) => Normalize(theme) switch
    {
        Light => ElementTheme.Light,
        Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
