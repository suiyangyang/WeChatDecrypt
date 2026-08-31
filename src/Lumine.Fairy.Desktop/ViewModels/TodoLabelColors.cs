using Avalonia.Media;
using AvaloniaColor = Avalonia.Media.Color;

namespace Lumine.Fairy.Desktop.ViewModels;

public static class TodoLabelColors
{
    public const string DefaultColor = "#000000";

    public static IReadOnlyList<TodoLabelColorDefinition> CommonColors { get; } =
    [
        new("黑色", DefaultColor),
        new("红色", "#FF3B30"),
        new("橙色", "#FF9500"),
        new("黄色", "#FFCC00"),
        new("绿色", "#34C759"),
        new("蓝色", "#007AFF"),
        new("紫色", "#AF52DE"),
        new("灰色", "#8E8E93"),
    ];

    public static string Normalize(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return DefaultColor;

        try
        {
            _ = AvaloniaColor.Parse(color.Trim());
            return color.Trim();
        }
        catch (FormatException)
        {
            return DefaultColor;
        }
    }

    public static SolidColorBrush CreateBrush(string? color)
    {
        try
        {
            return new SolidColorBrush(AvaloniaColor.Parse(Normalize(color)));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(AvaloniaColor.Parse(DefaultColor));
        }
    }
}

public sealed record TodoLabelColorDefinition(string Name, string Color);
