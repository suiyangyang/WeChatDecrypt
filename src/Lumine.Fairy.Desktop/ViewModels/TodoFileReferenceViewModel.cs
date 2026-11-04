using Avalonia.Media;
using Lumine.Fairy.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.Protocol;
using System.Diagnostics;

namespace Lumine.Fairy.Desktop.ViewModels;

public sealed class TodoFileReferenceViewModel
{
    private readonly Action<TodoFileReferenceViewModel>? _remove;

    public TodoFileReferenceViewModel(
        TodoFileReference reference,
        Action<TodoFileReferenceViewModel>? remove = null,
        string? linkColor = null)
    {
        Path = reference.Path;
        IsDirectory = reference.IsDirectory;
        _remove = remove;
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        OpenInExplorerCommand = new AsyncRelayCommand(OpenInExplorerAsync);
        CopyPathCommand = new AsyncRelayCommand(async () => await AppClipboard.TrySetTextAsync(Path));
        RemoveCommand = remove is null ? null : new RelayCommand(() => remove(this));
        LinkBrush = TodoLabelColors.CreateReferenceBrush(linkColor);
    }

    public string Path { get; }
    public bool IsDirectory { get; }
    public string FullPath => Path;
    public string DisplayPath => FormatDisplayPath(Path, IsDirectory);
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand OpenInExplorerCommand { get; }
    public IAsyncRelayCommand CopyPathCommand { get; }
    public RelayCommand? RemoveCommand { get; }
    public SolidColorBrush LinkBrush { get; }

    private Task OpenAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 引用路径可能已被移动或删除；保留卡片中的引用，避免打开失败时影响任务编辑。
        }

        return Task.CompletedTask;
    }

    private Task OpenInExplorerAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = IsDirectory
                    ? QuoteArgument(Path)
                    : $"/select,{QuoteArgument(Path)}",
                UseShellExecute = true,
            };
            Process.Start(startInfo);
        }
        catch (Exception)
        {
            // 路径失效时不影响任务卡片本身的编辑和保存。
        }

        return Task.CompletedTask;
    }

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string FormatDisplayPath(string path, bool isDirectory)
    {
        var normalized = path.Trim().Replace('\\', '/');
        var directorySuffix = isDirectory && !normalized.EndsWith("/", StringComparison.Ordinal) ? "/" : string.Empty;
        return normalized.TrimEnd('/') + directorySuffix;
    }
}
