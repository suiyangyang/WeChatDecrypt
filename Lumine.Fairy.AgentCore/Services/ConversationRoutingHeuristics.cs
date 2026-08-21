using Lumine.Fairy.Models;
using System.Text.RegularExpressions;

namespace Lumine.Fairy.Services;

public static class ConversationRoutingHeuristics
{
    private static readonly string[] DesktopAppAliases =
    [
        "记事本", "notepad",
        "资源管理器", "文件资源管理器", "explorer",
        "edge", "msedge", "microsoft edge", "浏览器",
        "wechat", "weixin", "微信",
        "vscode", "vs code", "code",
        "vs 2022", "visual studio 2022",
        "vs 2026", "visual studio 2026"
    ];

    public static bool IsLikelyFileOpsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cmd = input.Trim();
        if (ContainsOpenVerb(cmd))
        {
            return ContainsExplicitFileTarget(cmd);
        }

        return ContainsAny(cmd, "删除", "移除", "重命名", "改名", "复制", "移动", "新建", "创建")
            && ContainsExplicitFileTarget(cmd);
    }

    public static bool IsLikelyWeChatOpsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains("微信", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cmd = input.Trim();
        var mentionsChat = cmd.Contains("聊天", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("聊天记录", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("会话", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("消息", StringComparison.OrdinalIgnoreCase);
        var mentionsHarvest = cmd.Contains("抓取", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("采集", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("读取", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("总结", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("汇总", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("摘要", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("分析", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("导出", StringComparison.OrdinalIgnoreCase);

        return mentionsChat && mentionsHarvest;
    }

    public static bool IsLikelyShellCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cmd = input.Trim();
        if (Regex.IsMatch(cmd, @"\b(git\s+status|dotnet\s+--info|dotnet\s+build|dotnet\s+new|docker\s+(pull|create|run|rmi|save)|get-location|get-childitem|pwd|dir|ls)\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return cmd.Contains("执行 git status", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看 git 状态", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看当前目录", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("当前目录", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("列出目录内容", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("当前路径", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("工作路径", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("目录里有什么", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("仓库有没有改动", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("工作区状态", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查询 dotnet --info", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看 dotnet 信息", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("sdk 版本", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("运行时信息", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("dotnet 环境", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("执行 dotnet build", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("编译项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("构建项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("docker", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("下载镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("拉取镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("部署镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("运行镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("列出镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("列出容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("停止容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("删除容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("移除容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("删除镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("移除镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("导出镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("保存镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("另存为", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 wpf", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winform", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winforms", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 console", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 classlib", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建类库", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建类库项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 web api", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 asp_net_core_web_api", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 avalonia", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建控制台项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 classlib 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 wpf 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winform 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 avalonia 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 web api 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("命令行", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("终端", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("shell", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExplicitPaddleOcrCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cmd = input.Trim();
        var mentionsPaddle = Regex.IsMatch(
            cmd,
            @"\bpaddle\s*ocr(?:\s*[- ]?vl)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || cmd.Contains("PaddleOCR", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("Paddle OCR", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("PaddleOCR-VL", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("Paddle OCR VL", StringComparison.OrdinalIgnoreCase);

        if (!mentionsPaddle)
        {
            return false;
        }

        return ContainsAny(cmd,
            "ocr",
            "识别",
            "提取文字",
            "提取文本",
            "文字识别",
            "文本识别",
            "识别文字",
            "识别文本",
            "图片文字",
            "图中文字",
            "读图",
            "截图",
            "当前窗口",
            "整屏",
            "全屏");
    }

    public static bool IsLikelyDesktopCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cmd = input.Trim();
        var hasStructuredDesktopTarget = cmd.Contains("target={", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("target = {", StringComparison.OrdinalIgnoreCase);
        var isQuotedTextInput = Regex.IsMatch(
                cmd,
                @"(?:^(?:在)?输入(?:到当前焦点|到当前窗口|到当前输入框)?\s*[：:]?\s*[\""“]).+(?:[\""”]$)",
                RegexOptions.IgnoreCase)
            || Regex.IsMatch(
                cmd,
                @"(?:把\s*[\""“]).+(?:[\""”]\s*输入到(?:当前焦点|当前窗口|当前输入框))",
                RegexOptions.IgnoreCase)
            || Regex.IsMatch(
                cmd,
                @"(?:向(?:当前焦点|当前窗口|当前输入框)输入\s*[\""“]).+(?:[\""”])",
                RegexOptions.IgnoreCase);

        return cmd.Contains("截图", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
            || (cmd.Contains("等待", StringComparison.OrdinalIgnoreCase) && cmd.Contains("窗口", StringComparison.OrdinalIgnoreCase))
            || (cmd.Contains("激活", StringComparison.OrdinalIgnoreCase) && cmd.Contains("窗口", StringComparison.OrdinalIgnoreCase))
            || Regex.IsMatch(cmd, @"^(切换到|切到|切换至)\s*.+窗口?$", RegexOptions.IgnoreCase)
            || (hasStructuredDesktopTarget && (cmd.Contains("点击", StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("双击", StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("输入", StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("快捷键", StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("滚动", StringComparison.OrdinalIgnoreCase)))
            || isQuotedTextInput
            || Regex.IsMatch(cmd, @"^(保存|保存一下|请保存|帮我保存)(当前窗口|当前窗口内容|当前内容|当前文档|当前页面|当前文件)?$", RegexOptions.IgnoreCase)
            || IsLikelyDesktopAppOpenCommand(cmd);
    }

    public static bool IsLikelyDesktopAppOpenCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (!ContainsOpenVerb(trimmed))
        {
            return false;
        }

        var candidate = NormalizeOpenTarget(trimmed);
        return IsKnownDesktopApplicationAlias(candidate);
    }

    public static bool IsKnownDesktopApplicationAlias(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = NormalizeOpenTarget(input);
        return DesktopAppAliases.Any(alias => string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLikelyContextualDesktopCommand(string input, SubAgentSkillContext context)
    {
        if (context.PreviousResolvedTarget?.ResolvedBounds is not { IsEmpty: false }
            || string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return Regex.IsMatch(input.Trim(), @"^(点击|双击)(?:\s|$)", RegexOptions.IgnoreCase);
    }

    public static bool ShouldPreferShell(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cmd = input.Trim();
        if (Regex.IsMatch(cmd, @"^(删除|移除)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return cmd.Contains("当前目录", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("工作目录", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("当前路径", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("目录里有什么", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("docker", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("下载镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("拉取镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("部署镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("运行镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("列出镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("查看容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("列出容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("停止容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("删除容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("移除容器", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("删除镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("移除镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("导出镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("保存镜像", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("另存为", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 wpf", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winform", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winforms", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 console", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 classlib", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建类库", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建类库项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 avalonia", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 web api", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 asp_net_core_web_api", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建控制台项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 classlib 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 avalonia 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 wpf 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 winform 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("创建 web api 项目", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("仓库有没有改动", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("工作区状态", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("sdk 版本", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("运行时信息", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(cmd, @"\b(git\s+status|dotnet\s+--info|dotnet\s+build|dotnet\s+new|docker\s+(pull|create|run|rmi|save)|get-location|get-childitem|pwd|dir|ls)\b", RegexOptions.IgnoreCase);
    }

    private static bool ContainsOpenVerb(string input)
    {
        return ContainsAny(input, "打开", "请打开", "帮我打开", "打开一下");
    }

    private static string NormalizeOpenTarget(string input)
    {
        var normalized = input.Trim();
        normalized = Regex.Replace(normalized, @"^(打开|打开一下|请打开|帮我打开)\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = normalized.Trim('：', ':', '“', '”', '"', '\'', '。', '，', ',', '！', '!', '？', '?');
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    private static bool ContainsExplicitFileTarget(string input)
    {
        return ContainsAny(input, "文件夹", "文件", "目录");
    }

    private static bool ContainsAny(string input, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (input.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}