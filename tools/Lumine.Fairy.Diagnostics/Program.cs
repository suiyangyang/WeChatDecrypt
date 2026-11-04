using System.Diagnostics;
using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

namespace Lumine.Fairy.Diagnostics;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = DiagnosticsOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(options.ImportCompanyDirectoriesPath))
        {
            var store = new ContactManagementSqliteStore(new ContactManagementStorageOptions());
            var importer = new DirectoryCompanyImportService(store);
            var importResult = await importer.ImportAsync(options.ImportCompanyDirectoriesPath);
            Console.WriteLine($"[完成] 客户公司导入：新增 {importResult.AddedCount} 家，已存在 {importResult.ExistingCount} 家。");
            foreach (var name in importResult.AddedNames)
                Console.WriteLine($"[新增公司] {name}");
            return 0;
        }

        Console.WriteLine("QwenPaw Remote PC 微信分析诊断");
        Console.WriteLine($"超时：{options.TimeoutSeconds} 秒；分析模式：{(options.RunAnalysis ? "是" : "否")}");

        using var weChat = new WeChatDataAnalysisClient(
            requestTimeout: TimeSpan.FromSeconds(options.TimeoutSeconds),
            trace: message => Console.WriteLine($"[HTTP] {message}"));
        try
        {
            var accounts = await RunStageAsync("读取微信账号", options.TimeoutSeconds, token => weChat.GetAccountsAsync(token));
            if (accounts.Count == 0)
            {
                Console.Error.WriteLine("[失败] 没有发现实时可用微信账号。");
                return 2;
            }

            foreach (var account in accounts)
                Console.WriteLine($"[账号] {account.Id} / {account.DisplayName}");

            var accountToUse = accounts.FirstOrDefault(x => string.Equals(x.Id, options.AccountId, StringComparison.OrdinalIgnoreCase))
                               ?? accounts.First();
            Console.WriteLine($"[选择] {accountToUse.Id} / {accountToUse.DisplayName}");

            var sessions = await RunStageAsync("读取微信会话", options.TimeoutSeconds, token => weChat.GetSessionsAsync(accountToUse.Id, token));
            Console.WriteLine($"[完成] 会话数量：{sessions.Count}");
            if (sessions.Count == 0)
            {
                Console.Error.WriteLine("[失败] 当前账号没有可用会话。");
                return 3;
            }

            var session = sessions.FirstOrDefault(x => string.Equals(x.Username, options.ConversationId, StringComparison.OrdinalIgnoreCase))
                          ?? sessions.First();
            Console.WriteLine($"[选择] 会话：{session.DisplayName} ({session.Username})");

            var messages = await RunStageAsync("读取微信消息", options.TimeoutSeconds, token => weChat.GetLatestMessagesAsync(accountToUse.Id, session.Username, options.MessageLimit, token));
            Console.WriteLine($"[完成] 消息数量：{messages.Count}");
            if (messages.Count == 0 || !options.RunAnalysis) return 0;

            var username = ReadEnvironment("QwenPawUser");
            var password = ReadEnvironment("QwenPawPwd");
            var serverUrl = ReadEnvironment("QwenPawUrl");
            if (string.IsNullOrWhiteSpace(serverUrl)) serverUrl = "http://127.0.0.1:8088";
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("未配置 QwenPawUser 或 QwenPawPwd，无法执行 --analyze。");

            using var qwenPaw = new QwenPawClient(new ProtectedTokenStore());
            await RunStageAsync("登录 QwenPaw", options.TimeoutSeconds, async token =>
            {
                await qwenPaw.LoginAsync(serverUrl, username, password, token);
                return true;
            });
            var agents = await RunStageAsync("读取 QwenPaw Agent", options.TimeoutSeconds, token => qwenPaw.GetAgentsAsync(token));
            var agent = agents.FirstOrDefault(x => x.Enabled && x.Id.Equals(options.AgentId, StringComparison.OrdinalIgnoreCase))
                        ?? agents.FirstOrDefault(x => x.Enabled)
                        ?? throw new InvalidOperationException("没有可用的 QwenPaw Agent。");

            using var notificationStore = new SqliteNotificationStore();
            using var todoStore = new SqliteTodoStore();
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(weChat, qwenPaw, notifications, todos);
            coordinator.ProgressChanged += (_, progress) =>
            {
                var current = progress.Stages.FirstOrDefault(x => x.Status == "running")
                              ?? progress.Stages.FirstOrDefault(x => x.Status == "error")
                              ?? progress.Stages.LastOrDefault(x => x.Status == "completed");
                if (current is not null)
                    Console.WriteLine($"[分析] {current.Title}：{current.Detail}");
                if (current?.Status == "error") Console.Error.WriteLine($"[错误] {current.Error}");
            };

            await coordinator.AnalyzeMessagesAsync(accountToUse, session, messages, agent.Id);
            Console.WriteLine("[完成] 微信分析完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[失败] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<T> RunStageAsync<T>(string title, int timeoutSeconds, Func<CancellationToken, Task<T>> action)
    {
        Console.WriteLine($"[开始] {title}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await action(timeout.Token);
            timer.Stop();
            Console.WriteLine($"[完成] {title}，耗时 {timer.Elapsed.TotalSeconds:0.00}s");
            return result;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            timer.Stop();
            throw new TimeoutException($"{title} 超过 {timeoutSeconds} 秒未返回，耗时 {timer.Elapsed.TotalSeconds:0.00}s。");
        }
    }

    private static string ReadEnvironment(string name)
        => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
           ?? string.Empty;

    private static void PrintHelp()
    {
        Console.WriteLine("用法：dotnet run --project Lumine.Fairy/tools/Lumine.Fairy.Diagnostics -- [选项]");
        Console.WriteLine("  --account <id>       指定微信账号");
        Console.WriteLine("  --conversation <id>  指定会话 username");
        Console.WriteLine("  --limit <n>          读取消息数量，默认 20");
        Console.WriteLine("  --timeout <seconds>  每个阶段超时，默认 30");
        Console.WriteLine("  --agent <id>         分析时使用的 Agent，默认 default");
        Console.WriteLine("  --analyze            继续调用 QwenPaw 执行真实分析");
        Console.WriteLine("  --import-company-directories <path>  将 path 下一级目录导入为公司");
    }
}

internal sealed class DiagnosticsOptions
{
    public string? AccountId { get; private set; }
    public string? ConversationId { get; private set; }
    public string AgentId { get; private set; } = "default";
    public int MessageLimit { get; private set; } = 20;
    public int TimeoutSeconds { get; private set; } = 30;
    public bool RunAnalysis { get; private set; }
    public string? ImportCompanyDirectoriesPath { get; private set; }
    public bool ShowHelp { get; private set; }

    public static DiagnosticsOptions Parse(string[] args)
    {
        var result = new DiagnosticsOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h": result.ShowHelp = true; break;
                case "--analyze": result.RunAnalysis = true; break;
                case "--import-company-directories": result.ImportCompanyDirectoriesPath = Next(args, ref i); break;
                case "--account": result.AccountId = Next(args, ref i); break;
                case "--conversation": result.ConversationId = Next(args, ref i); break;
                case "--agent": result.AgentId = Next(args, ref i); break;
                case "--limit": result.MessageLimit = Math.Clamp(int.Parse(Next(args, ref i)), 1, 50); break;
                case "--timeout": result.TimeoutSeconds = Math.Clamp(int.Parse(Next(args, ref i)), 1, 600); break;
                default: throw new ArgumentException($"未知参数：{args[i]}");
            }
        }
        return result;
    }

    private static string Next(string[] args, ref int index)
        => ++index < args.Length ? args[index] : throw new ArgumentException("参数缺少值。");
}
