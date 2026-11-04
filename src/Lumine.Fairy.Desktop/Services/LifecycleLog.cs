using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Lumine.Fairy.Desktop.Services;

/// <summary>
/// 进程生命周期日志：记录启动、心跳、退出与未处理异常，用于事后区分
/// “闪退”“正常退出”“被外部结束”三种情况。日志目录：%LOCALAPPDATA%\Lumine.Fairy\logs
/// </summary>
internal static class LifecycleLog
{
    private const string CrashFilePrefix = "crash-";
    private const string UnknownExitReason = "未记录退出原因，进程可能被外部结束（关闭终端、停止调试、taskkill、注销或断电）";

    private static readonly object SyncRoot = new();
    private static readonly string RootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lumine.Fairy");

    private static string? _exitReason;
    private static System.Threading.Timer? _heartbeatTimer;
    private static int _initialized;

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    /// <summary>安装未处理异常与进程退出钩子，并写入本次启动记录。需要在入口处最先调用。</summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) => Crash(
            "未处理异常",
            args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "未知异常对象"),
            args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Crash("未观察的 Task 异常", args.Exception, isTerminating: false);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteExitRecord();

        WriteStartupRecord();
    }

    public static void Info(string message) => Append("INFO", message);

    public static void Warn(string message, Exception? exception = null)
        => Append("WARN", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    // 初始化可能涉及登录或业务数据，只记录诊断元数据，不输出异常消息或 Data。
    public static void InitializationFailure(string stage, Exception exception)
    {
        var details = new StringBuilder($"初始化阶段失败：{stage}");
        for (var current = exception; current is not null; current = current.InnerException)
        {
            details.AppendLine();
            details.Append($"异常类型={current.GetType().FullName} HResult=0x{current.HResult:X8}");
            if (current is System.ComponentModel.Win32Exception native)
                details.Append($" NativeErrorCode={native.NativeErrorCode}");
            details.AppendLine();
            details.Append(current.StackTrace);
        }
        Append("WARN", details.ToString());
    }

    /// <summary>记录一次未处理异常：运行日志写一行摘要，完整堆栈另存到独立的 crash 文件。</summary>
    public static void Crash(string source, Exception exception, bool isTerminating)
    {
        var stamp = DateTimeOffset.Now;
        NoteExit($"发生未处理异常（{source}），详见同目录 crash 日志");
        Append("FATAL", $"{source}，进程即将终止：{exception.GetType().Name}: {exception.Message}");
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var fileName = $"{CrashFilePrefix}{stamp:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log";
            File.WriteAllText(
                Path.Combine(LogDirectory, fileName),
                DescribeCrash(source, exception, stamp, isTerminating),
                new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>记录本次退出的触发原因，进程退出钩子会把它写进最终结论。多次调用以首次为准。</summary>
    public static void NoteExit(string reason)
    {
        lock (SyncRoot)
        {
            _exitReason ??= reason;
        }
    }

    /// <summary>周期写入心跳文件；进程被强杀时可用心跳的最后修改时间定位停止时刻。</summary>
    public static void StartHeartbeat(TimeSpan interval)
    {
        WriteHeartbeat();
        lock (SyncRoot)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new System.Threading.Timer(_ => WriteHeartbeat(), null, interval, interval);
        }
    }

    private static void WriteStartupRecord()
    {
#if DEBUG
        const string buildConfiguration = "Debug";
#else
        const string buildConfiguration = "Release";
#endif
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var consoleAttached = GetConsoleWindow() != nint.Zero;
        Append("START", $"Fairy 启动 配置={buildConfiguration} 版本={version}"
            + $" 控制台={(consoleAttached ? "有（可能由终端启动）" : "无")}"
            + $" 可执行文件={Environment.ProcessPath}"
            + $" 工作目录={Environment.CurrentDirectory}"
            + $" 命令行={FormatArguments()}");
    }

    private static void WriteExitRecord()
    {
        string reason;
        lock (SyncRoot)
        {
            reason = _exitReason ?? UnknownExitReason;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        Append("EXIT", $"{reason}；退出码={Environment.ExitCode}");
    }

    private static void WriteHeartbeat()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var target = Path.Combine(LogDirectory, "heartbeat.txt");
            var temp = Path.Combine(LogDirectory, $"heartbeat.{Environment.ProcessId}.tmp");
            File.WriteAllText(temp, $"pid={Environment.ProcessId} {DateTimeOffset.Now:O}", new UTF8Encoding(false));
            File.Move(temp, target, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FormatArguments()
    {
        var arguments = Environment.GetCommandLineArgs();
        return arguments.Length <= 1 ? "(无)" : string.Join(' ', arguments.Skip(1).Select(arg => $"\"{arg}\""));
    }

    private static string DescribeCrash(string source, Exception exception, DateTimeOffset stamp, bool isTerminating)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"时间：{stamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"进程：{Environment.ProcessPath} (pid={Environment.ProcessId})");
        builder.AppendLine($"来源：{source}");
        builder.AppendLine($"进程即将终止：{(isTerminating ? "是" : "否")}");
        builder.AppendLine($"线程：{DescribeThread()}");
        builder.AppendLine($"最后心跳：{ReadHeartbeat()}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private static string DescribeThread()
    {
        var current = Thread.CurrentThread;
        var name = string.IsNullOrEmpty(current.Name) ? "(未命名)" : current.Name;
        return $"{name} (ManagedThreadId={current.ManagedThreadId}, 线程池线程={current.IsThreadPoolThread})";
    }

    private static string ReadHeartbeat()
    {
        try
        {
            var path = Path.Combine(LogDirectory, "heartbeat.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "(无心跳文件)";
        }
        catch (IOException)
        {
            return "(心跳文件不可读)";
        }
    }

    private static void Append(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"runtime-{DateTimeOffset.Now:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} | {level,-5} | pid={Environment.ProcessId} | {message}{Environment.NewLine}";
            lock (SyncRoot)
            {
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
}
