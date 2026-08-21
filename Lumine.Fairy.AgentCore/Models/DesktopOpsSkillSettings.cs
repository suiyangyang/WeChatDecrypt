using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 DesktopOps 第一版的最小技能配置。
/// </summary>
public sealed class DesktopOpsSkillSettings
{
    /// <summary>
    /// 获取或设置各代理模式下允许操作的应用或进程名列表。
    /// </summary>
    public Dictionary<AgentMode, List<string>> AllowedApplicationNames { get; set; } = new();

    /// <summary>
    /// 获取或设置默认超时时间（秒）。
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 获取或设置默认最大重试次数。
    /// </summary>
    public int MaxRetryCount { get; set; } = 1;

    /// <summary>
    /// 获取或设置从哪个风险等级开始必须进入确认流。
    /// </summary>
    public DesktopActionRiskLevel ConfirmationRiskLevel { get; set; } = DesktopActionRiskLevel.DangerousWrite;

    /// <summary>
    /// 获取或设置是否启用视觉分析抽象层。
    /// 默认关闭，避免将其变成系统强依赖。
    /// </summary>
    public bool EnableVisionAnalysis { get; set; }

    /// <summary>
    /// 获取或设置是否允许视觉分析层使用外部 VL/多模态端点。
    /// 第一版默认关闭，仅保留接入点。
    /// </summary>
    public bool AllowExternalVisionModel { get; set; }

    /// <summary>
    /// 获取或设置可选的视觉分析模型端点配置。
    /// 当前仅作为最小兼容入口保留，不构成系统强依赖。
    /// </summary>
    public LlmEndpointConfig? VisionEndpoint { get; set; }

    /// <summary>
    /// 获取或设置本地 OCR 后端。
    /// </summary>
    public DesktopLocalOcrBackend LocalOcrBackend { get; set; } = DesktopLocalOcrBackend.RapidOcr;

    /// <summary>
    /// 获取或设置本地 OCR 专用 Python 路径；为空时自动探测。
    /// </summary>
    public string LocalOcrPythonExecutable { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 PaddleOCR-VL 在本地模式下使用的设备标识，例如 cpu / gpu / xpu。
    /// </summary>
    public string PaddleOcrVlDevice { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 PaddleOCR-VL 远端推理服务地址；为空时表示走本地 Python 推理。
    /// </summary>
    public string PaddleOcrVlServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 PaddleOCR-VL 远端推理后端类型，例如 vllm-server。
    /// </summary>
    public string PaddleOcrVlBackend { get; set; } = "vllm-server";

    /// <summary>
    /// 获取或设置 PaddleOCR-VL 远端推理的模型名称。
    /// </summary>
    public string PaddleOcrVlApiModelName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 PaddleOCR-VL 远端推理的 API Key。
    /// </summary>
    public string PaddleOcrVlApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置截图或视觉工件的默认输出目录。
    /// </summary>
    public string SnapshotDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置截图文件默认保留小时数。
    /// </summary>
    public int SnapshotRetentionHours { get; set; }

    /// <summary>
    /// 获取或设置截图目录默认最大文件数。
    /// </summary>
    public int MaxSnapshotFileCount { get; set; } = 100;

    /// <summary>
    /// 获取或设置微信聊天读取默认回溯天数。
    /// </summary>
    public int WeChatHistoryLookbackDays { get; set; } = 7;

    /// <summary>
    /// 获取或设置单次联系人列表扫描最多识别的人数。
    /// </summary>
    public int WeChatMaxContactScanCount { get; set; } = 30;

    /// <summary>
    /// 获取或设置单个微信会话允许的最大滚动轮数。
    /// 用于控制向历史方向扫描聊天记录时的预算上限。
    /// </summary>
    public int WeChatMaxScrollRoundsPerConversation { get; set; } = 100;

    /// <summary>
    /// 获取或设置微信联系人/群组到项目的映射。
    /// </summary>
    public List<WeChatProjectBinding> WeChatProjectBindings { get; set; } = [];

    /// <summary>
    /// 获取或设置按程序定义的内容识别策略。
    /// 保持为空时，系统沿用原有默认行为。
    /// </summary>
    public List<DesktopAppRecognitionProfile> AppRecognitionProfiles { get; set; } = [];
}