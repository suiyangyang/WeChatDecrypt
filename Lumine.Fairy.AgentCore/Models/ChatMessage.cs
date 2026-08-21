using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示用户对一条消息的反馈状态。
/// </summary>
public enum FeedbackType
{
    /// <summary>
    /// 未提供反馈。
    /// </summary>
    None = 0,

    /// <summary>
    /// 表示正向反馈。
    /// </summary>
    Like = 1,

    /// <summary>
    /// 表示负向反馈。
    /// </summary>
    Dislike = 2
}

/// <summary>
/// 表示通知消息的级别，用于统一 toast 与系统消息的语义和样式。
/// </summary>
public enum NotificationLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// 表示通知消息的来源，用于在 UI 中展示来源标签。
/// </summary>
public enum NotificationSource
{
    System = 0,
    Settings = 1,
    Chat = 2,
    Voice = 3,
    WakeWord = 4,
    Index = 5,
    Tool = 6
}

/// <summary>
/// 表示聊天会话中的一条消息，并携带 UI 展示所需的派生状态。
/// </summary>
public sealed partial class ChatMessage : ObservableObject
{
    private static readonly Regex MarkdownImagePattern = new(@"!\[[^\]]*\]\((?<path>[^)\r\n]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 生成 <c>Role</c> 属性的后备字段，表示消息发送者角色，例如 <c>User</c>、<c>Assistant</c> 或 <c>System</c>。
    /// </summary>
    [ObservableProperty]
    private string _role = string.Empty;

    /// <summary>
    /// 生成 <c>Content</c> 属性的后备字段，表示消息正文内容。
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// 生成 <c>NotificationLevel</c> 属性的后备字段，表示系统消息或通知的级别。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemMessageForeground))]
    private NotificationLevel _notificationLevel = NotificationLevel.Info;

    /// <summary>
    /// 生成 <c>NotificationSource</c> 属性的后备字段，表示系统消息或通知的来源。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemSourceLabel))]
    [NotifyPropertyChangedFor(nameof(SystemSourceBackground))]
    [NotifyPropertyChangedFor(nameof(SystemSourceForeground))]
    [NotifyPropertyChangedFor(nameof(SystemSourceIconGlyph))]
    private NotificationSource _notificationSource = NotificationSource.System;

    /// <summary>
    /// 生成 <c>IsActionPanelVisible</c> 属性的后备字段，表示该消息关联的操作面板是否可见。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isActionPanelVisible;

    /// <summary>
    /// 生成 <c>IsCommandMessage</c> 属性的后备字段，标识该消息是否为命令执行消息。
    /// </summary>
    [ObservableProperty]
    private bool _isCommandMessage;

    /// <summary>
    /// 生成 <c>IsCommandFailed</c> 属性的后备字段，标识命令消息是否执行失败。
    /// </summary>
    [ObservableProperty]
    private bool _isCommandFailed;

    /// <summary>
    /// 生成 <c>Feedback</c> 属性的后备字段，保存用户对消息的反馈结果。
    /// </summary>
    [ObservableProperty]
    private FeedbackType _feedback = FeedbackType.None;

    /// <summary>
    /// 生成 <c>Avatar</c> 属性的后备字段，表示消息展示时所使用的头像资源路径。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _avatar = string.Empty;

    private PendingFileAction? _pendingAction;
    private PendingCommandAction? _pendingCommandAction;
    private PendingDesktopAction? _pendingDesktopAction;
    private WeChatPendingActionCard? _weChatPendingActionCard;
    private ShellExecutionCard? _shellExecutionCard;
    private FileOpsExecutionCard? _fileOpsExecutionCard;
    private DesktopExecutionCard? _desktopExecutionCard;
    private WeChatExecutionCard? _weChatExecutionCard;
    private WeChatScanProgressCard? _weChatScanProgressCard;
    private UiTarget? _resolvedDesktopTarget;

    /// <summary>
    /// 待确认操作描述；当值非 <see langword="null"/> 时，消息会以待确认操作卡片样式展示。
    /// </summary>
    [JsonIgnore]
    public PendingFileAction? PendingAction
    {
        get => _pendingAction;
        set
        {
            if (ReferenceEquals(_pendingAction, value))
            {
                return;
            }

            _pendingAction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPendingAction));
            OnPropertyChanged(nameof(HasPendingInteraction));
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantPendingActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
            OnPropertyChanged(nameof(IsAssistantShellExecutionCard));
            OnPropertyChanged(nameof(IsAssistantFileOpsExecutionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
        }
    }

    /// <summary>
    /// 微信待确认卡片；当值非 <see langword="null"/> 时，以微信专用确认卡形式展示。
    /// </summary>
    [JsonIgnore]
    public WeChatPendingActionCard? WeChatPendingActionCard
    {
        get => _weChatPendingActionCard;
        set
        {
            if (ReferenceEquals(_weChatPendingActionCard, value))
            {
                return;
            }

            _weChatPendingActionCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
        }
    }

    /// <summary>
    /// Shell 执行结果卡片；当值非 <see langword="null"/> 时，以结构化任务卡形式展示。
    /// </summary>
    [JsonIgnore]
    public ShellExecutionCard? ShellExecutionCard
    {
        get => _shellExecutionCard;
        set
        {
            if (ReferenceEquals(_shellExecutionCard, value))
            {
                return;
            }

            _shellExecutionCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantShellExecutionCard));
            OnPropertyChanged(nameof(HasAssistantExecutionCard));
        }
    }

    /// <summary>
    /// FileOps 执行结果卡片；当值非 <see langword="null"/> 时，以结构化任务卡形式展示。
    /// </summary>
    [JsonIgnore]
    public FileOpsExecutionCard? FileOpsExecutionCard
    {
        get => _fileOpsExecutionCard;
        set
        {
            if (ReferenceEquals(_fileOpsExecutionCard, value))
            {
                return;
            }

            _fileOpsExecutionCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantFileOpsExecutionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
            OnPropertyChanged(nameof(HasAssistantExecutionCard));
        }
    }

    /// <summary>
    /// 桌面动作执行结果卡片；当值非 <see langword="null"/> 时，以结构化任务卡形式展示。
    /// </summary>
    [JsonIgnore]
    public DesktopExecutionCard? DesktopExecutionCard
    {
        get => _desktopExecutionCard;
        set
        {
            if (ReferenceEquals(_desktopExecutionCard, value))
            {
                return;
            }

            _desktopExecutionCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantDesktopExecutionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
            OnPropertyChanged(nameof(HasAssistantExecutionCard));
        }
    }

    /// <summary>
    /// 微信抓取执行结果卡片；当值非 <see langword="null"/> 时，以微信专用结果卡形式展示。
    /// </summary>
    [JsonIgnore]
    public WeChatExecutionCard? WeChatExecutionCard
    {
        get => _weChatExecutionCard;
        set
        {
            if (ReferenceEquals(_weChatExecutionCard, value))
            {
                return;
            }

            _weChatExecutionCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantDesktopExecutionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
            OnPropertyChanged(nameof(HasAssistantExecutionCard));
        }
    }

    /// <summary>
    /// 微信扫描进度卡片；当值非 <see langword="null"/> 时，以扫描进度气泡形式展示。
    /// </summary>
    [JsonIgnore]
    public WeChatScanProgressCard? WeChatScanProgressCard
    {
        get => _weChatScanProgressCard;
        set
        {
            if (ReferenceEquals(_weChatScanProgressCard, value))
            {
                return;
            }

            _weChatScanProgressCard = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantWeChatScanProgressCard));
        }
    }

    /// <summary>
    /// 待确认命令描述；当值非 <see langword="null"/> 时，消息会以待确认命令卡片样式展示。
    /// </summary>
    [JsonIgnore]
    public PendingCommandAction? PendingCommandAction
    {
        get => _pendingCommandAction;
        set
        {
            if (ReferenceEquals(_pendingCommandAction, value))
            {
                return;
            }

            _pendingCommandAction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPendingCommandAction));
            OnPropertyChanged(nameof(HasPendingInteraction));
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantPendingActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
            OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
        }
    }

    /// <summary>
    /// 待确认桌面动作描述；当值非 <see langword="null"/> 时，消息会以待确认桌面动作卡片样式展示。
    /// </summary>
    [JsonIgnore]
    public PendingDesktopAction? PendingDesktopAction
    {
        get => _pendingDesktopAction;
        set
        {
            if (ReferenceEquals(_pendingDesktopAction, value))
            {
                return;
            }

            _pendingDesktopAction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPendingDesktopAction));
            OnPropertyChanged(nameof(HasPendingInteraction));
            OnPropertyChanged(nameof(IsAssistantPlainText));
            OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
            OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
            OnPropertyChanged(nameof(IsAssistantPendingActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
            OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
        }
    }

    /// <summary>
    /// 获取或设置该消息关联的桌面已解析目标。
    /// </summary>
    [JsonIgnore]
    public UiTarget? ResolvedDesktopTarget
    {
        get => _resolvedDesktopTarget;
        set
        {
            if (ReferenceEquals(_resolvedDesktopTarget, value))
            {
                return;
            }

            _resolvedDesktopTarget = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 获取当前消息是否包含待用户确认的文件操作。
    /// </summary>
    [JsonIgnore]
    public bool IsPendingAction => PendingAction is { IsDone: false };

    /// <summary>
    /// 获取当前消息是否包含待用户确认的命令操作。
    /// </summary>
    [JsonIgnore]
    public bool IsPendingCommandAction => PendingCommandAction is { IsDone: false };

    /// <summary>
    /// 获取当前消息是否包含待用户确认的桌面动作。
    /// </summary>
    [JsonIgnore]
    public bool IsPendingDesktopAction => PendingDesktopAction is { IsDone: false };

    /// <summary>
    /// 获取当前消息是否包含任一待确认交互。
    /// </summary>
    [JsonIgnore]
    public bool HasPendingInteraction => IsPendingAction || IsPendingCommandAction || IsPendingDesktopAction;

    /// <summary>
    /// 获取或设置消息创建时间。
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 生成 <c>ShowTimestampSeparator</c> 属性的后备字段，表示当前消息前方是否显示时间分隔。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _showTimestampSeparator;

    /// <summary>
    /// 生成 <c>TimestampSeparatorText</c> 属性的后备字段，表示消息前方时间分隔的展示文本。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _timestampSeparatorText = string.Empty;

    /// <summary>
    /// 获取当前消息是否来自助手端，且不是系统消息。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistant => !string.Equals(Role, "User", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(Role, "System", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取当前消息是否显示为普通 Fairy 文本气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantPlainText => IsAssistant && !IsCommandMessage && !HasPendingInteraction && !IsAssistantWeChatScanProgressCard;

    /// <summary>
    /// 获取当前消息内容是否需要 Markdown 控件渲染。
    /// </summary>
    [JsonIgnore]
    public bool UsesMarkdownRendering => RequiresMarkdownRendering(Content);

    [JsonIgnore]
    public IReadOnlyList<string> InlineImagePaths => ExtractInlineImagePaths(Content);

    [JsonIgnore]
    public bool HasInlineImages => InlineImagePaths.Count > 0;

    [JsonIgnore]
    public bool IsUserInlineImageMessage => IsUser && HasInlineImages;

    [JsonIgnore]
    public bool IsUserTextBubble => IsUser && !IsCommandMessage && !IsUserInlineImageMessage;

    [JsonIgnore]
    public bool IsUserImageBubble => IsUser && !IsCommandMessage && IsUserInlineImageMessage;

    [JsonIgnore]
    public bool IsUserCommandTextBubble => IsUser && IsCommandMessage && !IsUserInlineImageMessage;

    [JsonIgnore]
    public bool IsUserCommandImageBubble => IsUser && IsCommandMessage && IsUserInlineImageMessage;

    [JsonIgnore]
    public string ContentWithoutInlineImages => RemoveInlineImages(Content);

    [JsonIgnore]
    public bool HasTextContentWithoutInlineImages => !string.IsNullOrWhiteSpace(ContentWithoutInlineImages);

    [JsonIgnore]
    public bool UsesMarkdownRenderingWithoutInlineImages => RequiresMarkdownRendering(ContentWithoutInlineImages);

    [JsonIgnore]
    public bool ShowContentWithoutInlineImagesAsPlainText => HasTextContentWithoutInlineImages && !UsesMarkdownRenderingWithoutInlineImages;

    [JsonIgnore]
    public bool ShowContentWithoutInlineImagesAsMarkdown => HasTextContentWithoutInlineImages && UsesMarkdownRenderingWithoutInlineImages;

    /// <summary>
    /// 获取当前消息是否显示为 Fairy 成功命令气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantCommandSuccessCard => IsAssistant && IsCommandSuccess && !HasPendingInteraction && !IsAssistantShellExecutionCard && !IsAssistantFileOpsExecutionCard && !IsAssistantDesktopExecutionCard && !IsAssistantWeChatExecutionCard && !IsAssistantWeChatScanProgressCard;

    /// <summary>
    /// 获取当前消息是否显示为 Fairy 失败命令气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantCommandFailedCard => IsAssistant && IsCommandFailed && !HasPendingInteraction && !IsAssistantShellExecutionCard && !IsAssistantFileOpsExecutionCard && !IsAssistantDesktopExecutionCard && !IsAssistantWeChatExecutionCard && !IsAssistantWeChatScanProgressCard;

    /// <summary>
    /// 获取当前消息是否显示为 Shell 执行结果卡片。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantShellExecutionCard => IsAssistant && ShellExecutionCard is not null;

    /// <summary>
    /// 获取当前消息是否显示为 FileOps 执行结果卡片。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantFileOpsExecutionCard => IsAssistant && FileOpsExecutionCard is not null;

    /// <summary>
    /// 获取当前消息是否显示为桌面动作执行结果卡片。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantDesktopExecutionCard => IsAssistant && DesktopExecutionCard is not null && WeChatExecutionCard is null;

    /// <summary>
    /// 获取当前消息是否显示为任一复杂执行结果卡片。
    /// </summary>
    [JsonIgnore]
    public bool HasAssistantExecutionCard => IsAssistantShellExecutionCard || IsAssistantFileOpsExecutionCard || IsAssistantDesktopExecutionCard || IsAssistantWeChatExecutionCard;

    /// <summary>
    /// 获取当前消息是否显示为微信抓取执行结果卡片。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantWeChatExecutionCard => IsAssistant && WeChatExecutionCard is not null;

    /// <summary>
    /// 获取当前消息是否显示为微信扫描进度气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantWeChatScanProgressCard => IsAssistant && WeChatScanProgressCard is not null;

    /// <summary>
    /// 获取当前消息是否显示为 Fairy 待确认操作气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantPendingActionCard => IsAssistant && IsPendingAction;

    /// <summary>
    /// 获取当前消息是否显示为 Fairy 待确认命令气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantPendingCommandActionCard => IsAssistant && IsPendingCommandAction;

    /// <summary>
    /// 获取当前消息是否显示为 Fairy 待确认桌面动作气泡。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantPendingDesktopActionCard => IsAssistant && IsPendingDesktopAction && WeChatPendingActionCard is null;

    /// <summary>
    /// 获取当前消息是否显示为微信待确认卡片。
    /// </summary>
    [JsonIgnore]
    public bool IsAssistantWeChatPendingActionCard => IsAssistant && WeChatPendingActionCard is not null;

    /// <summary>
    /// 获取当前消息是否来自用户。
    /// </summary>
    [JsonIgnore]
    public bool IsUser => string.Equals(Role, "User", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取当前消息是否为系统消息。
    /// </summary>
    [JsonIgnore]
    public bool IsSystem => string.Equals(Role, "System", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取系统消息在聊天列表中的前景色。
    /// </summary>
    [JsonIgnore]
    public string SystemMessageForeground => NotificationPresentation.GetSystemMessageForeground(NotificationLevel);

    /// <summary>
    /// 获取系统消息来源标签文本。
    /// </summary>
    [JsonIgnore]
    public string SystemSourceLabel => NotificationPresentation.GetSourceLabel(NotificationSource);

    /// <summary>
    /// 获取系统消息来源标签背景色。
    /// </summary>
    [JsonIgnore]
    public string SystemSourceBackground => NotificationPresentation.GetSourceBadgeBackground(NotificationSource);

    /// <summary>
    /// 获取系统消息来源标签前景色。
    /// </summary>
    [JsonIgnore]
    public string SystemSourceForeground => NotificationPresentation.GetSourceBadgeForeground(NotificationSource);

    /// <summary>
    /// 获取系统消息来源标签图标。
    /// </summary>
    [JsonIgnore]
    public string SystemSourceIconGlyph => NotificationPresentation.GetSourceIconGlyph(NotificationSource);

    /// <summary>
    /// 获取命令消息是否执行成功。
    /// </summary>
    [JsonIgnore]
    public bool IsCommandSuccess => IsCommandMessage && !IsCommandFailed;

    /// <summary>
    /// 在角色变化时刷新与角色相关的派生属性。
    /// </summary>
    /// <param name="value">更新后的角色值。</param>
    partial void OnRoleChanged(string value)
    {
        OnPropertyChanged(nameof(IsAssistant));
        OnPropertyChanged(nameof(IsAssistantPlainText));
        OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
        OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
        OnPropertyChanged(nameof(IsAssistantPendingActionCard));
        OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
        OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
        OnPropertyChanged(nameof(IsAssistantShellExecutionCard));
        OnPropertyChanged(nameof(IsAssistantFileOpsExecutionCard));
        OnPropertyChanged(nameof(IsAssistantDesktopExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatScanProgressCard));
        OnPropertyChanged(nameof(HasAssistantExecutionCard));
        OnPropertyChanged(nameof(IsUser));
        OnPropertyChanged(nameof(IsUserInlineImageMessage));
        OnPropertyChanged(nameof(IsUserTextBubble));
        OnPropertyChanged(nameof(IsUserImageBubble));
        OnPropertyChanged(nameof(IsUserCommandTextBubble));
        OnPropertyChanged(nameof(IsUserCommandImageBubble));
        OnPropertyChanged(nameof(IsSystem));
    }

    /// <summary>
    /// 在消息正文变化时刷新内容渲染方式。
    /// </summary>
    /// <param name="value">更新后的正文内容。</param>
    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(UsesMarkdownRendering));
        OnPropertyChanged(nameof(InlineImagePaths));
        OnPropertyChanged(nameof(HasInlineImages));
        OnPropertyChanged(nameof(IsUserInlineImageMessage));
        OnPropertyChanged(nameof(IsUserTextBubble));
        OnPropertyChanged(nameof(IsUserImageBubble));
        OnPropertyChanged(nameof(IsUserCommandTextBubble));
        OnPropertyChanged(nameof(IsUserCommandImageBubble));
        OnPropertyChanged(nameof(ContentWithoutInlineImages));
        OnPropertyChanged(nameof(HasTextContentWithoutInlineImages));
        OnPropertyChanged(nameof(UsesMarkdownRenderingWithoutInlineImages));
        OnPropertyChanged(nameof(ShowContentWithoutInlineImagesAsPlainText));
        OnPropertyChanged(nameof(ShowContentWithoutInlineImagesAsMarkdown));
    }

    /// <summary>
    /// 在通知级别变化时刷新系统消息样式。
    /// </summary>
    /// <param name="value">更新后的通知级别。</param>
    partial void OnNotificationLevelChanged(NotificationLevel value)
    {
        OnPropertyChanged(nameof(SystemMessageForeground));
    }

    /// <summary>
    /// 在通知来源变化时刷新系统消息来源样式。
    /// </summary>
    /// <param name="value">更新后的通知来源。</param>
    partial void OnNotificationSourceChanged(NotificationSource value)
    {
        OnPropertyChanged(nameof(SystemSourceLabel));
        OnPropertyChanged(nameof(SystemSourceBackground));
        OnPropertyChanged(nameof(SystemSourceForeground));
        OnPropertyChanged(nameof(SystemSourceIconGlyph));
    }

    /// <summary>
    /// 在命令消息标记变化时刷新命令执行状态。
    /// </summary>
    /// <param name="value">更新后的命令消息标记。</param>
    partial void OnIsCommandMessageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCommandSuccess));
        OnPropertyChanged(nameof(IsAssistantPlainText));
        OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
        OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
        OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
        OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
        OnPropertyChanged(nameof(IsAssistantShellExecutionCard));
        OnPropertyChanged(nameof(IsAssistantFileOpsExecutionCard));
        OnPropertyChanged(nameof(IsAssistantDesktopExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatScanProgressCard));
        OnPropertyChanged(nameof(HasAssistantExecutionCard));
        OnPropertyChanged(nameof(IsUserTextBubble));
        OnPropertyChanged(nameof(IsUserImageBubble));
        OnPropertyChanged(nameof(IsUserCommandTextBubble));
        OnPropertyChanged(nameof(IsUserCommandImageBubble));
    }

    /// <summary>
    /// 在命令失败标记变化时刷新命令执行状态。
    /// </summary>
    /// <param name="value">更新后的失败标记。</param>
    partial void OnIsCommandFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCommandSuccess));
        OnPropertyChanged(nameof(IsAssistantCommandSuccessCard));
        OnPropertyChanged(nameof(IsAssistantCommandFailedCard));
        OnPropertyChanged(nameof(IsAssistantPendingCommandActionCard));
        OnPropertyChanged(nameof(IsAssistantPendingDesktopActionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatPendingActionCard));
        OnPropertyChanged(nameof(IsAssistantShellExecutionCard));
        OnPropertyChanged(nameof(IsAssistantFileOpsExecutionCard));
        OnPropertyChanged(nameof(IsAssistantDesktopExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatExecutionCard));
        OnPropertyChanged(nameof(IsAssistantWeChatScanProgressCard));
        OnPropertyChanged(nameof(HasAssistantExecutionCard));
        OnPropertyChanged(nameof(IsUserTextBubble));
        OnPropertyChanged(nameof(IsUserImageBubble));
        OnPropertyChanged(nameof(IsUserCommandTextBubble));
        OnPropertyChanged(nameof(IsUserCommandImageBubble));
    }

    private static bool RequiresMarkdownRendering(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Contains("```", StringComparison.Ordinal)
            || content.Contains("](", StringComparison.Ordinal)
            || content.Contains("|---", StringComparison.Ordinal)
            || content.Contains("**", StringComparison.Ordinal)
            || content.Contains("__", StringComparison.Ordinal))
        {
            return true;
        }

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal)
                || line.StartsWith("> ", StringComparison.Ordinal)
                || line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)
                || line.StartsWith("+ ", StringComparison.Ordinal)
                || line.StartsWith("1. ", StringComparison.Ordinal)
                || line.StartsWith("2. ", StringComparison.Ordinal)
                || line.StartsWith("3. ", StringComparison.Ordinal)
                || line.StartsWith("4. ", StringComparison.Ordinal)
                || line.StartsWith("5. ", StringComparison.Ordinal)
                || line.Contains("`", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractInlineImagePaths(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return MarkdownImagePattern
            .Matches(content)
            .Select(static match => match.Groups["path"].Value.Trim())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RemoveInlineImages(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (MarkdownImagePattern.IsMatch(trimmed))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        return builder.ToString().Trim();
    }
}
