using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次截图或视觉快照的引用信息。
/// </summary>
public sealed class ScreenSnapshotRef
{
    /// <summary>
    /// 获取或设置快照唯一标识。
    /// </summary>
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置截图落盘路径。
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置内存态图像引用键。
    /// </summary>
    public string MemoryKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置从视觉/OCR 中提取出的文本摘要。
    /// </summary>
    public string OcrText { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置截图宽度。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 获取或设置截图高度。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 获取或设置本次截图对应的屏幕区域；全屏截图时通常为整个虚拟桌面。
    /// </summary>
    public ScreenRegion? CaptureRegion { get; set; }

    /// <summary>
    /// 获取或设置截图关联的窗口描述；全屏截图时可为空。
    /// </summary>
    public WindowDescriptor? Window { get; set; }

    /// <summary>
    /// 获取或设置快照采集时间（UTC）。
    /// </summary>
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}