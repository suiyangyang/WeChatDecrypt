using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Desktop.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public ToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer();
    }

    public ToastWindow(NotificationRecord record)
    {
        InitializeComponent();
        DataContext = record;
        PlaceBottomRight();
        Opened += (_, _) => PlaceBottomRight();
        PointerPressed += (_, _) => Close();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(record.DurationSeconds, 3, 60)) };
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    private void PlaceBottomRight()
    {
        var area = Screens.Primary?.WorkingArea;
        if (area is null) return;
        Position = new PixelPoint(area.Value.Right - (int)Width - 24, area.Value.Bottom - (int)Height - 24);
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
