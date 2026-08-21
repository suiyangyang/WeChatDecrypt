using System.Diagnostics;

namespace Lumine.Fairy.Desktop.Services;

internal static class BuiltInAppLauncher
{
    private static readonly BuiltInAppDefinition ContactManagement = new(
        DisplayName: "联系人管理",
        ExecutableFileName: "Lumine.Fairy.ContactManagement.App.exe",
        ToolsDirectoryName: "contact-management",
        ProjectDirectoryName: "Lumine.Fairy.ContactManagement.App");

    private static readonly BuiltInAppDefinition TodoList = new(
        DisplayName: "Todo / 日报",
        ExecutableFileName: "Lumine.Fairy.TodoList.App.exe",
        ToolsDirectoryName: "todolist",
        ProjectDirectoryName: "Lumine.Fairy.TodoList.App");

    public static BuiltInAppLaunchResult LaunchContactManagement()
        => Launch(ContactManagement);

    public static BuiltInAppLaunchResult LaunchTodoList()
        => Launch(TodoList);

    private static BuiltInAppLaunchResult Launch(BuiltInAppDefinition definition)
    {
        var executablePath = ResolveExecutablePath(definition);
        if (executablePath is null)
        {
            return BuiltInAppLaunchResult.Failed(
                $"未找到{definition.DisplayName}应用。请先构建对应项目，或重新构建 Lumine.Fairy.Desktop。\n" +
                $"期望文件：{definition.ExecutableFileName}");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
            };
            using var process = Process.Start(startInfo);
            return process is null
                ? BuiltInAppLaunchResult.Failed($"{definition.DisplayName}启动失败：未能创建进程。")
                : BuiltInAppLaunchResult.Succeeded(executablePath, process.Id);
        }
        catch (Exception ex)
        {
            return BuiltInAppLaunchResult.Failed($"{definition.DisplayName}启动失败：{ex.Message}");
        }
    }

    private static string? ResolveExecutablePath(BuiltInAppDefinition definition)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "tools", definition.ToolsDirectoryName, definition.ExecutableFileName),
            Path.Combine(baseDirectory, definition.ExecutableFileName),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "..", definition.ProjectDirectoryName, "bin", "Debug", "net10.0-windows", definition.ExecutableFileName),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "..", definition.ProjectDirectoryName, "bin", "Release", "net10.0-windows", definition.ExecutableFileName),
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private sealed record BuiltInAppDefinition(
        string DisplayName,
        string ExecutableFileName,
        string ToolsDirectoryName,
        string ProjectDirectoryName);
}

internal sealed class BuiltInAppLaunchResult
{
    public bool Success { get; private init; }

    public string ExecutablePath { get; private init; } = string.Empty;

    public int? ProcessId { get; private init; }

    public string FailureReason { get; private init; } = string.Empty;

    public static BuiltInAppLaunchResult Succeeded(string executablePath, int processId)
        => new() { Success = true, ExecutablePath = executablePath, ProcessId = processId };

    public static BuiltInAppLaunchResult Failed(string failureReason)
        => new() { FailureReason = failureReason };
}
