using Microsoft.Win32;
using System.Security;

namespace Lumine.Fairy.Desktop.Services;

internal sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumine.Fairy";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName)?.ToString());
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            throw new InvalidOperationException("无法打开 Windows 当前用户启动项。");
        }

        if (enabled)
        {
            key.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定 Fairy 程序路径。");
        }

        var commandLine = Environment.GetCommandLineArgs();
        if (IsDotNetHost(processPath) && commandLine.Length > 1)
        {
            return string.Join(" ", new[] { QuoteArgument(processPath) }.Concat(commandLine.Skip(1).Select(QuoteArgument)));
        }

        return QuoteArgument(processPath);
    }

    private static bool IsDotNetHost(string processPath)
        => string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";
}
