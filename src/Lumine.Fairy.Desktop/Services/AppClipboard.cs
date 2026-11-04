using Avalonia.Input.Platform;

namespace Lumine.Fairy.Desktop.Services;

/// <summary>
/// 全局剪贴板入口：主窗口初始化时注册 Provider，
/// 供无法直接访问窗口的 ViewModel（如文件引用）复制文本。
/// </summary>
public static class AppClipboard
{
    public static Func<IClipboard?>? Provider { get; set; }

    public static async Task<bool> TrySetTextAsync(string text)
    {
        var clipboard = Provider?.Invoke();
        if (clipboard is null)
            return false;
        await clipboard.SetTextAsync(text);
        return true;
    }
}
