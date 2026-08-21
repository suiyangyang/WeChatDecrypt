using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lumine.Fairy.Desktop.ViewModels;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool boolean && !boolean;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool boolean && !boolean;
}

public sealed class BooleanToBrushConverter : IValueConverter
{
    public string TrueColor { get; set; } = "#E3EEFF";
    public string FalseColor { get; set; } = "#FFFFFF";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Avalonia.Media.Color.Parse(value is true ? TrueColor : FalseColor));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
